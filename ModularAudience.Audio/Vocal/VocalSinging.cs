using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Vocal;

internal sealed record VocalSingingSyllable(
    IReadOnlyList<VocalToken> Tokens,
    double PauseBeforeMilliseconds,
    double NaturalDurationMilliseconds,
    int SourceStart,
    int SourceLength);

internal static class VocalSinging
{
    private static readonly int[] Melody = [0, 4, 7, 5, 2, 4, 7, 0];

    internal static int Note(int index) => Melody[Math.Max(0, index) % Melody.Length];

    internal static (IReadOnlyList<VocalSingingSyllable> Syllables, double TrailingPauseMilliseconds) CreateSyllables(
        VocalLanguage language, IReadOnlyList<VocalToken> tokens, CancellationToken cancellationToken)
    {
        List<VocalSingingSyllable> syllables = [];
        List<VocalToken> leading = [];
        List<VocalToken> current = [];
        double pendingPause = 0;
        string? previousSymbol = null;

        void FlushCurrent()
        {
            if (current.Count == 0) return;
            syllables.Add(CreateSyllable(current, pendingPause, language));
            current.Clear();
            pendingPause = 0;
        }

        foreach (VocalToken token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token is VocalPauseToken pause)
            {
                FlushCurrent();
                pendingPause += pause.Milliseconds;
                previousSymbol = null;
                continue;
            }
            if (token is VocalCustomSampleToken sample)
            {
                if (sample.HasNucleus)
                {
                    FlushCurrent();
                    if (leading.Count > 0)
                    {
                        current.AddRange(leading);
                        leading.Clear();
                    }
                    current.Add(sample);
                }
                else if (current.Count > 0)
                {
                    current.Add(sample);
                }
                else
                {
                    leading.Add(sample);
                }
                previousSymbol = null;
                continue;
            }
            if (token is not VocalPhonemeToken phoneme) continue;

            if (!VocalPhonemeInventory.Contains(phoneme.Phoneme))
                throw new InvalidOperationException($"Unknown vocal phoneme: {phoneme.Phoneme}");
            VocalPhoneme definition = VocalPhonemeInventory.Get(phoneme.Phoneme);
            if (phoneme.Phoneme == "unsupported")
            {
                if (current.Count > 0) current.Add(phoneme);
                else leading.Add(phoneme);
                previousSymbol = null;
                continue;
            }

            bool beat = phoneme.Phoneme.StartsWith("beat-", StringComparison.Ordinal);
            bool newNucleus = beat || definition.IsVowel
                && (language == VocalLanguage.AnimalCrossing || phoneme.Phoneme != previousSymbol);
            if (newNucleus)
            {
                FlushCurrent();
                if (leading.Count > 0)
                {
                    current.AddRange(leading);
                    leading.Clear();
                }
                current.Add(phoneme);
            }
            else if (current.Count > 0)
            {
                current.Add(phoneme);
            }
            else
            {
                leading.Add(phoneme);
            }
            previousSymbol = phoneme.Phoneme;
        }
        FlushCurrent();

        if (leading.Count > 0)
        {
            if (syllables.Count == 0)
            {
                syllables.Add(CreateSyllable(leading, pendingPause, language));
                pendingPause = 0;
            }
            else
            {
                VocalSingingSyllable last = syllables[^1];
                List<VocalToken> combined = [.. last.Tokens, .. leading];
                syllables[^1] = CreateSyllable(combined, last.PauseBeforeMilliseconds, language);
            }
        }

