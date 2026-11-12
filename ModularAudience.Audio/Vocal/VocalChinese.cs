using System.Text;
using DotNetG2P.Chinese;

namespace ModularAudience.Audio.Vocal;

internal static class VocalChinese
{
    private static readonly Lazy<ChineseG2PEngine> Engine = new(() => new ChineseG2PEngine());
    private static readonly Dictionary<string, string> Finals = new(StringComparer.Ordinal)
    {
        ["a"] = "a", ["o"] = "o", ["e"] = "ɤ", ["ai"] = "aɪ", ["ei"] = "eɪ",
        ["ao"] = "aʊ", ["ou"] = "oʊ", ["an"] = "an", ["en"] = "ən",
        ["ang"] = "aŋ", ["eng"] = "əŋ", ["ong"] = "ʊŋ", ["er"] = "əɻ",
        ["i"] = "i", ["ia"] = "ia", ["ie"] = "iɛ", ["iao"] = "iaʊ",
        ["iu"] = "ioʊ", ["ian"] = "iɛn", ["in"] = "in", ["iang"] = "iaŋ",
        ["ing"] = "iŋ", ["iong"] = "iʊŋ",
        ["u"] = "u", ["ua"] = "ua", ["uo"] = "uo", ["uai"] = "uaɪ",
        ["ui"] = "ueɪ", ["uan"] = "uan", ["un"] = "uən", ["uang"] = "uaŋ",
        ["ueng"] = "uəŋ", ["v"] = "y", ["ve"] = "yɛ", ["van"] = "yɛn", ["vn"] = "yn",
    };

