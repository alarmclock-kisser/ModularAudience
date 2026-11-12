namespace ModularAudience.Audio.Vocal;

internal static class VocalRussian
{
    internal static List<VocalPronunciationPhone> Phonemize(string word, CancellationToken cancellationToken)
    {
        var result = new List<VocalPronunciationPhone>();
        var vowels = new List<(int Index, char Letter)>();
        int explicitStress = -1;
        for (int i = 0; i < word.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = word[i];
            char next = i + 1 < word.Length ? word[i + 1] : '\0';
            if (c == '\'') continue;
            if (c == '\u0301')
            {
                if (vowels.Count == 0) throw VocalMultilingualPhonemizer.Unsupported(word, VocalLanguage.Russian);
                explicitStress = vowels[^1].Index;
                continue;
            }
            if (c is 'а' or 'a' or 'á') { AddVowel(result, vowels, "a", c, ref explicitStress, c == 'á'); continue; }
            if (c is 'о' or 'o' or 'ó') { AddVowel(result, vowels, "o", c, ref explicitStress, c == 'ó'); continue; }
            if (c is 'у' or 'u' or 'ú') { AddVowel(result, vowels, "oo", c, ref explicitStress, c == 'ú'); continue; }
            if (c == 'ы') { AddVowel(result, vowels, "tr-i", c, ref explicitStress, false); continue; }
            if (c is 'э' or 'e' or 'é')
            {
                if (c is 'e' or 'é' && (i == 0 || word[i - 1] != 'y' && word[i - 1] != c))
                    result.Add(new("y"));
                AddVowel(result, vowels, "e", c, ref explicitStress, c == 'é');
                continue;
            }
            if (c is 'и' or 'i' or 'í') { AddVowel(result, vowels, "ee", c, ref explicitStress, c == 'í'); continue; }
            if (c is 'е' or 'ё' or 'ю' or 'я')
            {
                if (i == 0 || word[i - 1] != c) result.Add(new("y"));
                string vowel = c switch { 'ё' => "o", 'ю' => "oo", 'я' => "a", _ => "e" };
                AddVowel(result, vowels, vowel, c, ref explicitStress, c == 'ё');
                continue;
            }
            if (c is 'ь' or 'ъ' or 'й')
            {
                result.Add(new("y"));
                continue;
            }

            string remaining = word[i..];
            if (remaining.StartsWith("shch", StringComparison.Ordinal))
            {
                result.Add(new("sh")); result.Add(new("sh")); i += 3; continue;
            }
            string? cluster = (c, next) switch
            {
                ('s', 'h') => "sh", ('z', 'h') => "zh", ('k', 'h') => "ch",
                ('c', 'h') => "tsh", ('t', 's') => "ts",
                _ => null,
            };
            if (cluster is not null) { result.Add(new(cluster)); i++; continue; }
            if (c == 'y' && next is 'a' or 'e' or 'o' or 'u')
            {
                result.Add(new("y"));
                continue;
            }
            if (c == 'y' && (i == 0 || !VocalPhonemeInventory.Get(result[^1].Symbol).IsVowel))
            {
                AddVowel(result, vowels, "tr-i", c, ref explicitStress, false);
                continue;
            }
            string sound = c switch
            {
                'б' or 'b' => "b", 'в' or 'v' => "v", 'г' or 'g' => "g",
                'д' or 'd' => "d", 'ж' or 'ž' => "zh", 'з' or 'z' => "z",
                'к' or 'k' or 'q' => "k", 'л' or 'l' => "l", 'м' or 'm' => "m",
                'н' or 'n' => "n", 'п' or 'p' => "p", 'р' or 'r' => "tap-r",
                'с' or 's' => "s", 'т' or 't' => "t", 'ф' or 'f' => "f",
                'х' or 'h' => "ch", 'ц' => "ts", 'ч' or 'č' => "tsh",
                'ш' or 'š' => "sh", 'щ' => "long-sh", 'й' or 'j' or 'y' => "y",
                'c' => "ts", 'x' => "ks",
                _ => throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Russian),
            };
            if (sound == "long-sh") { result.Add(new("sh")); result.Add(new("sh")); continue; }
            if (sound == "ks") { result.Add(new("k")); result.Add(new("s")); continue; }
            if (i == word.Length - 1 && c is >= 'а' and <= 'я')
                sound = sound switch
                {
                    "b" => "p", "v" => "f", "g" => "k", "d" => "t", "z" => "s", "zh" => "sh", _ => sound,
                };
            result.Add(new(sound));
        }

        if (vowels.Count > 0)
        {
            int stress = explicitStress >= 0 ? explicitStress
                : word is "привет" or "privet" ? vowels[^1].Index
                : vowels.Count > 1 ? vowels[^2].Index : vowels[0].Index;
            VocalMultilingualPhonemizer.SetStress(result, stress);
            for (int first = 0; first < vowels.Count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int end = first + 1;
                bool stressed = vowels[first].Index == stress;
                while (end < vowels.Count && vowels[end].Letter == vowels[first].Letter
                    && vowels[end].Index == vowels[end - 1].Index + 1)
                {
                    if ((end & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    stressed |= vowels[end].Index == stress;
                    end++;
                }
                if (vowels[first].Letter == 'о' && !stressed)
                    for (int j = first; j < end; j++)
                    {
                        if ((j & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                        result[vowels[j].Index] = result[vowels[j].Index] with { Symbol = "ah" };
                    }
                first = end;
            }
        }
        return result;
    }

    private static void AddVowel(
        List<VocalPronunciationPhone> result, List<(int Index, char Letter)> vowels,
        string sound, char letter, ref int stress, bool stressed)
    {
        if (stressed) stress = result.Count;
        vowels.Add((result.Count, letter));
        result.Add(new(sound));
    }
}
