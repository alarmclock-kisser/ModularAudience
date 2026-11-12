using System.Globalization;
using System.Text;
using DotNetG2P.French;
using DotNetG2P.Spanish;

namespace ModularAudience.Audio.Vocal;

internal readonly record struct VocalPronunciationPhone(
    string Symbol, bool Stressed = false, int Tone = 0, double ToneStart = 0, double ToneEnd = 1)
{
    internal int SourceRepetitionLength { get; init; }
}

internal sealed class VocalUnsupportedPronunciationException : ArgumentException
{
    internal VocalUnsupportedPronunciationException(string message) : base(message) { }
}

internal static class VocalMultilingualPhonemizer
{
    private static readonly Lazy<FrenchG2PEngine> French = new(() =>
        new FrenchG2PEngine(new FrenchG2POptions(includeStress: true)));
    private static readonly Lazy<SpanishG2PEngine> Spanish = new(() => new SpanishG2PEngine());

    internal static bool Supports(VocalLanguage language) => language is
        VocalLanguage.French or VocalLanguage.Spanish or VocalLanguage.Italian or VocalLanguage.Chinese
        or VocalLanguage.Japanese or VocalLanguage.Turkish or VocalLanguage.Arabic or VocalLanguage.Russian;

    internal static bool NeedsCrossScriptFallback(string text) =>
        text.EnumerateRunes().Any(rune => Classify(rune, ScriptGroup.None) is
            ScriptGroup.Arabic or ScriptGroup.Cyrillic or ScriptGroup.Han or ScriptGroup.Kana
            or ScriptGroup.Ipa or ScriptGroup.Other);

    internal static IReadOnlyList<VocalPronunciationPhone> PhonemizeWord(
        VocalLanguage language, string word, CancellationToken cancellationToken)
    {
        try
        {
            return PhonemizeWordCore(language, word, cancellationToken);
        }
        catch (VocalUnsupportedPronunciationException)
        {
            return PhonemizeBestEffort(language, word, cancellationToken);
        }
    }

