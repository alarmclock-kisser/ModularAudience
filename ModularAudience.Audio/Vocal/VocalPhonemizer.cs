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
    private static readonly HashSet<string> GermanFullFinalE = new(StringComparer.Ordinal)
    {
        "problem", "system", "bequem", "extrem", "poem", "theorem", "diadem", "athen", "ph\u00e4nomen",
    };

    public static IReadOnlyList<string> Phonemize(
        VocalLanguage language, string text, CancellationToken cancellationToken = default)
        => PhonemizeCore(language, text, null, cancellationToken);

    internal static (IReadOnlyList<string> Phonemes, IReadOnlyList<int> Repetitions) PhonemizeWithRepetitions(
        VocalLanguage language, string text, CancellationToken cancellationToken)
    {
        if (language is not (VocalLanguage.English or VocalLanguage.German))
            throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported vocal language.");
        var repetitions = new List<int>();
        var phonemes = PhonemizeCore(language, text, repetitions, cancellationToken);
        return (phonemes, repetitions);
    }

    private static IReadOnlyList<string> PhonemizeCore(
        VocalLanguage language, string text, List<int>? repetitions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (language is not (VocalLanguage.AnimalCrossing or VocalLanguage.Beatboxing)
            && VocalIpa.TryParseNotation(text, cancellationToken, out var ipaPhones))
            return ipaPhones.Select(phone => phone.Symbol).ToArray();
        if (language is VocalLanguage.English or VocalLanguage.German
            && VocalMultilingualPhonemizer.NeedsCrossScriptFallback(text))
        {
            var bestEffort = VocalMultilingualPhonemizer.PhonemizeBestEffort(language, text, cancellationToken);
            AddRepetitions(repetitions, bestEffort.Count, 0, cancellationToken);
            return bestEffort.Select(phone => phone.Symbol).ToArray();
        }
        if (VocalMultilingualPhonemizer.Supports(language))
            return VocalTextParser.Parse(language, text, new VocalEmphasisSyntax(), cancellationToken)
                .SelectMany(token => token switch
                {
                    VocalPhonemeToken phoneme => new[] { phoneme.Phoneme },
                    VocalPauseToken => new[] { "sil" },
                    _ => Array.Empty<string>(),
                }).ToArray();
        if (language == VocalLanguage.Beatboxing)
            return VocalTextParser.Parse(language, text, VocalEmphasisSyntax.Default(), cancellationToken)
                .OfType<VocalPhonemeToken>().Select(token => token.Phoneme).ToArray();
        string normalized = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (language == VocalLanguage.AnimalCrossing) return PhonemizeAnimal(normalized, cancellationToken);

        if (!language.IsFullyImplemented())
            throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported vocal language.");
        var effective = language;
        var result = new List<string>();
        int i = 0;
        while (i < normalized.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (char.IsWhiteSpace(normalized[i]))
            {
                result.Add("sil");
                repetitions?.Add(0);
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
            {
                result.AddRange(exception);
                AddRepetitions(repetitions, exception.Length, 0, cancellationToken);
            }
            else
                PhonemizeWord(effective, word, result, repetitions, cancellationToken);
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
        VocalLanguage language, string word, List<string> result, List<int>? repetitions, CancellationToken cancellationToken)
    {
        int i = 0;
        bool hasEnglishVowel = language != VocalLanguage.German && word.Any(IsEnglishVowel);
        bool hasGermanVowel = false;
        while (i < word.Length)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!char.IsLetter(word[i])) { i++; continue; }
            int sourceRepetition = 1;
            int repeat = 1;
            if (CanSustain(language, word[i]))
            {
                while (i + repeat < word.Length && word[i + repeat] == word[i])
                {
                    if ((repeat & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    repeat++;
                }
                sourceRepetition = repeat;
                if (repetitions is not null)
                    sourceRepetition = CappedRepetitionLength(word, i);
                if (repeat >= 3)
                {
                    int first = result.Count;
                    if (language == VocalLanguage.German) MapGerman(word, i, result, hasGermanVowel);
                    else MapEnglish(word, i, result, hasEnglishVowel);
                    if (result.Count == first + 1)
                    {
                        string phoneme = result[first];
                        for (int extra = 1; extra < repeat; extra++)
                        {
                            if ((extra & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                            result.Add(phoneme);
                        }
                        AddRepetitions(repetitions, result.Count - first, sourceRepetition, cancellationToken);
                        if (language == VocalLanguage.German && IsGermanVowel(word[i]))
                            hasGermanVowel = true;
                        i += repeat;
                        continue;
                    }
                    result.RemoveRange(first, result.Count - first);
                }
            }
            int previousCount = result.Count;
            int consumed = language == VocalLanguage.German
                ? MapGerman(word, i, result, hasGermanVowel)
                : MapEnglish(word, i, result, hasEnglishVowel);
            bool vowel = language == VocalLanguage.German ? IsGermanVowel(word[i]) : IsEnglishVowel(word[i]);
            if (repeat == 2 && consumed == 1 && vowel && result.Count == previousCount + 1
                && VocalPhonemeInventory.Get(result[previousCount]).IsVowel)
            {
                result.Add(result[previousCount]);
                consumed = 2;
            }
            AddRepetitions(repetitions, result.Count - previousCount, sourceRepetition, cancellationToken);
            if (language == VocalLanguage.German)
            {
                for (int j = i; j < i + consumed; j++)
                    if (IsGermanVowel(word[j]) && !(word[j] == 'u' && j > 0 && word[j - 1] == 'q'))
                        hasGermanVowel = true;
            }
            i += consumed;
        }
    }

    internal static int CappedRepetitionLength(string text, int index)
    {
        char letter = text[index];
        bool previous = index > 0 && text[index - 1] == letter;
        bool next = index + 1 < text.Length && text[index + 1] == letter;
        if (previous && next || previous && index > 1 && text[index - 2] == letter
            || next && index + 2 < text.Length && text[index + 2] == letter)
            return 3;
        return previous || next ? 2 : 1;
    }

    private static void AddRepetitions(
        List<int>? repetitions, int count, int sourceRepetition, CancellationToken cancellationToken)
    {
        if (repetitions is null) return;
        for (int i = 0; i < count; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            repetitions.Add(sourceRepetition);
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
            string? longVowel = c == prev || c == next ? null : MagicE(word, index, c);
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

    private static int MapGerman(string word, int index, List<string> result, bool hasPreviousVowel)
    {
        ReadOnlySpan<char> remaining = word.AsSpan(index);
        foreach (var (grapheme, phonemes) in GermanClusters)
        {
            if (!remaining.StartsWith(grapheme, StringComparison.Ordinal)) continue;
            if (grapheme is "sp" or "st" && index != 0
                && !(index >= 3 && word.AsSpan(index - 3, 3).SequenceEqual("ver"))) continue;
            if (grapheme == "er" && index + 2 != word.Length) continue;
            if (grapheme == "er")
            {
                if (!hasPreviousVowel)
                {
                    result.Add("de-e-long");
                    result.Add("de-r-glide");
                }
                else result.Add("de-er");
            }
            else if (grapheme == "ch")
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
        string? shortVowel = IsGermanVowel(c) && c != prev && c != next && HasShortVowelSpelling(word, index)
            ? GermanShortVowel(c) : null;
        string? phoneme = shortVowel ?? (c switch
        {
            'a' => "a",
            'e' => GermanE(word, index, hasPreviousVowel),
            'i' => next == 'i' || prev == 'i' || word is "mir" or "dir" or "wir" or "ihr" ? "ee" : "i",
            'o' => next is '\0' or 'h' or '\u00df' or 'o' || prev == 'o'
                || HasOpenVowelSpelling(word, index) ? "de-o-long" : "o",
            'u' => next is '\0' or 'h' or '\u00df' or 'u' || prev == 'u' || HasOpenVowelSpelling(word, index)
                || index + 3 == word.Length && word[index + 2] == 'e' ? "oo" : "u",
            '\u00e4' => "ae", '\u00f6' => "oe", '\u00fc' => "ue",
            'b' => next == '\0' ? "p" : "b", 'c' => "k",
            'd' => next == '\0' ? "t" : "d", 'f' => "f",
            'g' => next == '\0' ? prev == 'i' && index > 1 ? "ich" : "k" : "g",
            'h' => "h", 'j' => "y", 'k' => "k", 'l' => "l", 'm' => "m", 'n' => "n",
            'p' => "p", 'q' => "k", 'r' => IsGermanVocalicR(word, index) ? "de-r-glide" : "de-r",
            's' => IsGermanVowel(next) && (index == 0 || IsGermanVowel(prev)) ? "z" : "s",
            '\u00df' => "s", 't' => "t", 'v' => "f", 'w' => "v", 'z' => "ts", _ => null,
        });
        if (c == 'x') result.AddRange(["k", "s"]);
        else if (phoneme is not null) result.Add(phoneme);
        return 1;
    }

    private static readonly (string Grapheme, string[] Phonemes)[] GermanClusters =
    [
        ("tsch", ["tsh"]), ("sch", ["sh"]), ("chs", ["k", "s"]),
        ("ch", []), ("sp", ["sh", "p"]), ("st", ["sh", "t"]), ("tz", ["ts"]),
        ("ck", ["k"]), ("ss", ["s"]), ("pf", ["p", "f"]), ("ei", ["eye"]), ("ai", ["eye"]),
        ("ay", ["eye"]), ("ie", ["ee"]), ("ee", ["de-e-long"]), ("eu", ["oy"]), ("\u00e4u", ["oy"]), ("au", ["ow"]),
        ("qu", ["k", "v"]), ("ng", ["ng"]), ("ph", ["f"]), ("th", ["t"]), ("er", []),
    ];

    private static string GermanE(string word, int index, bool hasPreviousVowel)
    {
        if (index == word.Length - 1)
            return word.Length == 1 ? "de-e-long" : "de-schwa";
        // Common -egel roots have a long first E and a reduced -el.
        if (word.AsSpan(index).StartsWith("egel", StringComparison.Ordinal)
            && (index == 0 || !IsGermanVowel(word[index - 1]))) return "de-e-long";
        if (index >= 2 && word.AsSpan(index - 2).StartsWith("egel", StringComparison.Ordinal))
            return "de-schwa";
        if (index == 1 && word[0] == 'b' && word.AsSpan(2).StartsWith("sch", StringComparison.Ordinal))
            return "de-schwa";
        char next = word[index + 1];
        if (next == 'h') return "de-e-long";
        if (index + 2 == word.Length && (next is 'n' or 'm'))
            return !hasPreviousVowel || GermanFullFinalE.Contains(word) ? "de-e-long" : "de-schwa";
        if (next == 'n' && hasPreviousVowel && index + 2 < word.Length
            && !IsGermanVowel(word[index + 2]))
            return "de-schwa";
        return "e";
    }

    private static bool HasOpenVowelSpelling(string word, int index) =>
        index + 2 < word.Length && char.IsLetter(word[index + 1])
        && !IsGermanVowel(word[index + 1]) && IsGermanVowel(word[index + 2]);

    private static bool HasShortVowelSpelling(string word, int index)
    {
        if (index + 2 >= word.Length) return false;
        char first = word[index + 1], second = word[index + 2];
        return first == second && char.IsLetter(first) && !IsGermanVowel(first)
            || first == 'c' && second == 'k'
            || first == 't' && second == 'z';
    }

    private static string? GermanShortVowel(char c) => c switch
    {
        'a' => "de-a-short", 'e' => "de-e-short", 'i' => "de-i-short",
        'o' => "de-o-short", 'u' => "de-u-short", '\u00e4' => "de-ae-short",
        '\u00f6' => "de-oe-short", '\u00fc' => "de-ue-short", _ => null,
    };

    private static bool IsGermanVocalicR(string word, int index)
    {
        char previous = index > 0 ? word[index - 1] : '\0';
        char next = index + 1 < word.Length ? word[index + 1] : '\0';
        if (next == 'r' || IsGermanVowel(next)) return false;
        return IsGermanVowel(previous)
            || previous == 'h' && index > 1 && IsGermanVowel(word[index - 2]);
    }

    private static bool IsGermanVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or '\u00e4' or '\u00f6' or '\u00fc';
}
