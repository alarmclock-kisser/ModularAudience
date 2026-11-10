using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Xml;
using ModularAudience.Audio.Effects.AutoTune;

namespace ModularAudience.Audio.Vocal;

internal static class VocalWindowsSpeech
{
    private const string SsmlNamespace = "http://www.w3.org/2001/10/synthesis";
    private static readonly string[] PitchNames = ["x-low", "low", "medium", "high", "x-high"];
    private static readonly double[] PitchRatios = [0.79, 0.92, 1, 1.12, 1.30];

    internal static AudioObj Render(VocalSynthSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Text.Length > 100_000)
            throw new InvalidOperationException("The text is too long for Windows speech synthesis.");

        var culture = CultureInfo.GetCultureInfo(settings.Language == VocalLanguage.German ? "de-DE" : "en-US");
        var tokens = VocalTextParser.ParseWindowsSpeech(settings.Language, settings.Text, settings.EmphasisSyntax, cancellationToken);
        using var synthesizer = new SpeechSynthesizer();
        VoiceInfo voice = SelectVoice(synthesizer, culture, settings.BasePitchHz);
        double nativePitch = voice.Gender == VoiceGender.Male ? 125 : voice.Gender == VoiceGender.Female ? 195 : 170;
        int pitchLevel = SelectPitchLevel(settings.BasePitchHz / nativePitch);
        string ssml = BuildSsml(tokens, voice.Culture, settings.Speed, pitchLevel, cancellationToken);
        using var stream = new BoundedAudioStream(2L * (VocalSynth.MaxOutputSamples / settings.Channels));
        synthesizer.SetOutputToAudioStream(stream,
            new SpeechAudioFormatInfo(settings.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        var completion = new TaskCompletionSource<SpeakCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        synthesizer.SpeakCompleted += (_, args) => completion.TrySetResult(args);
        cancellationToken.ThrowIfCancellationRequested();
        synthesizer.SpeakSsmlAsync(ssml);
        while (!completion.Task.Wait(50))
        {
            if (!cancellationToken.IsCancellationRequested) continue;
            synthesizer.SpeakAsyncCancelAll();
            cancellationToken.ThrowIfCancellationRequested();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var finished = completion.Task.GetAwaiter().GetResult();
        if (finished.Error is not null)
            throw new InvalidOperationException($"Windows speech synthesis failed: {finished.Error.Message}", finished.Error);
        if (finished.Cancelled)
            throw new InvalidOperationException("Windows speech synthesis was canceled unexpectedly.");
        synthesizer.SetOutputToNull();

        float[] mono = ReadAudio(stream, cancellationToken);
        mono = ApplyVoiceProfile(mono, nativePitch * PitchRatios[pitchLevel], settings, cancellationToken);
        var data = new float[checked(mono.Length * settings.Channels)];
        for (int i = 0; i < mono.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int channel = 0; channel < settings.Channels; channel++)
                data[i * settings.Channels + channel] = mono[i];
        }
        return VocalSynth.CreateAudio(data, settings, cancellationToken);
    }

    private static VoiceInfo SelectVoice(SpeechSynthesizer synthesizer, CultureInfo culture, double pitch)
    {
        VoiceGender preferred = pitch < 160 ? VoiceGender.Male : VoiceGender.Female;
        var voice = synthesizer.GetInstalledVoices()
            .Where(item => item.Enabled && item.VoiceInfo.Culture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
            .OrderByDescending(item => item.VoiceInfo.Gender == preferred)
            .Select(item => item.VoiceInfo)
            .FirstOrDefault();
        if (voice is null)
            throw new InvalidOperationException(
                $"No Windows speech voice for {culture.EnglishName} is installed. Install one in Windows settings or disable Natural speech.");
        synthesizer.SelectVoice(voice.Name);
        return voice;
    }

    private static int SelectPitchLevel(double ratio)
    {
        int best = 0;
        for (int i = 1; i < PitchRatios.Length; i++)
            if (Math.Abs(Math.Log(ratio / PitchRatios[i])) < Math.Abs(Math.Log(ratio / PitchRatios[best])))
                best = i;
        return best;
    }

    internal static string BuildSsml(
        IReadOnlyList<VocalToken> tokens, CultureInfo culture, double speed,
        int pitchLevel = 2, CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true });
        writer.WriteStartElement("speak", SsmlNamespace);
        writer.WriteAttributeString("version", "1.0");
        writer.WriteAttributeString("xml", "lang", null, culture.Name);
        writer.WriteStartElement("prosody", SsmlNamespace);
        writer.WriteAttributeString("rate", (speed * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%");
        writer.WriteAttributeString("pitch", PitchNames[pitchLevel]);
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token is VocalPauseToken pause)
            {
                double milliseconds = pause.Milliseconds / speed;
                if (!double.IsFinite(milliseconds) || milliseconds < 0 || milliseconds > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(tokens), "A speech pause has an invalid duration.");
                writer.WriteStartElement("break", SsmlNamespace);
                writer.WriteAttributeString("time", Math.Round(milliseconds).ToString(CultureInfo.InvariantCulture) + "ms");
                writer.WriteEndElement();
            }
            else if (token is VocalTextToken text)
            {
                WriteText(writer, text, pitchLevel);
            }
            else throw new InvalidOperationException("Unknown Windows speech token.");
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Flush();
        return output.ToString();
    }

    private static void WriteText(XmlWriter writer, VocalTextToken token, int pitchLevel)
    {
        (string? attribute, string? value) = token.Emphasis switch
        {
            VocalEmphasis.Strong => ("volume", "loud"),
            VocalEmphasis.Whisper => ("volume", "soft"),
            VocalEmphasis.Lengthened => ("rate", "70%"),
            VocalEmphasis.PitchUp => ("pitch", PitchNames[Math.Min(pitchLevel + 1, PitchNames.Length - 1)]),
            VocalEmphasis.PitchDown => ("pitch", PitchNames[Math.Max(pitchLevel - 1, 0)]),
            _ => (null, null),
        };
        if (attribute is not null)
        {
            writer.WriteStartElement("prosody", SsmlNamespace);
            writer.WriteAttributeString(attribute, value);
        }
        writer.WriteString(token.Text);
        if (attribute is not null) writer.WriteEndElement();
    }

    private static float[] ReadAudio(MemoryStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length == 0 || stream.Length % 2 != 0)
            throw new InvalidOperationException("Windows speech did not produce valid PCM audio.");
        byte[] bytes = stream.GetBuffer();
        var samples = new float[checked((int)(stream.Length / 2))];
        for (int i = 0; i < samples.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            short sample = unchecked((short)(bytes[2 * i] | bytes[2 * i + 1] << 8));
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    private static float[] ApplyVoiceProfile(
        float[] source, double nativePitch, VocalSynthSettings settings, CancellationToken cancellationToken)
    {
        double shift = 12 * Math.Log2(settings.BasePitchHz / nativePitch);
        if (Math.Abs(shift) > 0.5)
        {
            double ratio = Math.Pow(2, shift / 12);
            double preservation = Math.Clamp(Math.Log(settings.FormantScale / ratio) / Math.Log(1 / ratio) * 100, 0, 100);
            var quality = new AutoTuneSettings
            {
                FftSize = settings.SampleRate < 12000 ? 1024 : settings.SampleRate >= 96000 ? 4096 : 2048,
                Overlap = 8,
                FormantPreservationPercent = preservation,
            };
            var curve = new AutoTuneCorrectionCurve([shift], [1f], 1, 0, 0);
            source = AutoTunePhaseVocoder.Render(source, settings.SampleRate, 1, quality, curve, 1, null, cancellationToken);
        }
        return ColorVoice(source, settings, cancellationToken);
    }

    private static float[] ColorVoice(float[] source, VocalSynthSettings settings, CancellationToken cancellationToken)
    {
        var result = new float[source.Length];
        double depth = settings.VibratoHz > 0
            ? (Math.Pow(2, settings.VibratoCents / 1200) - 1) * settings.SampleRate / (2 * Math.PI * settings.VibratoHz)
            : 0;
        if (!double.IsFinite(depth) || depth > settings.SampleRate * 0.25)
            throw new ArgumentOutOfRangeException(nameof(settings.VibratoHz), "The vibrato rate is too low for the requested depth.");
        double tilt = Math.Clamp((settings.FormantScale - 1) * 1.25, -0.5, 0.5);
        double lowAlpha = 1 - Math.Exp(-2 * Math.PI * 1500 / settings.SampleRate);
        double envelopeAlpha = 1 - Math.Exp(-1.0 / (0.015 * settings.SampleRate));
        double low = 0, envelope = 0;
        var random = new Random(VocalSynth.StableSeed(settings));
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            double time = i / (double)settings.SampleRate;
            double position = i - depth * (1 + Math.Sin(2 * Math.PI * settings.VibratoHz * time));
            double value = 0;
            if (position >= 0)
            {
                int left = (int)position;
                int right = Math.Min(left + 1, source.Length - 1);
                value = source[left] + (source[right] - source[left]) * (position - left);
            }
            low += lowAlpha * (value - low);
            value += tilt * (value - low);
            envelope += envelopeAlpha * (Math.Abs(value) - envelope);
            value *= (0.7 + 0.3 * settings.Voicing)
                * (1 + 0.06 * settings.Roughness * Math.Sin(2 * Math.PI * 17 * time));
            value += (random.NextDouble() * 2 - 1) * envelope * settings.Breathiness * (0.25 + 0.75 * settings.Noise) * 0.08;
            result[i] = (float)value;
        }
        return result;
    }

    private sealed class BoundedAudioStream(long maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckLength(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckLength(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            CheckLength(1);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            if (value > maximumBytes) throw new InvalidOperationException("The vocal synthesis output would exceed the supported size.");
            base.SetLength(value);
        }

        private void CheckLength(int count)
        {
            if (Position + count > maximumBytes)
                throw new InvalidOperationException("The vocal synthesis output would exceed the supported size.");
        }
    }
}