    private static IReadOnlyList<VocalPronunciationPhone> PhonemizeWordCore(
        VocalLanguage language, string word, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (word.Length > 4096)
            throw new ArgumentException("A word or unbroken text segment exceeds the 4096-character pronunciation limit.", nameof(word));
        string normalized = word.Normalize(NormalizationForm.FormKC).Replace('’', '\'');
        normalized = language == VocalLanguage.Turkish
            ? normalized.Replace('I', 'ı').Replace('İ', 'i').ToLowerInvariant()
            : normalized.ToLowerInvariant();

        var phones = language switch
        {
            VocalLanguage.French => FromIpaEngine(normalized, language, French.Value.ToIPA, cancellationToken),
            VocalLanguage.Spanish => FromIpaEngine(normalized, language, Spanish.Value.ToIPA, cancellationToken),
            VocalLanguage.Italian => VocalLatinLanguages.Italian(normalized, cancellationToken),
            VocalLanguage.Turkish => VocalLatinLanguages.Turkish(normalized, cancellationToken),
            VocalLanguage.Chinese => VocalChinese.Phonemize(normalized, cancellationToken),
            VocalLanguage.Japanese => VocalJapanese.Phonemize(normalized, cancellationToken),
            VocalLanguage.Arabic => VocalArabic.Phonemize(normalized, cancellationToken),
            VocalLanguage.Russian => VocalRussian.Phonemize(normalized, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (phones.Count == 0)
            throw Unsupported($"No pronunciation was found for '{word}' in {language.DisplayName()}.");
        return phones;
    }

    internal static IReadOnlyList<VocalPronunciationPhone> PhonemizeBestEffort(
        VocalLanguage language, string word, CancellationToken cancellationToken)
    {
        var result = new List<VocalPronunciationPhone>();
        var segment = new StringBuilder();
        ScriptGroup current = ScriptGroup.None;

        void FlushSegment()
        {
            if (segment.Length == 0) return;
            string value = segment.ToString();
            segment.Clear();
            AppendScriptSegment(result, language, current, value, cancellationToken);
        }

        foreach (Rune rune in word.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScriptGroup next = Classify(rune, current);
            if (next != current) FlushSegment();
            if (next == ScriptGroup.Other)
            {
                AppendUnknownRune(result, rune, cancellationToken);
                current = ScriptGroup.None;
                continue;
            }
            current = next;
            segment.Append(rune.ToString());
        }
        FlushSegment();

        return result.Count == 0 ? [new VocalPronunciationPhone("unsupported")] : result;
    }

    private static void AppendScriptSegment(
        List<VocalPronunciationPhone> output, VocalLanguage language, ScriptGroup script, string text,
        CancellationToken cancellationToken)
    {
        if (script == ScriptGroup.Ipa)
        {
            VocalIpa.Append(output, text, cancellationToken);
            return;
        }

        VocalLanguage sourceLanguage = script switch
        {
            ScriptGroup.Arabic => VocalLanguage.Arabic,
            ScriptGroup.Cyrillic => VocalLanguage.Russian,
            ScriptGroup.Han => language == VocalLanguage.Japanese ? VocalLanguage.Japanese : VocalLanguage.Chinese,
            ScriptGroup.Kana => VocalLanguage.Japanese,
            _ => language,
        };
        try
        {
            if (Supports(sourceLanguage))
                output.AddRange(PhonemizeWordCore(sourceLanguage, text, cancellationToken));
            else
                output.AddRange(VocalPhonemizer.Phonemize(sourceLanguage, text, cancellationToken)
                    .Select(phone => new VocalPronunciationPhone(phone)));
            return;
        }
        catch (VocalUnsupportedPronunciationException)
        {
            foreach (Rune rune in text.EnumerateRunes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryAppendRune(output, sourceLanguage, script, rune, cancellationToken))
                    output.Add(new VocalPronunciationPhone("unsupported"));
            }
        }
    }

    private static bool TryAppendRune(
        List<VocalPronunciationPhone> output, VocalLanguage sourceLanguage, ScriptGroup script, Rune rune,
        CancellationToken cancellationToken)
    {
        if (VocalIpa.IsIpaSpecificCharacter(rune.Value))
        {
            VocalIpa.Append(output, rune.ToString(), cancellationToken);
            return true;
        }

        int start = output.Count;
        try
        {
            if (Supports(sourceLanguage))
                output.AddRange(PhonemizeWordCore(sourceLanguage, rune.ToString(), cancellationToken));
            else
                output.AddRange(VocalPhonemizer.Phonemize(sourceLanguage, rune.ToString(), cancellationToken)
                    .Select(phone => new VocalPronunciationPhone(phone)));
        }
        catch (VocalUnsupportedPronunciationException)
        {
            if (script == ScriptGroup.Latin && sourceLanguage != VocalLanguage.English)
                output.AddRange(VocalPhonemizer.Phonemize(VocalLanguage.English, rune.ToString(), cancellationToken)
                    .Select(phone => new VocalPronunciationPhone(phone)));
        }
        return output.Count > start;
    }

    private static void AppendUnknownRune(
        List<VocalPronunciationPhone> output, Rune rune, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (VocalIpa.IsIpaSpecificCharacter(rune.Value))
            VocalIpa.Append(output, rune.ToString(), cancellationToken);
        else
            output.Add(new VocalPronunciationPhone("unsupported"));
    }

    private static ScriptGroup Classify(Rune rune, ScriptGroup previous)
    {
        int value = rune.Value;
        if (VocalIpa.IsIpaSpecificCharacter(value)
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                && previous == ScriptGroup.Ipa)
            return ScriptGroup.Ipa;
        if (value is >= 0x0600 and <= 0x08ff) return ScriptGroup.Arabic;
        if (value is >= 0x0400 and <= 0x052f) return ScriptGroup.Cyrillic;
        if (value is >= 0x3040 and <= 0x30ff or >= 0x31f0 and <= 0x31ff) return ScriptGroup.Kana;
        if (value is >= 0x3400 and <= 0x9fff or >= 0x20000 and <= 0x2fa1f) return ScriptGroup.Han;
        if (value <= char.MaxValue
            && (IsLatin((char)value) || char.IsAsciiLetter((char)value))) return ScriptGroup.Latin;
        if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            return previous == ScriptGroup.None ? ScriptGroup.Other : previous;
        return ScriptGroup.Other;
    }

    private enum ScriptGroup
    {
        None,
        Latin,
        Arabic,
        Cyrillic,
        Han,
        Kana,
        Ipa,
        Other,
    }

    private static List<VocalPronunciationPhone> FromIpaEngine(
        string word, VocalLanguage language, Func<string, string> convert, CancellationToken cancellationToken)
    {
        string allowed = language == VocalLanguage.French
            ? "abcdefghijklmnopqrstuvwxyzàâäæçéèêëîïôöœùûüÿ"
            : "abcdefghijklmnopqrstuvwxyzáéíóúüñ";
        ValidateCharacters(word, allowed, language);
        string ipa = convert(word);
        var phones = new List<VocalPronunciationPhone>();
        VocalIpa.Append(phones, ipa, cancellationToken);
        return phones;
    }

    internal static void ValidateCharacters(string word, string allowed, VocalLanguage language)
    {
        foreach (char c in word)
        {
            if (allowed.Contains(c, StringComparison.Ordinal) || c == '\'') continue;
            throw Unsupported(c.ToString(), language);
        }
    }

    internal static VocalUnsupportedPronunciationException Unsupported(string value, VocalLanguage language) =>
        new($"Unsupported {language.DisplayName()} character or pronunciation: '{value}'.");

    private static VocalUnsupportedPronunciationException Unsupported(string message) =>
        new(message);

    internal static void SetStress(List<VocalPronunciationPhone> phones, int index)
    {
        if (index >= 0 && index < phones.Count)
            phones[index] = phones[index] with { Stressed = true };
    }

    internal static void SetSyllableTone(List<VocalPronunciationPhone> phones, int start, int tone)
    {
        if (tone is < 1 or > 5) return;
        double duration = 0;
        for (int i = start; i < phones.Count; i++)
        {
            var definition = VocalPhonemeInventory.Get(phones[i].Symbol);
            if (definition.Voiced) duration += definition.DurationMs;
        }
        double position = 0;
        for (int i = start; i < phones.Count && duration > 0; i++)
        {
            var definition = VocalPhonemeInventory.Get(phones[i].Symbol);
            if (!definition.Voiced) continue;
            double next = position + definition.DurationMs / duration;
            phones[i] = phones[i] with { Tone = tone, ToneStart = position, ToneEnd = next };
            position = next;
        }
    }

    internal static bool IsLatin(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
            or >= '\u00c0' and <= '\u024f' or >= '\u1e00' and <= '\u1eff';

    internal static bool IsCombiningMark(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}
