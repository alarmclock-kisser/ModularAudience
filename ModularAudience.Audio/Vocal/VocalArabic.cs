namespace ModularAudience.Audio.Vocal;

internal static class VocalArabic
{
    private static readonly Dictionary<string, string[]> CommonWords = new(StringComparer.Ordinal)
    {
        ["جهاد"] = ["j", "de-i-short", "h", "a", "a", "d"],
        ["مرحبا"] = ["m", "de-a-short", "tap-r", "arab-h", "de-a-short", "b", "a"],
        ["السلام"] = ["de-a-short", "s", "s", "de-a-short", "l", "a", "a", "m"],
        ["عليكم"] = ["arab-ayn", "de-a-short", "l", "eye", "k", "oo", "m"],
        ["شكرا"] = ["sh", "de-u-short", "k", "tap-r", "de-a-short", "n"],
    };

    internal static List<VocalPronunciationPhone> Phonemize(string word, CancellationToken cancellationToken)
    {
        var result = new List<VocalPronunciationPhone>();
        if (CommonWords.TryGetValue(word, out var common))
        {
            result.AddRange(common.Select(sound => new VocalPronunciationPhone(sound)));
            StressPenultimateVowel(result);
            return result;
        }

        int i = 0;
        while (i < word.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int start = i;
            bool arabic = IsArabic(word[i]);
            while (i < word.Length && IsArabic(word[i]) == arabic) i++;
            string part = word[start..i];
            if (arabic) AppendArabic(result, part, cancellationToken);
            else AppendRoman(result, part, cancellationToken);
        }
        StressPenultimateVowel(result);
        return result;
    }

    private static bool IsArabic(char c) => c is >= '\u0600' and <= '\u06ff';

    private static void AppendArabic(
        List<VocalPronunciationPhone> result, string word, CancellationToken cancellationToken)
    {
        int i = 0;
        bool assimilateArticle = false;
        if (word.Length > 2 && word[0] == 'ا' && word[1] == 'ل')
        {
            result.Add(new("de-a-short"));
            assimilateArticle = IsSunLetter(word[2]);
            if (!assimilateArticle) result.Add(new("l"));
            i = 2;
        }
        while (i < word.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char letter = word[i++];
            if (letter == 'ـ') continue;
            string? sound = letter switch
            {
                'ا' or 'آ' or 'أ' or 'إ' or 'ٱ' or 'ى' => "a",
                'ب' => "b", 'ت' => "t", 'ث' => "th", 'ج' => "j",
                'ح' => "arab-h", 'خ' => "ch", 'د' => "d", 'ذ' => "dh",
                'ر' => "tap-r", 'ز' => "z", 'س' or 'ص' => "s",
                'ش' => "sh", 'ض' => "d", 'ط' => "t", 'ظ' => "dh",
                'ع' => "arab-ayn", 'غ' => "de-r", 'ف' => "f",
                'ق' => "arab-q", 'ك' => "k", 'ل' => "l", 'م' => "m",
                'ن' => "n", 'ه' => "h", 'ة' => "a", 'ء' or 'ؤ' or 'ئ' => "h",
                'و' or 'ي' => null,
                _ => throw VocalMultilingualPhonemizer.Unsupported(letter.ToString(), VocalLanguage.Arabic),
            };
            bool shadda = false;
            bool sukun = false;
            char vowelMark = '\0';
            while (i < word.Length && word[i] is >= '\u064b' and <= '\u065f')
            {
                char mark = word[i++];
                if (mark == 'ّ') shadda = true;
                else if (mark == 'ْ') sukun = true;
                else if (mark is 'َ' or 'ُ' or 'ِ' or 'ً' or 'ٌ' or 'ٍ') vowelMark = mark;
                else throw VocalMultilingualPhonemizer.Unsupported(mark.ToString(), VocalLanguage.Arabic);
            }

            if (letter is 'و' or 'ي')
            {
                bool glide = vowelMark != '\0' || result.Count > 0
                    && VocalPhonemeInventory.Get(result[^1].Symbol).IsVowel
                    && result[^1].Symbol is "a" or "de-a-short";
                sound = letter == 'و' ? glide ? "w" : "oo" : glide ? "y" : "ee";
            }
            result.Add(new(sound!));
            if (shadda || assimilateArticle) result.Add(new(sound!));
            assimilateArticle = false;
            if (letter == 'آ') result.Add(new(sound!));
            if (vowelMark != '\0')
            {
                result.Add(new(vowelMark switch
                {
                    'َ' or 'ً' => "de-a-short",
                    'ُ' or 'ٌ' => "de-u-short",
                    _ => "de-i-short",
                }));
                if (vowelMark is 'ً' or 'ٌ' or 'ٍ') result.Add(new("n"));
                if (vowelMark == 'ً' && i < word.Length && word[i] == 'ا') i++;
            }
            else if (!sukun && !VocalPhonemeInventory.Get(sound!).IsVowel
                && i < word.Length && IsArabicConsonant(word[i]))
                result.Add(new("de-schwa"));
        }
    }

