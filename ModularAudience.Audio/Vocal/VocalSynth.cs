using System.Globalization;

namespace ModularAudience.Audio.Vocal;

public static class VocalSynth
{
    public static AudioObj Render(VocalSynthSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var s = settings.Clone();
        int rate = s.SampleRate, channels = s.Channels;

        var tokens = VocalTextParser.Parse(s.Language, s.Text, s.EmphasisSyntax);
        // Build render list
        var random = new Random(HashCode.Combine(s.VoiceName, s.Text, s.Language));
        var pcm = new List<float>();

        foreach (var vt in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (vt is VocalPauseToken pause)
            {
                int pauseMs = (int)Math.Round(pause.Milliseconds / s.Speed);
                int frames = Math.Max(1, (int)(rate * pauseMs / 1000.0));
                AppendSilence(pcm, channels, frames);
                continue;
            }
            if (vt is VocalPhonemeToken pt)
            {
                var phoneme = VocalPhonemeInventory.Get(pt.Phoneme);
                RenderPhoneme(pcm, phoneme, pt.Emphasis, s, rate, channels, random, cancellationToken);
            }
        }

        if (pcm.Count == 0)
        {
            int frames = (int)(rate * 0.5);
            AppendSilence(pcm, channels, frames);
        }

        // Peak normalize (attenuate only)
        float peak = 0;
        for (int i = 0; i < pcm.Count; i++) peak = Math.Max(peak, Math.Abs(pcm[i]));
        if (peak > 0.95f)
        {
            float scale = 0.95f / peak;
            for (int i = 0; i < pcm.Count; i++) pcm[i] *= scale;
        }

        var data = pcm.ToArray();
        var result = new AudioObj
        {
            Data = data,
            SampleRate = rate,
            Channels = channels,
            BitDepth = 32,
            Length = data.Length,
            Duration = TimeSpan.FromSeconds(data.Length / (double)(rate * channels)),
            Bpm = 120,
            Volume = 100,
        };
        result.Rename($"{s.VoiceName} · {s.Language.DisplayName()} · Vocal");
        return result;
    }

    private static void AppendSilence(List<float> pcm, int channels, int frames)
    {
        for (int i = 0; i < frames * channels; i++) pcm.Add(0f);
    }

    private static void RenderPhoneme(List<float> pcm, VocalPhoneme ph, VocalEmphasis emphasis, VocalSynthSettings s, int rate, int channels, Random random, CancellationToken cancellationToken)
    {
        // Emphasis adjustments
        double pitchMul = 1.0;
        double durMul = 1.0;
        double gainMul = 1.0;
        double breathAdd = 0.0;
        double voicingMul = 1.0;
        bool doVibrato = true;
        switch (emphasis)
        {
            case VocalEmphasis.Strong:
                pitchMul = 1.08; durMul = 1.1; gainMul = 1.2; voicingMul = 1.05; break;
            case VocalEmphasis.Whisper:
                pitchMul = 1.0; durMul = 1.0; gainMul = 0.7; breathAdd = 0.5; voicingMul = 0.3; doVibrato = false; break;
            case VocalEmphasis.Lengthened:
                durMul = 1.5; break;
            case VocalEmphasis.PitchUp:
                pitchMul = 1.12; break;
            case VocalEmphasis.PitchDown:
                pitchMul = 0.88; break;
        }

        double pitch = s.BasePitchHz * pitchMul;
        // roughness jitter seed
        double rough = s.Roughness;

        int baseMs = (int)Math.Round(ph.DurationMs / s.Speed * durMul);
        int frames = Math.Max(4, (int)(rate * baseMs / 1000.0));

        // formants scaled
        var formants = new double[ph.Formants.Length];
        for (int f = 0; f < formants.Length; f++) formants[f] = ph.Formants[f] * s.FormantScale;

        // fade
        int fadeIn = Math.Min(frames / 4, (int)(rate * 0.008));
        int fadeOut = Math.Min(frames / 4, (int)(rate * 0.008));
        if (fadeIn < 1) fadeIn = 1;
        if (fadeOut < 1) fadeOut = 1;

        double prevValue = 0;
        for (int i = 0; i < frames; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double t = i / (double)rate;
            // vibrato
            double vibCents = 0;
            if (doVibrato && s.VibratoCents > 0)
            {
                vibCents = s.VibratoCents * (1 - Math.Exp(-t * 5)) * Math.Sin(2 * Math.PI * s.VibratoHz * t);
            }
            double f0 = pitch * Math.Pow(2, vibCents / 1200.0);
            if (rough > 0)
            {
                f0 *= 1 + (random.NextDouble() * 2 - 1) * rough * 0.03;
            }

            // Richer voiced source: additive harmonics with spectral tilt and formant boosts.
            double voice = 0;
            if (ph.Voiced && ph.Formants.Length > 0)
            {
                int nHarm = 14;
                for (int h = 1; h <= nHarm; h++)
                {
                    double freq = f0 * h;
                    if (freq > rate * 0.45) break;
                    double hAmp = 1.0 / h;
                    for (int f = 0; f < formants.Length; f++)
                    {
                        double fr = formants[f];
                        if (fr <= 0) continue;
                        double bw = 130.0;
                        double diff = freq - fr;
                        hAmp *= 1.0 + 1.6 * Math.Exp(-(diff * diff) / (2 * bw * bw));
                    }
                    voice += hAmp * Math.Sin(2 * Math.PI * freq * t);
                }
                voice *= 0.16 * ph.Voicing * voicingMul * s.Voicing;
            }

            // noise
            double noise = (random.NextDouble() * 2 - 1);
            double noiseContrib = noise * ph.Noise * s.Noise;

            double breath = (random.NextDouble() * 2 - 1) * (s.Breathiness + breathAdd);

            double value = voice;
            if (!ph.Voiced)
            {
                value = noise * Math.Max(0.5, ph.Noise) * s.Noise;
            }
            else
            {
                value += noiseContrib * 0.25;
            }
            value += breath * 0.2;

            // envelope within phoneme
            double env = 1.0;
            if (i < fadeIn) env = i / (double)fadeIn;
            if (i >= frames - fadeOut) env *= (frames - i) / (double)fadeOut;
            value *= env * gainMul;

            // gentle smoothing to reduce harshness
            value = 0.55 * value + 0.45 * prevValue;
            prevValue = value;

            // write channels
            float sample = (float)Math.Clamp(value, -1f, 1f);
            for (int c = 0; c < channels; c++)
            {
                pcm.Add(sample);
            }
        }
    }
}