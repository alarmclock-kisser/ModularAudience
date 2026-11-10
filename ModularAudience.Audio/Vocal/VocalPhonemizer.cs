using System.Text;

namespace ModularAudience.Audio.Vocal;

public static class VocalPhonemizer
{
    private static readonly Dictionary<string, string[]> EnglishExceptions = new(StringComparer.Ordinal)
    {
        ["a"] = ["uh"], ["i"] = ["eye"], ["an"] = ["ae", "n"],
        ["hi"] = ["h", "eye"], ["me"] = ["m", "ee"], ["we"] = ["w", "ee"],
        ["he"] = ["h", "ee"], ["she"] = ["sh", "ee"], ["be"] = ["b", "ee"],
        ["no"] = ["n", "oh"], ["go"] = ["g", "oh"], ["so"] = ["s", "oh"],
        ["the"] = ["dh", "uh"], ["this"] = ["dh", "i", "s"], ["that"] = ["dh", "ae", "t"],
        ["these"] = ["dh", "ee", "z"], ["those"] = ["dh", "oh", "z"], ["they"] = ["dh", "ay"],
        ["them"] = ["dh", "e", "m"], ["then"] = ["dh", "e", "n"], ["there"] = ["dh", "e", "r"],
        ["their"] = ["dh", "e", "r"], ["you"] = ["y", "oo"], ["your"] = ["y", "o", "r"],
        ["are"] = ["a", "r"], ["to"] = ["t", "oo"], ["do"] = ["d", "oo"], ["of"] = ["uh", "v"],
        ["one"] = ["w", "uh", "n"], ["two"] = ["t", "oo"], ["is"] = ["i", "z"], ["as"] = ["ae", "z"],
        ["hello"] = ["h", "uh", "l", "oh"], ["world"] = ["w", "er", "l", "d"],
        ["mother"] = ["m", "uh", "dh", "er"], ["father"] = ["f", "a", "dh", "er"],
        ["love"] = ["l", "uh", "v"], ["have"] = ["h", "ae", "v"], ["said"] = ["s", "e", "d"],
        ["says"] = ["s", "e", "z"], ["was"] = ["w", "uh", "z"], ["what"] = ["w", "uh", "t"],
        ["who"] = ["h", "oo"], ["some"] = ["s", "uh", "m"], ["come"] = ["k", "uh", "m"],
        ["know"] = ["n", "oh"],
        ["does"] = ["d", "uh", "z"], ["people"] = ["p", "ee", "p", "uh", "l"],
        ["could"] = ["k", "u", "d"], ["would"] = ["w", "u", "d"], ["should"] = ["sh", "u", "d"],
        ["though"] = ["dh", "oh"], ["through"] = ["th", "r", "oo"], ["thought"] = ["th", "o", "t"],
        ["rough"] = ["r", "uh", "f"], ["enough"] = ["i", "n", "uh", "f"],
        ["book"] = ["b", "u", "k"], ["good"] = ["g", "u", "d"], ["look"] = ["l", "u", "k"],
        ["head"] = ["h", "e", "d"], ["great"] = ["g", "r", "ay", "t"], ["height"] = ["h", "eye", "t"],
        ["intrigued"] = ["i", "n", "t", "r", "ee", "g", "d"],
        ["cow"] = ["k", "ow"], ["how"] = ["h", "ow"], ["now"] = ["n", "ow"],
        ["i'm"] = ["eye", "m"], ["you're"] = ["y", "o", "r"], ["we're"] = ["w", "ee", "r"],
        ["it's"] = ["i", "t", "s"], ["don't"] = ["d", "oh", "n", "t"],
    };

    private static readonly string[] AnimalVowels = ["animal-a", "animal-e", "animal-i", "animal-o", "animal-u"];

    public static IReadOnlyList<string> Phonemize(
        VocalLanguage language, string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (language == VocalLanguage.Beatboxing)
            return VocalTextParser.Parse(language, text, VocalEmphasisSyntax.Default(), cancellationToken)
                .OfType<VocalPhonemeToken>().Select(token => token.Phoneme).ToArray();
        string normalized = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (language == VocalLanguage.AnimalCrossing) return PhonemizeAnimal(normalized, cancellationToken);

        var effective = language.IsFullyImplemented() ? language : VocalLanguage.English;
        var result = new List<string>();
        int i = 0;
        while (i < normalized.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (char.IsWhiteSpace(normalized[i]))
            {
                result.Add("sil");
                i++;
                continue;
            }
            if (!char.IsLetter(normalized[i]))
            {
                i++;
                continue;
            }

            int start = i++;
            while (i < normalized.Length && (char.IsLetter(normalized[i]) || normalized[i] is '\'' or '\u2019'))
            {
                if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                i++;
            }
            string word = normalized[start..i].Replace('\u2019', '\'');
            if (effective == VocalLanguage.English && EnglishExceptions.TryGetValue(word, out var exception))
                result.AddRange(exception);
            else
                PhonemizeWord(effective, word, result, cancellationToken);
        }
        return result;
    }

