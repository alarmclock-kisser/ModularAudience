using System.Text;

namespace ModularAudience.Audio.Vocal;

public static class VocalPhonemizer
{
    public static IReadOnlyList<string> Phonemize(VocalLanguage language, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var effective = language.IsFullyImplemented() ? language : VocalLanguage.English;
        var lower = text.ToLowerInvariant();
        var result = new List<string>();
        int i = 0;
        while (i < lower.Length)
        {
            char c = lower[i];
            if (char.IsWhiteSpace(c)) { result.Add("sil"); i++; continue; }
            if (!char.IsLetter(c)) { i++; continue; }
            // Try longest match up to 4
            bool matched = false;
            for (int len = Math.Min(4, lower.Length - i); len >= 1; len--)
            {
                var grapheme = lower.Substring(i, len);
                if (TryMap(effective, grapheme, lower, i, out var phonemes, out var consumed))
                {
                    result.AddRange(phonemes);
                    i += consumed;
                    matched = true;
                    break;
                }
            }
            if (!matched) i++;
        }
        return result;
    }

    private static bool TryMap(VocalLanguage lang, string grapheme, string text, int index, out List<string> phonemes, out int consumed)
    {
        phonemes = [];
        consumed = grapheme.Length;
        if (lang == VocalLanguage.German)
        {
            return TryGerman(grapheme, text, index, out phonemes, out consumed);
        }
        return TryEnglish(grapheme, text, index, out phonemes, out consumed);
    }

    private static bool TryEnglish(string g, string text, int index, out List<string> phonemes, out int consumed)
    {
        phonemes = []; consumed = g.Length;
        // Digraphs / trigraphs
        switch (g)
        {
            case "tch": phonemes.Add("ch"); return true;
            case "dge": phonemes.Add("j"); return true; // approximate
            case "igh": phonemes.Add("ay"); return true;
            case "eigh": phonemes.Add("ay"); return true;
            case "ough": phonemes.Add("oh"); return true; // rough
            case "augh": phonemes.Add("ah"); return true;
            case "th": phonemes.Add("th"); return true;
            case "sh": phonemes.Add("sh"); return true;
            case "ch": phonemes.Add("ch"); return true;
            case "ph": phonemes.Add("f"); return true;
            case "wh": phonemes.Add("w"); return true; // w-ish
            case "ng": phonemes.Add("ng"); return true;
            case "qu": phonemes.Add("k"); phonemes.Add("w"); return true;
            case "ck": phonemes.Add("k"); return true;
            case "x": phonemes.Add("k"); phonemes.Add("s"); return true;
            case "ce": case "ci": case "cy": phonemes.Add("s"); consumed = 1; return true; // only consume first char? keep simple: consume 1
            case "ge": case "gi": case "gy": phonemes.Add("j"); consumed = 1; return true;
            case "ai": case "ay": phonemes.Add("ay"); return true;
            case "ee": case "ea": phonemes.Add("ee"); return true;
            case "oa": case "oe": phonemes.Add("oh"); return true;
            case "oo": phonemes.Add("oo"); return true;
            case "ou": case "ow": phonemes.Add("a"); phonemes.Add("oo"); consumed = 2; return true; // diphthong approx
            case "ui": phonemes.Add("oo"); return true;
            case "ey": phonemes.Add("ay"); return true;
            case "oy": case "oi": phonemes.Add("oh"); phonemes.Add("i"); return true;
            case "y": 
                // y as vowel or consonant
                if (IsVowelContext(text, index)) { phonemes.Add("i"); } else { phonemes.Add("y"); }
                return true;
        }
        if (g.Length == 1)
        {
            char c = g[0];
            // magic e handling: if vowel followed by consonant(s)+e
            if ("aeiou".Contains(c))
            {
                // Silent trailing 'e' (e.g. "more", "name" final e) when preceded by a consonant.
                if (c == 'e' && index > 0 && (index == text.Length - 1 || !char.IsLetter(text[index + 1])))
                {
                    char prev = text[index - 1];
                    if (!"aeiou".Contains(prev) && index >= 3)
                    {
                        return true;
                    }
                }
                var longVowel = MagicE(text, index, c);
                if (longVowel is not null) { phonemes.Add(longVowel); return true; }
                phonemes.Add(c switch
                {
                    'a' => "ae",
                    'e' => "e",
                    'i' => "i",
                    'o' => "oh",
                    'u' => "u",
                    _ => "uh",
                });
                return true;
            }
            switch (c)
            {
                case 'b': phonemes.Add("b"); return true;
                case 'c': phonemes.Add("k"); return true;
                case 'd': phonemes.Add("d"); return true;
                case 'f': phonemes.Add("f"); return true;
                case 'g': phonemes.Add("g"); return true;
                case 'h': phonemes.Add("h"); return true;
                case 'j': phonemes.Add("j"); return true;
                case 'k': phonemes.Add("k"); return true;
                case 'l': phonemes.Add("l"); return true;
                case 'm': phonemes.Add("m"); return true;
                case 'n': phonemes.Add("n"); return true;
                case 'p': phonemes.Add("p"); return true;
                case 'q': phonemes.Add("k"); return true;
                case 'r': phonemes.Add("r"); return true;
                case 's': phonemes.Add("s"); return true;
                case 't': phonemes.Add("t"); return true;
                case 'v': phonemes.Add("v"); return true;
                case 'w': phonemes.Add("w"); return true;
                case 'x': phonemes.Add("k"); phonemes.Add("s"); return true;
                case 'z': phonemes.Add("z"); return true;
            }
        }
        return false;
    }

