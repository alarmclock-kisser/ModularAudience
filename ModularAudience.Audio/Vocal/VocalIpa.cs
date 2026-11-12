using System.Text;

namespace ModularAudience.Audio.Vocal;

internal static class VocalIpa
{
    internal static bool TryParseNotation(
        string text, CancellationToken cancellationToken, out List<VocalPronunciationPhone> phones)
    {
        string notation = text;
        bool delimited = notation.Length >= 2
            && (notation[0] == '/' && notation[^1] == '/'
                || notation[0] == '[' && notation[^1] == ']');
        if (delimited) notation = notation[1..^1];

        bool hasIpaSymbol = false;
        bool hasOtherScript = false;
        foreach (Rune rune in notation.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int value = rune.Value;
            hasIpaSymbol |= IsIpaSpecificCharacter(value);
            hasOtherScript |= value is >= 0x0600 and <= 0x08ff
                or >= 0x3040 and <= 0x30ff
                or >= 0x3400 and <= 0x9fff
                or >= 0xac00 and <= 0xd7af
                or >= 0x0400 and <= 0x052f;
        }

        if ((!delimited && !hasIpaSymbol) || hasOtherScript)
        {
            phones = [];
            return false;
        }

        phones = [];
        Append(phones, notation, cancellationToken);
        return phones.Count > 0;
    }

    internal static bool IsIpaSpecificCharacter(int value) =>
        value is >= 0x0250 and <= 0x02ff
            or >= 0x1c0 and <= 0x1c3
            or 0x0303 or 0x0329 or 0x032a or 0x035c or 0x0361
            or 0x03b8 or 0x03b2 or 0x03c7 or 0x03b3
            or 0x203c;

    internal static void Append(List<VocalPronunciationPhone> output, string ipa, CancellationToken cancellationToken)
    {
        bool stress = false;
        for (int i = 0; i < ipa.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = char.ToLowerInvariant(ipa[i]);
            if (c == 'ˈ') { stress = true; continue; }
            if (c is 'ˌ' or '.' or ' ') continue;
            if (c == '̃')
            {
                if (output.Count == 0)
                {
                    output.Add(new("n"));
                    continue;
                }
                var previous = output[^1];
                string? nasal = previous.Symbol switch
                {
                    "a" or "ah" => "fr-an",
                    "o" or "oh" => "fr-on",
                    "e" or "i" or "ee" => "fr-in",
                    "oe" or "ue" => "fr-un",
                    _ => null,
                };
                if (nasal is not null) output[^1] = previous with { Symbol = nasal };
                else output.Add(new("n"));
                continue;
            }
            if (c == 'ː')
            {
                if (output.Count == 0) output.Add(new("uh"));
                else output.Add(output[^1] with { Stressed = false });
                continue;
            }
            if (c == 'ʰ') { output.Add(new("h")); continue; }
            if (c is '̪' or '̩' or '͡' or '͜' or 'ʼ' or 'ˑ'
                or '˥' or '˦' or '˧' or '˨' or '˩' or 'ꜛ' or 'ꜜ'
                or '̯' or '̞' or '̝' or '̟' or '̠' or '̚' or '̤' or '̰' or '̥'
                or '̬' or '̹' or '̜' or '̘' or '̙' or '̪' or '͈' or '͉') continue;

            string? pair = i + 1 < ipa.Length ? ipa.Substring(i, 2) : null;
            string? symbol = pair switch
            {
                "tʃ" or "tɕ" or "ʈʂ" => "tsh",
                "dʒ" or "ɟʝ" => "j",
                "aɪ" => "eye",
                "aʊ" => "ow",
                "eɪ" => "ay",
                "oʊ" => "oh",
                "ts" => "ts",
                "dz" => "dz",
                "t͡ʃ" or "t͜ʃ" => "tsh",
                "d͡ʒ" or "d͜ʒ" => "j",
                _ => null,
            };
            if (symbol is not null) i++;
            else symbol = c switch
            {
                'a' or 'ɑ' or 'ɐ' => "a",
                'ɒ' => "o",
                'e' or 'ɛ' => "e",
                'ɘ' or 'ə' => "de-schwa",
                'ɵ' or 'ɞ' => "uh",
                'ɜ' => "er",
                'ɨ' => "i",
                'i' => "ee",
                'ɪ' => "i",
                'o' or 'ɔ' => "o",
                'ɶ' => "ae",
                'u' => "oo",
                'ʊ' => "u",
                'ɯ' or 'ʉ' => "oo",
                'y' => "ue",
                'ø' or 'œ' => "oe",
                'ɤ' => "uh",
                'p' => "p", 'b' => "b", 't' => "t", 'd' => "d",
                'c' or 'q' => "k",
                'k' => "k", 'ɡ' or 'g' => "g",
                'f' or 'ɸ' or 'ʍ' => "f", 'v' or 'β' or 'ʋ' => "v",
                's' => "s", 'z' or 'ʑ' => "z",
                'ʃ' or 'ɕ' or 'ʂ' => "sh", 'ʒ' => "zh",
                'ɬ' => "sh", 'ɮ' => "zh",
                'θ' => "th", 'ð' => "dh", 'x' or 'χ' => "ch",
                'h' or 'ɦ' or 'ʜ' => "h", 'ʔ' => "t",
                'ʁ' or 'ɣ' or 'ʕ' => "de-r",
                'm' or 'ɱ' => "m", 'n' or 'ɳ' => "n", 'ŋ' or 'ɴ' => "ng",
                'l' or 'ɭ' or 'ɺ' => "l", 'r' or 'ʀ' or 'ʙ' => "trill-r", 'ɾ' or 'ɽ' => "tap-r",
                'ɹ' or 'ɻ' => "zh-apical",
                'ʈ' => "t", 'ɖ' => "d",
                'j' or 'ʝ' => "y", 'w' or 'ɥ' => "w",
                'ɲ' => "n", 'ʎ' => "l",
                'ʘ' => "beat-p",
                'ǀ' or 'ʇ' => "beat-t",
                'ǃ' or 'ʗ' => "beat-k",
                'ǂ' => "beat-tch",
                'ǁ' => "beat-ts",
                _ => "unsupported",
            };
            bool vowel = VocalPhonemeInventory.Get(symbol).IsVowel;
            output.Add(new(symbol, stress && vowel));
            if (stress && vowel) stress = false;
            if (c is 'ɲ' or 'ʎ') output.Add(new("y"));
        }
    }
}