    private static IReadOnlyList<string> PhonemizeAnimal(string text, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        foreach (Rune rune in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Rune.IsWhiteSpace(rune))
                result.Add("sil");
            else if (Rune.IsLetterOrDigit(rune))
            {
                int vowel = rune.Value switch
                {
                    'a' or '\u00e4' => 0,
                    'e' => 1,
                    'i' => 2,
                    'o' or '\u00f6' => 3,
                    'u' or '\u00fc' => 4,
                    _ => rune.Value % AnimalVowels.Length,
                };
                result.Add(AnimalVowels[vowel]);
            }
            else if (rune.Value == '.')
                result.Add("animal-dot");
            else if (rune.Value == '\u2026')
                result.AddRange(["animal-dot", "animal-dot", "animal-dot"]);
        }
        return result;
    }

    private static void PhonemizeWord(
        VocalLanguage language, string word, List<string> result, CancellationToken cancellationToken)
    {
        int i = 0;
        bool hasEnglishVowel = language != VocalLanguage.German && word.Any(IsEnglishVowel);
        while (i < word.Length)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!char.IsLetter(word[i])) { i++; continue; }
            if (CanSustain(language, word[i]))
            {
                int repeat = 1;
                while (i + repeat < word.Length && word[i + repeat] == word[i])
                {
                    if ((repeat & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    repeat++;
                }
                if (repeat >= 3)
                {
                    int first = result.Count;
                    if (language == VocalLanguage.German) MapGerman(word, i, result);
                    else MapEnglish(word, i, result, hasEnglishVowel);
                    if (result.Count == first + 1)
                    {
                        string phoneme = result[first];
                        for (int extra = 1; extra < repeat; extra++)
                        {
                            if ((extra & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                            result.Add(phoneme);
                        }
                        i += repeat;
                        continue;
                    }
                    result.RemoveRange(first, result.Count - first);
                }
            }
            int consumed = language == VocalLanguage.German
                ? MapGerman(word, i, result)
                : MapEnglish(word, i, result, hasEnglishVowel);
            i += consumed;
        }
    }

    internal static bool CanSustain(VocalLanguage language, char letter)
    {
        char c = char.ToLowerInvariant(letter);
        return c is 'a' or 'e' or 'i' or 'o' or 'u'
            or 'f' or 'l' or 'm' or 'n' or 'r' or 's' or 'v' or 'w'
            || (language == VocalLanguage.German && (c is '\u00e4' or '\u00f6' or '\u00fc'))
            || (c == 'z' && language != VocalLanguage.German);
    }

    private static int MapEnglish(string word, int index, List<string> result, bool hasEnglishVowel)
    {
        ReadOnlySpan<char> remaining = word.AsSpan(index);
        foreach (var (grapheme, phonemes) in EnglishClusters)
        {
            if (!remaining.StartsWith(grapheme, StringComparison.Ordinal)) continue;
            if (grapheme == "le" && (index == 0 || index + 2 != word.Length || IsEnglishVowel(word[index - 1])))
                continue;
            if (grapheme == "ow" && index + 2 == word.Length)
                result.Add("oh");
            else if (grapheme == "th" && index > 0 && index + 2 < word.Length
                && IsEnglishVowel(word[index - 1]) && IsEnglishVowel(word[index + 2]))
                result.Add("dh");
            else
                result.AddRange(phonemes);
            return grapheme.Length;
        }

        char c = word[index];
        char next = index + 1 < word.Length ? word[index + 1] : '\0';
        char prev = index > 0 ? word[index - 1] : '\0';
        if (index > 0 && c == prev && !IsEnglishVowel(c)) return 1;
        if (IsEnglishVowel(c))
        {
            if (c == 'e' && index == word.Length - 1 && index >= 3 && !IsEnglishVowel(prev))
                return 1;
            string? longVowel = MagicE(word, index, c);
            result.Add(longVowel ?? c switch
            {
                'a' => "ae", 'e' => "e", 'i' => "i", 'o' => "a", 'u' => "uh", _ => "uh",
            });
            return 1;
        }

        string? phoneme = c switch
        {
            'b' => "b", 'c' => next is 'e' or 'i' or 'y' ? "s" : "k",
            'd' => "d", 'f' => "f", 'g' => next is 'e' or 'i' or 'y' ? "j" : "g",
            'h' => "h", 'j' => "j", 'k' => "k", 'l' => "l", 'm' => "m", 'n' => "n",
            'p' => "p", 'q' => "k", 'r' => "r",
            's' => index == word.Length - 1 && IsEnglishVowel(prev) ? "z" : "s",
            't' => "t", 'v' => "v", 'w' => "w",
            'y' => index == 0 ? "y" : hasEnglishVowel ? "ee" : "eye",
            'z' => "z", _ => null,
        };
        if (c == 'x') result.AddRange(["k", "s"]);
        else if (phoneme is not null) result.Add(phoneme);
        return 1;
    }

    private static readonly (string Grapheme, string[] Phonemes)[] EnglishClusters =
    [
        ("eigh", ["ay"]), ("tion", ["sh", "uh", "n"]), ("sion", ["zh", "uh", "n"]),
        ("tch", ["tsh"]), ("dge", ["j"]), ("igh", ["eye"]), ("ore", ["o", "r"]),
        ("air", ["e", "r"]), ("augh", ["o"]), ("ough", ["oh"]),
        ("th", ["th"]), ("sh", ["sh"]), ("ch", ["tsh"]), ("ph", ["f"]), ("wh", ["w"]),
        ("ng", ["ng"]), ("qu", ["k", "w"]), ("ck", ["k"]), ("le", ["uh", "l"]),
        ("ai", ["ay"]), ("ay", ["ay"]), ("ee", ["ee"]), ("ea", ["ee"]),
        ("oa", ["oh"]), ("oe", ["oh"]), ("oo", ["oo"]), ("ou", ["ow"]), ("ow", ["ow"]),
        ("ui", ["oo"]), ("ey", ["ay"]), ("oy", ["oy"]), ("oi", ["oy"]),
        ("ar", ["a", "r"]), ("or", ["o", "r"]), ("ir", ["er"]), ("ur", ["er"]), ("er", ["er"]),
    ];

    private static string? MagicE(string word, int index, char vowel)
    {
        int consonant = index + 1;
        if (consonant >= word.Length || IsEnglishVowel(word[consonant])) return null;
        bool endsWithE = consonant + 2 == word.Length && word[consonant + 1] == 'e';
        bool endsWithLe = consonant + 3 == word.Length && word[(consonant + 1)..] == "le";
        if (!endsWithE && !endsWithLe) return null;
        return vowel switch
        {
            'a' => "ay", 'e' => "ee", 'i' => "eye", 'o' => "oh", 'u' => "oo", _ => null,
        };
    }

    private static bool IsEnglishVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static int MapGerman(string word, int index, List<string> result)
    {
        ReadOnlySpan<char> remaining = word.AsSpan(index);
        foreach (var (grapheme, phonemes) in GermanClusters)
        {
            if (!remaining.StartsWith(grapheme, StringComparison.Ordinal)) continue;
            if (grapheme is "sp" or "st" && index != 0) continue;
            if (grapheme == "er" && index + 2 != word.Length) continue;
            if (grapheme == "ch")
            {
                char previous = index > 0 ? word[index - 1] : '\0';
                result.Add(previous is 'a' or 'o' or 'u' ? "ch" : "ich");
            }
            else result.AddRange(phonemes);
            return grapheme.Length;
        }

        char c = word[index];
        char next = index + 1 < word.Length ? word[index + 1] : '\0';
        char prev = index > 0 ? word[index - 1] : '\0';
        if (index > 0 && c == prev && !IsGermanVowel(c)) return 1;
        if (c == 'h' && IsGermanVowel(prev)) return 1;
        string? phoneme = c switch
        {
            'a' => "a", 'e' => index == word.Length - 1 || (next == 'n' && index + 2 == word.Length) ? "uh" : "e",
            'i' => "i", 'o' => "o",
            'u' => index + 3 == word.Length && word[index + 2] == 'e' ? "oo" : "u",
            '\u00e4' => "ae", '\u00f6' => "oe", '\u00fc' => "ue",
            'b' => next == '\0' ? "p" : "b", 'c' => "k",
            'd' => next == '\0' ? "t" : "d", 'f' => "f",
            'g' => next == '\0' ? prev == 'i' && index > 1 ? "ich" : "k" : "g",
            'h' => "h", 'j' => "y", 'k' => "k", 'l' => "l", 'm' => "m", 'n' => "n",
            'p' => "p", 'q' => "k", 'r' => "r",
            's' => IsGermanVowel(next) && (index == 0 || IsGermanVowel(prev)) ? "z" : "s",
            '\u00df' => "s", 't' => "t", 'v' => "f", 'w' => "v", 'z' => "ts", _ => null,
        };
        if (c == 'x') result.AddRange(["k", "s"]);
        else if (phoneme is not null) result.Add(phoneme);
        return 1;
    }

    private static readonly (string Grapheme, string[] Phonemes)[] GermanClusters =
    [
        ("tsch", ["tsh"]), ("sch", ["sh"]), ("chs", ["k", "s"]),
        ("ch", []), ("sp", ["sh", "p"]), ("st", ["sh", "t"]), ("tz", ["ts"]),
        ("ck", ["k"]), ("ss", ["s"]), ("pf", ["p", "f"]), ("ei", ["eye"]), ("ai", ["eye"]),
        ("ay", ["eye"]), ("ie", ["ee"]), ("eu", ["oy"]), ("\u00e4u", ["oy"]), ("au", ["ow"]),
        ("qu", ["k", "v"]), ("ng", ["ng"]), ("ph", ["f"]), ("th", ["t"]), ("er", ["uh"]),
    ];

    private static bool IsGermanVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or '\u00e4' or '\u00f6' or '\u00fc';
}