    private static string? MagicE(string text, int index, char vowel)
    {
        // vowel ... (one or two consonants) e (word boundary)
        int j = index + 1;
        // consume consonants
        int cons = 0;
        while (j < text.Length && "bcdfghjklmnpqrstvwxyz".Contains(text[j]) && cons < 2)
        {
            j++; cons++;
        }
        if (cons >= 1 && j < text.Length && text[j] == 'e')
        {
            // ensure boundary after e
            if (j + 1 >= text.Length || !char.IsLetter(text[j + 1]))
            {
                return vowel switch { 'a' => "ay", 'e' => "ee", 'i' => "ay", 'o' => "oh", 'u' => "oo", _ => null };
            }
        }
        return null;
    }

    private static bool IsVowelContext(string text, int index)
    {
        // y is vowel if not at start of syllable (preceded by consonant) - crude: vowel if previous char is consonant
        if (index == 0) return false;
        char prev = text[index - 1];
        return !"aeiou".Contains(prev);
    }

    private static bool TryGerman(string g, string text, int index, out List<string> phonemes, out int consumed)
    {
        phonemes = []; consumed = g.Length;
        switch (g)
        {
            case "sch": phonemes.Add("sh"); return true;
            case "ch": phonemes.Add("ch"); return true; // loch-ish
            case "sp": 
                // at word start -> shp
                if (index == 0 || !char.IsLetter(text[index - 1])) { phonemes.Add("sh"); phonemes.Add("p"); return true; }
                phonemes.Add("s"); phonemes.Add("p"); return true;
            case "st":
                if (index == 0 || !char.IsLetter(text[index - 1])) { phonemes.Add("sh"); phonemes.Add("t"); return true; }
                phonemes.Add("s"); phonemes.Add("t"); return true;
            case "tz": phonemes.Add("ts"); return true;
            case "ck": phonemes.Add("k"); return true;
            case "ß": phonemes.Add("s"); return true;
            case "ä": phonemes.Add("ae"); return true;
            case "ö": phonemes.Add("oe"); return true;
            case "ü": phonemes.Add("ue"); return true;
            case "ei": case "ai": case "ay": phonemes.Add("ay"); return true;
            case "ie": phonemes.Add("ee"); return true;
            case "eu": case "äu": phonemes.Add("oe"); return true; // approx
            case "au": phonemes.Add("a"); phonemes.Add("oo"); consumed = 2; return true;
            case "qu": phonemes.Add("k"); phonemes.Add("v"); return true;
            case "ng": phonemes.Add("ng"); return true;
            case "ph": phonemes.Add("f"); return true;
            case "th": phonemes.Add("t"); return true; // German th = t
            case "v": phonemes.Add("f"); return true;
            case "w": phonemes.Add("v"); return true;
            case "j": phonemes.Add("y"); return true;
            case "z": phonemes.Add("ts"); return true;
            case "x": phonemes.Add("k"); phonemes.Add("s"); return true;
        }
        if (g.Length == 1)
        {
            char c = g[0];
            if ("aeiou".Contains(c))
            {
                phonemes.Add(c switch
                {
                    'a' => "a",
                    'e' => "e",
                    'i' => "i",
                    'o' => "o",
                    'u' => "u",
                    _ => "uh",
                });
                return true;
            }
            switch (c)
            {
                case 'b': phonemes.Add("b"); return true;
                case 'c': phonemes.Add("k"); return true;
                case 'd': phonemes.Add("d"); return true;
                case 'f': phonemes.Add("f"); return true;
                case 'g': phonemes.Add("g"); return true;
                case 'h': phonemes.Add("h"); return true;
                case 'k': phonemes.Add("k"); return true;
                case 'l': phonemes.Add("l"); return true;
                case 'm': phonemes.Add("m"); return true;
                case 'n': phonemes.Add("n"); return true;
                case 'p': phonemes.Add("p"); return true;
                case 'q': phonemes.Add("k"); return true;
                case 'r': phonemes.Add("r"); return true;
                case 's': phonemes.Add("s"); return true;
                case 't': phonemes.Add("t"); return true;
            }
        }
        return false;
    }
}