        return (syllables, pendingPause);
    }

    private static VocalSingingSyllable CreateSyllable(
        IReadOnlyList<VocalToken> tokens, double pauseBeforeMilliseconds, VocalLanguage language)
    {
        double naturalDuration = 0;
        int sourceStart = int.MaxValue;
        int sourceEnd = -1;
        foreach (VocalToken token in tokens)
        {
            if (token is VocalCustomSampleToken sample)
            {
                naturalDuration += sample.DurationMilliseconds;
                if (sample.SourceStart >= 0)
                {
                    sourceStart = Math.Min(sourceStart, sample.SourceStart);
                    sourceEnd = Math.Max(sourceEnd, sample.SourceStart + sample.SourceLength);
                }
                continue;
            }
            if (token is not VocalPhonemeToken phoneme) continue;
            VocalPhoneme definition = VocalPhonemeInventory.Get(phoneme.Phoneme);
            bool beat = phoneme.Phoneme.StartsWith("beat-", StringComparison.Ordinal);
            double milliseconds = definition.DurationMs * (beat ? 1 : VocalSynth.DurationMultiplier(phoneme));
            if (definition.IsVowel)
                milliseconds *= language == VocalLanguage.AnimalCrossing ? 1.35 : 1.75;
            naturalDuration += milliseconds;
            if (phoneme.SourceStart >= 0)
            {
                sourceStart = Math.Min(sourceStart, phoneme.SourceStart);
                sourceEnd = Math.Max(sourceEnd, phoneme.SourceStart + phoneme.SourceLength);
            }
        }
        return new VocalSingingSyllable(tokens.ToArray(), pauseBeforeMilliseconds,
            Math.Max(1, naturalDuration), sourceEnd < 0 ? -1 : sourceStart, sourceEnd < 0 ? 0 : sourceEnd - sourceStart);
    }

    internal static float[] RenderWindows(
        float[] source, VocalSynthSettings settings, CancellationToken cancellationToken, long startFrame = 0)
    {
        int rate = settings.SampleRate;
        int hop = Math.Max(1, rate / 100);
        var pitches = PyinPitchTracker.Track(source, rate, hop, 55, Math.Min(900, rate * 0.45), 1, cancellationToken);
        var shifts = new double[pitches.Length];
        var wet = new float[pitches.Length];
        double previousShift = 0, previousWet = 0;
        bool haveShift = false;
        for (int i = 0; i < pitches.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool voiced = pitches[i].FrequencyHz > 0;
            if (voiced)
            {
                double seconds = (startFrame + (long)i * hop) / (double)rate;
                int note = (int)(seconds * settings.Speed / 0.32);
                double target = settings.BasePitchHz * Math.Pow(2, Note(note) / 12.0);
                double desired = 12 * Math.Log2(target / pitches[i].FrequencyHz);
                previousShift = haveShift ? previousShift + 0.55 * (desired - previousShift) : desired;
                haveShift = true;
            }
            previousWet += 0.4 * ((voiced ? 1 : 0) - previousWet);
            shifts[i] = previousShift;
            wet[i] = (float)previousWet;
        }
        if (!haveShift) return source;

        var quality = new AutoTuneSettings
        {
            FftSize = rate < 12000 ? 1024 : rate >= 96000 ? 4096 : 2048,
            Overlap = 8,
            FormantPreservationPercent = 70,
        };
        var curve = new AutoTuneCorrectionCurve(shifts, wet, hop, 0, 0);
        float[] shifted = AutoTunePhaseVocoder.Render(source, rate, 1, quality, curve, 1, null, cancellationToken);
        for (int i = 0; i < source.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            double amount = curve.WetAt(i);
            shifted[i] = (float)(source[i] + (shifted[i] - source[i]) * amount);
        }
        return shifted;
    }

    internal static float[] RenderWindowsAtPitch(
        float[] source, VocalSynthSettings settings, double targetFrequency, CancellationToken cancellationToken)
    {
        int rate = settings.SampleRate;
        int hop = Math.Max(1, rate / 100);
        var pitches = PyinPitchTracker.Track(source, rate, hop, 40, Math.Min(6000, rate * 0.45), 1, cancellationToken);
        var shifts = new double[pitches.Length];
        var wet = new float[pitches.Length];
        double previousShift = 0, previousWet = 0;
        bool haveShift = false;
        for (int i = 0; i < pitches.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool voiced = pitches[i].FrequencyHz > 0;
            if (voiced)
            {
                double desired = 12 * Math.Log2(targetFrequency / pitches[i].FrequencyHz);
                previousShift = haveShift ? previousShift + 0.55 * (desired - previousShift) : desired;
                haveShift = true;
            }
            previousWet += 0.4 * ((voiced ? 1 : 0) - previousWet);
            shifts[i] = previousShift;
            wet[i] = (float)previousWet;
        }
        if (!haveShift) return source;

        var quality = new AutoTuneSettings
        {
            FftSize = rate < 12000 ? 1024 : rate >= 96000 ? 4096 : 2048,
            Overlap = 8,
            FormantPreservationPercent = 70,
        };
        var curve = new AutoTuneCorrectionCurve(shifts, wet, hop, 0, 0);
        float[] shifted = AutoTunePhaseVocoder.Render(source, rate, 1, quality, curve, 1, null, cancellationToken);
        for (int i = 0; i < source.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            double amount = curve.WetAt(i);
            shifted[i] = (float)(source[i] + (shifted[i] - source[i]) * amount);
        }
        return shifted;
    }
}
