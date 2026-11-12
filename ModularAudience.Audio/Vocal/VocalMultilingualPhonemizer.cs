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

internal static class VocalMultilingualPhonemizer
{
    private static readonly Lazy<FrenchG2PEngine> French = new(() =>
        new FrenchG2PEngine(new FrenchG2POptions(includeStress: true)));
    private static readonly Lazy<SpanishG2PEngine> Spanish = new(() => new SpanishG2PEngine());

    internal static bool Supports(VocalLanguage language) => language is
        VocalLanguage.French or VocalLanguage.Spanish or VocalLanguage.Italian or VocalLanguage.Chinese
        or VocalLanguage.Japanese or VocalLanguage.Turkish or VocalLanguage.Arabic or VocalLanguage.Russian;

    internal static IReadOnlyList<VocalPronunciationPhone> PhonemizeWord(
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
            throw new ArgumentException($"No pronunciation was found for '{word}' in {language.DisplayName()}.", nameof(word));
        return phones;
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

    internal static ArgumentException Unsupported(string value, VocalLanguage language) =>
        new($"Unsupported {language.DisplayName()} character or pronunciation: '{value}'.");

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
        c is >= 'a' and <= 'z' or >= '\u00c0' and <= '\u024f' or >= '\u1e00' and <= '\u1eff';

    internal static bool IsCombiningMark(char c) =>
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}
