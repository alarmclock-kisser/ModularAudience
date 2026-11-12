using System.Text;
using Lucene.Net.Analysis.Ja;
using Lucene.Net.Analysis.Ja.TokenAttributes;
using Lucene.Net.Analysis.Ja.Util;
using Lucene.Net.Analysis.TokenAttributes;

namespace ModularAudience.Audio.Vocal;

internal static class VocalJapanese
{
    internal static List<VocalPronunciationPhone> Phonemize(string text, CancellationToken cancellationToken)
    {
        var result = new List<VocalPronunciationPhone>();
        bool hasJapanese = false;
        foreach (char c in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsJapanese(c)) hasJapanese = true;
            else if (!VocalMultilingualPhonemizer.IsLatin(c) && c != '\''
                && !VocalMultilingualPhonemizer.IsCombiningMark(c))
                throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Japanese);
        }
        if (!hasJapanese)
        {
            AppendRomaji(result, text, cancellationToken);
            return result;
        }

        using var tokenizer = new JapaneseTokenizer(
            new StringReader(text), null, false, JapaneseTokenizerMode.NORMAL);
        var surface = tokenizer.AddAttribute<ICharTermAttribute>();
        var reading = tokenizer.AddAttribute<IReadingAttribute>();
        var kana = new StringBuilder();
        tokenizer.Reset();
        while (tokenizer.IncrementToken())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string original = surface.ToString();
            string? pronounced = reading.GetPronunciation() ?? reading.GetReading();
            if (pronounced is null)
            {
                if (original.Any(IsKanji))
                    throw VocalMultilingualPhonemizer.Unsupported(original, VocalLanguage.Japanese);
                pronounced = original;
            }
            if (pronounced.Any(IsJapanese))
                kana.Append(pronounced);
            else
            {
                if (kana.Length > 0) { AppendKana(result, kana.ToString(), cancellationToken); kana.Clear(); }
                AppendRomaji(result, pronounced, cancellationToken);
            }
        }
        tokenizer.End();
        if (kana.Length > 0) AppendKana(result, kana.ToString(), cancellationToken);
        return result;
    }

    private static bool IsKanji(char c) => c is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff' or '々';
    private static bool IsJapanese(char c) => IsKanji(c) || c is >= '\u3041' and <= '\u30ff';

    private static void AppendKana(
        List<VocalPronunciationPhone> result, string kana, CancellationToken cancellationToken)
    {
        var katakana = new StringBuilder(kana.Length);
        foreach (char c in kana)
            katakana.Append(c is >= '\u3041' and <= '\u3096' ? (char)(c + 0x60) : c);

        int start = 0;
        for (int i = 0; i <= katakana.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i < katakana.Length && katakana[i] is not ('ー' or 'ッ')) continue;
            if (i > start)
                AppendRomaji(result, ToStringUtil.GetRomanization(katakana.ToString(start, i - start)), cancellationToken);
            if (i < katakana.Length && katakana[i] == 'ッ')
                result.Add(new("sil"));
            else if (i < katakana.Length)
            {
                int vowel = result.FindLastIndex(p => VocalPhonemeInventory.Get(p.Symbol).IsVowel);
                if (vowel < 0) throw VocalMultilingualPhonemizer.Unsupported("ー", VocalLanguage.Japanese);
                result.Add(result[vowel] with { Stressed = false });
            }
            start = i + 1;
        }
    }

    private static void AppendRomaji(
        List<VocalPronunciationPhone> result, string text, CancellationToken cancellationToken)
    {
        string word = text.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        for (int i = 0; i < word.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = word[i];
            char next = i + 1 < word.Length ? word[i + 1] : '\0';
            if (c == '\'') continue;
            if (c == '\u0304')
            {
                if (result.Count == 0 || !VocalPhonemeInventory.Get(result[^1].Symbol).IsVowel)
                    throw VocalMultilingualPhonemizer.Unsupported(text, VocalLanguage.Japanese);
                result.Add(result[^1] with { Stressed = false });
                continue;
            }
            if (c == 's' && next == 'h') { result.Add(new("sh")); i++; continue; }
            if (c == 'c' && next == 'h') { result.Add(new("tsh")); i++; continue; }
            if (c == 't' && next == 's') { result.Add(new("ts")); i++; continue; }
            if (c == 'o' && next == 'u')
            {
                result.Add(new("o")); result.Add(new("o")); i++; continue;
            }
            if (c == 'e' && next == 'i')
            {
                result.Add(new("e")); result.Add(new("e")); i++; continue;
            }
            string sound = c switch
            {
                'a' => "a", 'e' => "e", 'i' => "ee", 'o' => "o", 'u' => "oo",
                'b' => "b", 'd' => "d", 'f' => "f", 'g' => "g", 'h' => "h",
                'j' => "j", 'k' => "k", 'm' => "m", 'n' => "n", 'p' => "p",
                'r' or 'l' => "tap-r", 's' => "s", 't' => "t",
                'v' => "v", 'w' => "w", 'y' => "y", 'z' => "z",
                _ => throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Japanese),
            };
            if (next == c && c is 'k' or 'p' or 't' or 's')
                result.Add(new("sil"));
            else
                result.Add(new(sound)
                {
                    SourceRepetitionLength = VocalPhonemizer.CappedRepetitionLength(word, i) == 3 ? 3 : 0,
                });
        }
    }
}