    internal static List<VocalPronunciationPhone> Phonemize(string text, CancellationToken cancellationToken)
    {
        var result = new List<VocalPronunciationPhone>();
        int i = 0;
        while (i < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsHanzi(text[i]))
            {
                int start = i;
                while (i < text.Length && IsHanzi(text[i]) && i - start < 256)
                {
                    if (!Engine.Value.ContainsChar(text[i]))
                        throw VocalMultilingualPhonemizer.Unsupported(text[i].ToString(), VocalLanguage.Chinese);
                    i++;
                }
                foreach (string syllable in Engine.Value.ToPinyinList(text[start..i], PinyinStyle.ToneNumber))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppendPinyin(result, syllable, cancellationToken);
                }
            }
            else
            {
                int start = i;
                while (i < text.Length && !IsHanzi(text[i])) i++;
                AppendPinyin(result, text[start..i], cancellationToken);
            }
        }
        return result;
    }

    private static bool IsHanzi(char c) => c is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';

    private static void AppendPinyin(
        List<VocalPronunciationPhone> result, string input, CancellationToken cancellationToken)
    {
        string decomposed = input.Normalize(NormalizationForm.FormD);
        var letters = new StringBuilder();
        var marks = new List<int>();
        foreach (char c in decomposed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (c is >= 'a' and <= 'z')
            {
                letters.Append(c);
                marks.Add(0);
            }
            else if (c == '\u0308' && letters.Length > 0 && letters[^1] == 'u')
                letters[^1] = 'v';
            else if (c is '\u0304' or '\u0301' or '\u030c' or '\u0300')
            {
                if (marks.Count == 0 || marks[^1] != 0)
                    throw VocalMultilingualPhonemizer.Unsupported(input, VocalLanguage.Chinese);
                marks[^1] = c switch { '\u0304' => 1, '\u0301' => 2, '\u030c' => 3, _ => 4 };
            }
            else if (c is >= '1' and <= '5')
            {
                AppendPinyinGroup(result, letters.ToString(), marks, c - '0', cancellationToken);
                letters.Clear(); marks.Clear();
            }
            else if (c == '\'')
            {
                AppendPinyinGroup(result, letters.ToString(), marks, 0, cancellationToken);
                letters.Clear(); marks.Clear();
            }
            else throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Chinese);
        }
        AppendPinyinGroup(result, letters.ToString(), marks, 0, cancellationToken);
    }

    private static void AppendPinyinGroup(
        List<VocalPronunciationPhone> result, string letters, List<int> marks, int numericTone,
        CancellationToken cancellationToken)
    {
        if (letters.Length == 0)
        {
            if (numericTone != 0) throw VocalMultilingualPhonemizer.Unsupported(numericTone.ToString(), VocalLanguage.Chinese);
            return;
        }

        var next = new int[letters.Length + 1];
        Array.Fill(next, -1);
        next[^1] = letters.Length;
        for (int start = letters.Length - 1; start >= 0; start--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int end = Math.Min(letters.Length, start + 6); end > start; end--)
            {
                if (next[end] < 0 || !TrySyllableIpa(letters[start..end], out _)) continue;
                int tones = 0;
                for (int p = start; p < end; p++) if (marks[p] != 0) tones++;
                if (tones > 1 || numericTone != 0 && end == letters.Length && tones > 0) continue;
                next[start] = end;
                break;
            }
        }
        if (next[0] < 0) throw VocalMultilingualPhonemizer.Unsupported(letters, VocalLanguage.Chinese);

        for (int start = 0; start < letters.Length; start = next[start])
        {
            int end = next[start];
            _ = TrySyllableIpa(letters[start..end], out string ipa);
            int tone = end == letters.Length ? numericTone : 0;
            for (int i = start; i < end; i++) if (marks[i] != 0) tone = marks[i];
            int first = result.Count;
            VocalIpa.Append(result, ipa, cancellationToken);
            if (end == start + 1 && VocalPhonemizer.CappedRepetitionLength(letters, start) == 3)
                for (int i = first; i < result.Count; i++)
                    result[i] = result[i] with { SourceRepetitionLength = 3 };
            VocalMultilingualPhonemizer.SetSyllableTone(result, first, tone);
        }
    }

    private static bool TrySyllableIpa(string syllable, out string ipa)
    {
        ipa = "";
        if (syllable is "m" or "n" or "ng" or "hm" or "hng")
        {
            ipa = syllable switch { "m" => "m", "n" => "n", "ng" => "ŋ", "hm" => "hm", _ => "hŋ" };
            return true;
        }
        string initial = "";
        string final = syllable;
        if (syllable.StartsWith("zh", StringComparison.Ordinal)
            || syllable.StartsWith("ch", StringComparison.Ordinal)
            || syllable.StartsWith("sh", StringComparison.Ordinal))
        {
            initial = syllable[..2];
            final = syllable[2..];
        }
        else if (syllable.Length > 1 && "bpmfdtnlgkhjqxrzcs".Contains(syllable[0]))
        {
            initial = syllable[..1];
            final = syllable[1..];
        }
        else if (syllable.StartsWith('y'))
            final = syllable switch
            {
                "yi" => "i", "yin" => "in", "ying" => "ing",
                "ya" => "ia", "ye" => "ie", "yao" => "iao", "you" => "iu",
                "yan" => "ian", "yang" => "iang", "yong" => "iong",
                "yu" => "v", "yue" => "ve", "yuan" => "van", "yun" => "vn",
                _ => "",
            };
        else if (syllable.StartsWith('w'))
            final = syllable switch
            {
                "wu" => "u", "wa" => "ua", "wo" => "uo", "wai" => "uai",
                "wei" => "ui", "wan" => "uan", "wen" => "un", "wang" => "uang", "weng" => "ueng",
                _ => "",
            };

        if (initial is "j" or "q" or "x" && final.StartsWith('u'))
            final = "v" + final[1..];
        if (!Finals.TryGetValue(final, out string? finalIpa)) return false;
        if (final == "er" && initial.Length > 0) return false;
        if (final == "i" && initial is "zh" or "ch" or "sh" or "r" or "z" or "c" or "s")
            finalIpa = "ɻ̩";

        string initialIpa = initial switch
        {
            "" => "", "b" => "p", "p" => "pʰ", "m" => "m", "f" => "f",
            "d" => "t", "t" => "tʰ", "n" => "n", "l" => "l",
            "g" => "k", "k" => "kʰ", "h" => "x",
            "j" => "tɕ", "q" => "tɕʰ", "x" => "ɕ",
            "zh" => "ʈʂ", "ch" => "ʈʂʰ", "sh" => "ʂ", "r" => "ʒ",
            "z" => "ts", "c" => "tsʰ", "s" => "s",
            _ => "",
        };
        ipa = initialIpa + finalIpa;
        return true;
    }
}
