namespace ModularAudience.Audio.Vocal;

internal static class VocalIpa
{
    internal static void Append(List<VocalPronunciationPhone> output, string ipa, CancellationToken cancellationToken)
    {
        bool stress = false;
        for (int i = 0; i < ipa.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = ipa[i];
            if (c == 'ˈ') { stress = true; continue; }
            if (c is 'ˌ' or '.' or ' ') continue;
            if (c == '̃')
            {
                if (output.Count == 0) throw new InvalidOperationException("A nasal IPA marker has no vowel.");
                var previous = output[^1];
                string nasal = previous.Symbol switch
                {
                    "a" or "ah" => "fr-an",
                    "o" or "oh" => "fr-on",
                    "e" or "i" or "ee" => "fr-in",
                    "oe" or "ue" => "fr-un",
                    _ => throw new InvalidOperationException($"Unsupported nasal IPA vowel: {previous.Symbol}"),
                };
                output[^1] = previous with { Symbol = nasal };
                continue;
            }
            if (c == 'ː')
            {
                if (output.Count == 0) throw new InvalidOperationException("An IPA length marker has no sound.");
                output.Add(output[^1] with { Stressed = false });
                continue;
            }
            if (c == 'ʰ') { output.Add(new("h")); continue; }
            if (c is '̪' or '̩' or '͡' or '͜') continue;

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
                _ => null,
            };
            if (symbol is not null) i++;
            else symbol = c switch
            {
                'a' or 'ɑ' => "a",
                'e' or 'ɛ' => "e",
                'i' => "ee",
                'ɪ' => "i",
                'o' or 'ɔ' => "o",
                'u' => "oo",
                'ʊ' => "u",
                'y' => "ue",
                'ø' or 'œ' => "oe",
                'ə' => "de-schwa",
                'ɤ' => "uh",
                'p' => "p", 'b' => "b", 't' => "t", 'd' => "d",
                'k' => "k", 'ɡ' or 'g' => "g",
                'f' => "f", 'v' or 'β' => "v", 's' => "s", 'z' => "z",
                'ʃ' or 'ɕ' or 'ʂ' => "sh", 'ʒ' => "zh",
                'θ' => "th", 'ð' => "dh", 'x' or 'χ' => "ch",
                'h' => "h", 'ʁ' or 'ɣ' => "de-r",
                'm' or 'ɱ' => "m", 'n' => "n", 'ŋ' => "ng",
                'l' => "l", 'r' => "trill-r", 'ɾ' => "tap-r",
                'ɹ' or 'ɻ' => "zh-apical",
                'j' or 'ʝ' => "y", 'w' or 'ɥ' => "w",
                'ɲ' => "n", 'ʎ' => "l",
                _ => throw new InvalidOperationException($"Unsupported IPA sound '{c}' in '{ipa}'."),
            };
            bool vowel = VocalPhonemeInventory.Get(symbol).IsVowel;
            output.Add(new(symbol, stress && vowel));
            if (stress && vowel) stress = false;
            if (c is 'ɲ' or 'ʎ') output.Add(new("y"));
        }
    }
}