    private static bool IsArabicConsonant(char c) => c is
        'ب' or 'ت' or 'ث' or 'ج' or 'ح' or 'خ' or 'د' or 'ذ' or 'ر' or 'ز'
        or 'س' or 'ش' or 'ص' or 'ض' or 'ط' or 'ظ' or 'ع' or 'غ' or 'ف'
        or 'ق' or 'ك' or 'ل' or 'م' or 'ن' or 'ه';

    private static bool IsSunLetter(char c) => c is
        'ت' or 'ث' or 'د' or 'ذ' or 'ر' or 'ز' or 'س' or 'ش' or 'ص' or 'ض' or 'ط' or 'ظ' or 'ل' or 'ن';

    private static void AppendRoman(
        List<VocalPronunciationPhone> result, string word, CancellationToken cancellationToken)
    {
        for (int i = 0; i < word.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = word[i];
            char next = i + 1 < word.Length ? word[i + 1] : '\0';
            if (c == '\'') continue;
            string? cluster = (c, next) switch
            {
                ('s', 'h') => "sh", ('k', 'h') => "ch", ('g', 'h') => "de-r",
                ('t', 'h') => "th", ('d', 'h') => "dh",
                _ => null,
            };
            if (cluster is not null) { result.Add(new(cluster)); i++; continue; }
            string sound = c switch
            {
                'a' or 'ā' => "a", 'i' or 'ī' => "ee", 'u' or 'ū' => "oo",
                'e' => "e", 'o' => "o",
                'b' => "b", 't' or 'ṭ' => "t", 'ṯ' => "th", 'j' or 'ǧ' => "j",
                'h' => "h", 'ḥ' => "arab-h", 'ḫ' => "ch", 'd' or 'ḍ' => "d",
                'ḏ' or 'ẓ' => "dh", 'r' => "tap-r", 'z' => "z", 's' or 'ṣ' => "s",
                'š' => "sh", 'ʿ' or '‘' => "arab-ayn", 'ʾ' => "h", 'ġ' or 'ɣ' => "de-r",
                'f' => "f", 'q' => "arab-q", 'k' => "k", 'l' => "l", 'm' => "m",
                'n' => "n", 'w' => "w", 'y' => "y", 'g' => "g", 'p' => "p",
                _ => throw VocalMultilingualPhonemizer.Unsupported(c.ToString(), VocalLanguage.Arabic),
            };
            result.Add(new(sound));
            if (c is 'ā' or 'ī' or 'ū') result.Add(new(sound));
        }
    }

    private static void StressPenultimateVowel(List<VocalPronunciationPhone> phones)
    {
        var vowels = new List<int>();
        for (int i = 0; i < phones.Count; i++)
            if (VocalPhonemeInventory.Get(phones[i].Symbol).IsVowel
                && (i == 0 || phones[i - 1].Symbol != phones[i].Symbol))
                vowels.Add(i);
        if (vowels.Count > 0)
            VocalMultilingualPhonemizer.SetStress(phones, vowels.Count > 1 ? vowels[^2] : vowels[0]);
    }
}
