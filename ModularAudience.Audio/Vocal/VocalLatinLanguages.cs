namespace ModularAudience.Audio.Vocal;

internal static class VocalLatinLanguages
{
    internal static List<VocalPronunciationPhone> Italian(string word, CancellationToken cancellationToken)
    {
        VocalMultilingualPhonemizer.ValidateCharacters(word,
            "abcdefghijklmnopqrstuvwxyzàáâèéêìíîòóôùúû", VocalLanguage.Italian);
        var result = new List<VocalPronunciationPhone>();
        var vowels = new List<int>();
        int explicitStress = -1;
        for (int i = 0; i < word.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = word[i];
            char next = i + 1 < word.Length ? word[i + 1] : '\0';
            char after = i + 2 < word.Length ? word[i + 2] : '\0';
            if (c == '\'') continue;
            if (IsItalianVowel(c))
            {
                vowels.Add(result.Count);
                if (c is 'à' or 'á' or 'â' or 'è' or 'é' or 'ê' or 'ì' or 'í' or 'î'
                    or 'ò' or 'ó' or 'ô' or 'ù' or 'ú' or 'û')
                    explicitStress = result.Count;
                result.Add(new(ItalianVowel(c))
                {
                    SourceRepetitionLength = VocalPhonemizer.CappedRepetitionLength(word, i) == 3 ? 3 : 0,
                });
                continue;
            }
            if (c == 'g' && next == 'n') { result.Add(new("n")); result.Add(new("y")); i++; continue; }
            if (c == 'g' && next == 'l' && after == 'i')
            {
                result.Add(new("l")); result.Add(new("y")); i += 2;
                if (i + 1 >= word.Length || !IsItalianVowel(word[i + 1]))
                {
                    vowels.Add(result.Count);
                    result.Add(new("ee"));
                }
                continue;
            }
            if (c == 's' && next == 'c' && after is 'e' or 'i')
            {
                result.Add(new("sh"));
                i++;
                if (after == 'i' && i + 2 < word.Length && IsItalianVowel(word[i + 2])) i++;
                continue;
            }
            if (c is 'c' or 'g')
            {
                if (next == c)
                {
                    string doubleSound = after is 'e' or 'i' ? (c == 'c' ? "tsh" : "j") : c == 'c' ? "k" : "g";
                    result.Add(new(doubleSound)); result.Add(new(doubleSound)); i++;
                    if (after == 'i' && i + 2 < word.Length && IsItalianVowel(word[i + 2])) i++;
                    continue;
                }
                if (next == 'h') { result.Add(new(c == 'c' ? "k" : "g")); i++; continue; }
                bool soft = next is 'e' or 'i';
                result.Add(new(c == 'c' ? soft ? "tsh" : "k" : soft ? "j" : "g"));
                if (next == 'i' && after != '\0' && IsItalianVowel(after)) i++;
                continue;
            }
            if (c == 'q' && next == 'u')
            {
                result.Add(new("k")); result.Add(new("w")); i++; continue;
            }
            if (c == 'r' && next == 'r') { result.Add(new("trill-r")); i++; continue; }
            string sound = c switch
            {
                'b' => "b", 'd' => "d", 'f' => "f", 'h' => "",
                'j' or 'y' => "y", 'k' => "k", 'l' => "l", 'm' => "m",
                'n' => "n", 'p' => "p", 'r' => "tap-r", 't' => "t",
                'v' or 'w' => "v", 'x' => "ks",
                's' when i > 0 && IsItalianVowel(word[i - 1]) && IsItalianVowel(next) => "z",
                's' => "s", 'z' => "ts",
                _ => throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Italian),
            };
            if (sound == "ks") { result.Add(new("k")); result.Add(new("s")); }
            else if (sound.Length > 0) result.Add(new(sound));
        }
        int stress = explicitStress >= 0 ? explicitStress : vowels.Count > 1 ? vowels[^2] : vowels.FirstOrDefault(-1);
        VocalMultilingualPhonemizer.SetStress(result, stress);
        return result;
    }

    internal static List<VocalPronunciationPhone> Turkish(string word, CancellationToken cancellationToken)
    {
        VocalMultilingualPhonemizer.ValidateCharacters(word,
            "abcdefghijklmnopqrstuvwxyzçğıöşüâîû", VocalLanguage.Turkish);
        var result = new List<VocalPronunciationPhone>();
        int lastVowel = -1;
        foreach (char c in word)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (c == '\'') continue;
            if (c == 'ğ')
            {
                if (lastVowel >= 0) result.Add(result[lastVowel] with { Stressed = false });
                else result.Add(new("y"));
                continue;
            }
            string sound = c switch
            {
                'a' or 'â' => "a", 'e' => "e", 'i' or 'î' => "ee", 'ı' => "tr-i",
                'o' => "o", 'ö' => "oe", 'u' or 'û' => "oo", 'ü' => "ue",
                'b' => "b", 'c' => "j", 'ç' => "tsh", 'd' => "d",
                'f' => "f", 'g' => "g", 'h' => "h", 'j' => "zh",
                'k' or 'q' => "k", 'l' => "l", 'm' => "m", 'n' => "n",
                'p' => "p", 'r' => "tap-r", 's' => "s", 'ş' => "sh",
                't' => "t", 'v' or 'w' => "v", 'y' => "y", 'z' => "z",
                'x' => "ks",
                _ => throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Turkish),
            };
            if (sound == "ks") { result.Add(new("k")); result.Add(new("s")); continue; }
            if (VocalPhonemeInventory.Get(sound).IsVowel) lastVowel = result.Count;
            result.Add(new(sound));
        }
        VocalMultilingualPhonemizer.SetStress(result, lastVowel);
        return result;
    }

    private static bool IsItalianVowel(char c) => "aàáâeèéêiìíîoòóôuùúû".Contains(c);

    private static string ItalianVowel(char c) => c switch
    {
        'a' or 'à' or 'á' or 'â' => "a",
        'e' or 'è' or 'é' or 'ê' => "e",
        'i' or 'ì' or 'í' or 'î' => "ee",
        'o' or 'ò' or 'ó' or 'ô' => "o",
        _ => "oo",
    };
}
