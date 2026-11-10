namespace ModularAudience.Audio.Vocal;

/// <summary>Shared acoustic definitions for phoneme symbols used by all languages.</summary>
public static class VocalPhonemeInventory
{
    private static readonly Dictionary<string, VocalPhoneme> Map = new(StringComparer.Ordinal)
    {
        // Vowels
        ["a"] = V("a", true, 110, [730, 1090, 2440], [1.0, 0.7, 0.4], 0.9, 0.1, true),
        ["ae"] = V("ae", true, 100, [660, 1720, 2410], [1.0, 0.75, 0.4], 0.9, 0.1, true),
        ["ah"] = V("ah", true, 110, [520, 1190, 2390], [1.0, 0.7, 0.4], 0.9, 0.1, true),
        ["ay"] = V("ay", true, 120, [440, 1950, 2500], [1.0, 0.75, 0.4], 0.9, 0.12, true),
        ["e"] = V("e", true, 100, [530, 1840, 2480], [1.0, 0.75, 0.4], 0.9, 0.1, true),
        ["ee"] = V("ee", true, 110, [270, 2290, 3010], [1.0, 0.8, 0.45], 0.9, 0.08, true),
        ["i"] = V("i", true, 90, [390, 1990, 2550], [1.0, 0.75, 0.4], 0.9, 0.1, true),
        ["o"] = V("o", true, 110, [570, 840, 2410], [1.0, 0.65, 0.4], 0.9, 0.1, true),
        ["oh"] = V("oh", true, 110, [370, 730, 2300], [1.0, 0.65, 0.4], 0.9, 0.1, true),
        ["oo"] = V("oo", true, 110, [300, 870, 2240], [1.0, 0.65, 0.4], 0.9, 0.1, true),
        ["u"] = V("u", true, 90, [440, 1020, 2240], [1.0, 0.65, 0.4], 0.9, 0.12, true),
        ["uh"] = V("uh", true, 90, [640, 1190, 2390], [1.0, 0.7, 0.4], 0.9, 0.12, true),
        ["er"] = V("er", true, 100, [490, 1350, 1690], [1.0, 0.7, 0.4], 0.9, 0.1, true),
        ["oe"] = V("oe", true, 110, [320, 1500, 2200], [1.0, 0.75, 0.4], 0.9, 0.1, true), // ö
        ["ue"] = V("ue", true, 110, [320, 1700, 2200], [1.0, 0.75, 0.4], 0.9, 0.1, true), // ü
        // Nasals
        ["m"] = V("m", true, 70, [250, 1000, 2000], [0.6, 0.3, 0.2], 0.85, 0.15, false),
        ["n"] = V("n", true, 70, [250, 1500, 2300], [0.6, 0.4, 0.2], 0.85, 0.15, false),
        ["ng"] = V("ng", true, 80, [250, 1000, 2200], [0.6, 0.3, 0.2], 0.85, 0.15, false),
        // Liquids/glides
        ["l"] = V("l", true, 70, [300, 1100, 2500], [0.7, 0.4, 0.3], 0.8, 0.2, false),
        ["r"] = V("r", true, 70, [300, 1200, 1600], [0.7, 0.4, 0.3], 0.8, 0.2, false),
        ["w"] = V("w", true, 60, [300, 600, 2200], [0.6, 0.4, 0.3], 0.7, 0.15, false),
        ["y"] = V("y", true, 60, [250, 2000, 2800], [0.6, 0.5, 0.3], 0.7, 0.15, false),
        // Fricatives (voiced)
        ["v"] = V("v", true, 80, [200, 1200, 2200], [0.3, 0.3, 0.2], 0.6, 0.6, false),
        ["z"] = V("z", true, 80, [200, 1400, 2300], [0.3, 0.3, 0.2], 0.6, 0.6, false),
        ["zh"] = V("zh", true, 90, [200, 1300, 2200], [0.3, 0.3, 0.2], 0.6, 0.6, false),
        // Fricatives (unvoiced)
        ["f"] = V("f", false, 80, [], [], 0.0, 1.0, false),
        ["s"] = V("s", false, 80, [], [], 0.0, 1.0, false),
        ["sh"] = V("sh", false, 90, [], [], 0.0, 1.0, false),
        ["ch"] = V("ch", false, 90, [], [], 0.0, 1.0, false), // loch / x
        ["h"] = V("h", false, 60, [], [], 0.0, 0.8, false),
        ["th"] = V("th", false, 80, [], [], 0.0, 1.0, false),
        // Stops (unvoiced/voiced) - modeled as short noise bursts
        ["p"] = V("p", false, 60, [], [], 0.0, 0.9, false),
        ["b"] = V("b", true, 60, [250, 1000, 2000], [0.4, 0.2, 0.1], 0.7, 0.5, false),
        ["t"] = V("t", false, 60, [], [], 0.0, 0.9, false),
        ["d"] = V("d", true, 60, [250, 1500, 2300], [0.4, 0.2, 0.1], 0.7, 0.5, false),
        ["k"] = V("k", false, 60, [], [], 0.0, 0.9, false),
        ["g"] = V("g", true, 60, [250, 1500, 2300], [0.4, 0.2, 0.1], 0.7, 0.5, false),
        // Affricates
        ["ts"] = V("ts", false, 90, [], [], 0.0, 1.0, false),
        ["dz"] = V("dz", true, 90, [250, 1500, 2300], [0.4, 0.2, 0.1], 0.7, 0.6, false),
        // Silence
        ["sil"] = V("sil", false, 40, [], [], 0.0, 0.0, false),
    };

    private static VocalPhoneme V(string symbol, bool voiced, double dur, double[] formants, double[] amps, double voicing, double noise, bool vowel)
        => new()
        {
            Symbol = symbol,
            Voiced = voiced,
            DurationMs = dur,
            Formants = formants,
            FormantAmplitudes = amps.Length == 0 ? new double[formants.Length] : amps,
            Voicing = voicing,
            Noise = noise,
            IsVowel = vowel,
        };

    public static VocalPhoneme Get(string symbol)
    {
        if (Map.TryGetValue(symbol, out var p)) return p;
        return Map["uh"];
    }

    public static bool Contains(string symbol) => Map.ContainsKey(symbol);

    public static IReadOnlyCollection<string> Symbols => Map.Keys;
}