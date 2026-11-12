using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Xml;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Processors_V1;

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
        if (settings.Singing && settings.SingingMidiFile is not null)
            return RenderMidi(settings, cancellationToken);

        var culture = CultureInfo.GetCultureInfo(settings.Language == VocalLanguage.German ? "de-DE" : "en-US");
        var tokens = VocalTextParser.ParseWindowsSpeech(settings.Language, settings.Text, settings.EmphasisSyntax, cancellationToken);
        using var synthesizer = new SpeechSynthesizer();
        VoiceInfo voice = SelectVoice(synthesizer, culture, settings.BasePitchHz);
        double nativePitch = voice.Gender == VoiceGender.Male ? 125 : voice.Gender == VoiceGender.Female ? 195 : 170;
        int pitchLevel = SelectPitchLevel(settings.BasePitchHz / nativePitch);
        bool hasCapitalized = tokens.OfType<VocalTextToken>().Any(token => token.Capitalized);
        double speechSpeed = settings.Singing ? settings.Speed * 0.8 : settings.Speed;
        string ssml = BuildSsml(tokens, voice.Culture, speechSpeed, pitchLevel, cancellationToken);
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
        if (hasCapitalized)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                mono[i] *= 1.55f;
            }
        }
        var data = new float[checked(mono.Length * settings.Channels)];
        for (int i = 0; i < mono.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int channel = 0; channel < settings.Channels; channel++)
                data[i * settings.Channels + channel] = mono[i];
        }
        return VocalSynth.CreateAudio(data, settings, cancellationToken);
    }

    private static AudioObj RenderMidi(VocalSynthSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<VocalToken> phoneticTokens = VocalTextParser.Parse(
            settings.Language, settings.Text, settings.EmphasisSyntax, cancellationToken);
        VocalMidiSingingPlan plan = VocalMidiSinging.CreatePlan(settings, phoneticTokens, cancellationToken);
        IReadOnlyList<VocalToken> speechTokens = VocalTextParser.ParseWindowsSpeech(
            settings.Language, settings.Text, settings.EmphasisSyntax, cancellationToken);
        Dictionary<int, VocalSyllableTextRange> ranges = VocalTextParser.GetSyllableTextRanges(
                settings.Language, settings.Text, settings.EmphasisSyntax, cancellationToken)
            .GroupBy(range => range.Index)
            .ToDictionary(group => group.Key, group => group.First());

        var culture = CultureInfo.GetCultureInfo(settings.Language == VocalLanguage.German ? "de-DE" : "en-US");
        using var synthesizer = new SpeechSynthesizer();
        VoiceInfo voice = SelectVoice(synthesizer, culture, settings.BasePitchHz);
        double nativePitch = voice.Gender == VoiceGender.Male ? 125 : voice.Gender == VoiceGender.Female ? 195 : 170;
        int pitchLevel = SelectPitchLevel(settings.BasePitchHz / nativePitch);
        int channels = settings.Channels;
        float[] output = new float[checked(plan.OutputFrames * channels)];

        foreach (IGrouping<int, VocalMidiScheduledNote> syllableNotes in plan.Notes.GroupBy(note => note.SyllableIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int syllableIndex = syllableNotes.Key;
            VocalSingingSyllable syllable = plan.Syllables[syllableIndex];
            VocalSyllableTextRange range;
            if (!ranges.TryGetValue(syllableIndex, out range))
            {
                if (syllable.SourceStart < 0 || syllable.SourceLength <= 0
                    || syllable.SourceStart + syllable.SourceLength > settings.Text.Length)
                    throw new InvalidOperationException("A MIDI singing syllable could not be mapped back to source text.");
                range = new VocalSyllableTextRange(
                    syllable.SourceStart, syllable.SourceLength, syllableIndex, 1);
            }

            IReadOnlyList<VocalToken> syllableSpeechTokens = GetSyllableSpeechTokens(speechTokens, range);
            if (syllableSpeechTokens.Count == 0)
                throw new InvalidOperationException("A MIDI singing syllable contains no Windows speech text.");

            int longestNoteFrames = syllableNotes.Max(note => note.DurationFrames);
            double targetSeconds = longestNoteFrames / (double)settings.SampleRate;
            double naturalSeconds = syllable.NaturalDurationMilliseconds / (1000 * settings.Speed);
            double speechSpeed = Math.Clamp(settings.Speed * 0.8 * naturalSeconds / targetSeconds, 0.2, 4.0);
            string ssml = BuildSsml(syllableSpeechTokens, voice.Culture, speechSpeed, pitchLevel, cancellationToken);
            float[] mono = SynthesizeMono(synthesizer, ssml, settings, cancellationToken);
            mono = ApplyVoiceProfile(
                mono, nativePitch * PitchRatios[pitchLevel], settings, cancellationToken, applyGeneratedMelody: false);
            if (syllableSpeechTokens.OfType<VocalTextToken>().Any(token => token.Capitalized))
            {
                for (int i = 0; i < mono.Length; i++)
                {
                    if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    mono[i] *= 1.55f;
                }
            }

            foreach (VocalMidiScheduledNote note in syllableNotes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double frequency = VocalMidiSinging.GetFrequency(settings.SingingMidiFile!, note.NoteNumber);
                if (!double.IsFinite(frequency) || frequency <= 0 || frequency >= settings.SampleRate * 0.45)
                    throw new InvalidDataException($"MIDI note {note.NoteNumber} is outside the supported vocal frequency range.");
                float[] durationMatched = FitSpeechToFrames(mono, note.DurationFrames, cancellationToken);
                float[] pitched = VocalSinging.RenderWindowsAtPitch(durationMatched, settings, frequency, cancellationToken);
                MixMidiMono(output, pitched, note, channels, cancellationToken);
            }
        }

        AudioObj result = VocalSynth.CreateAudio(output, settings, cancellationToken);
        result.Bpm = (float)(settings.SingingMidiFile!.DefaultBpm * settings.Speed);
        return result;
    }

    private static IReadOnlyList<VocalToken> GetSyllableSpeechTokens(
        IReadOnlyList<VocalToken> speechTokens, VocalSyllableTextRange range)
    {
        List<VocalToken> result = [];
        int rangeEnd = checked(range.Start + range.Length);
        foreach (VocalToken token in speechTokens)
        {
            if (token is not VocalTextToken text || text.SourceStart < 0 || text.SourceLength <= 0) continue;
            int overlapStart = Math.Max(range.Start, text.SourceStart);
            int overlapEnd = Math.Min(rangeEnd, text.SourceStart + text.SourceLength);
            if (overlapEnd <= overlapStart) continue;

            int textOffset = overlapStart - text.SourceStart;
            int length = Math.Min(overlapEnd - overlapStart, text.Text.Length - textOffset);
            if (length <= 0) continue;
            result.Add(text with
            {
                Text = text.Text.Substring(textOffset, length),
                SourceStart = overlapStart,
                SourceLength = length,
            });
        }
        return result;
    }

    private static float[] SynthesizeMono(
        SpeechSynthesizer synthesizer, string ssml, VocalSynthSettings settings,
        CancellationToken cancellationToken)
    {
        using var stream = new BoundedAudioStream(2L * (VocalSynth.MaxOutputSamples / settings.Channels));
        synthesizer.SetOutputToAudioStream(stream,
            new SpeechAudioFormatInfo(settings.SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        var completion = new TaskCompletionSource<SpeakCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SpeakCompletedEventArgs> completed = (_, args) => completion.TrySetResult(args);
        synthesizer.SpeakCompleted += completed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            synthesizer.SpeakSsmlAsync(ssml);
            while (!completion.Task.Wait(50))
            {
                if (!cancellationToken.IsCancellationRequested) continue;
                synthesizer.SpeakAsyncCancelAll();
                cancellationToken.ThrowIfCancellationRequested();
            }
            cancellationToken.ThrowIfCancellationRequested();
            SpeakCompletedEventArgs result = completion.Task.GetAwaiter().GetResult();
            if (result.Error is not null)
                throw new InvalidOperationException($"Windows speech synthesis failed: {result.Error.Message}", result.Error);
            if (result.Cancelled)
                throw new InvalidOperationException("Windows speech synthesis was canceled unexpectedly.");
            synthesizer.SetOutputToNull();
            return ReadAudio(stream, cancellationToken);
        }
        finally
        {
            synthesizer.SpeakCompleted -= completed;
            synthesizer.SetOutputToNull();
        }
    }

    private static float[] FitSpeechToFrames(
        float[] source, int targetFrames, CancellationToken cancellationToken)
    {
        if (source.Length == targetFrames) return (float[])source.Clone();
        if (source.Length >= 256 && targetFrames >= 256)
        {
            int fftSize = 256;
            while (fftSize < 4096 && fftSize * 2 <= Math.Min(source.Length, targetFrames))
                fftSize *= 2;
            return PitchShifter.StretchPhaseVocoder(
                source, 1, targetFrames, fftSize, 0.875, cancellationToken: cancellationToken);
        }

        var result = new float[targetFrames];
        for (int i = 0; i < targetFrames; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            double position = targetFrames <= 1 || source.Length <= 1
                ? 0
                : i * (source.Length - 1.0) / (targetFrames - 1);
            int left = (int)position;
            int right = Math.Min(left + 1, source.Length - 1);
            result[i] = source[left] + (source[right] - source[left]) * (float)(position - left);
        }
        return result;
    }

    private static void MixMidiMono(
        float[] output, float[] voice, VocalMidiScheduledNote note, int channels,
        CancellationToken cancellationToken)
    {
        float gain = Math.Clamp(note.Velocity / 127f, 0.05f, 1f);
        for (int frame = 0; frame < voice.Length; frame++)
        {
            if ((frame & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            int outputFrame = note.StartFrame + frame;
            if ((uint)outputFrame >= (uint)(output.Length / channels))
                throw new InvalidOperationException("A scheduled MIDI voice exceeds the output timeline.");
            float sample = voice[frame] * gain;
            for (int channel = 0; channel < channels; channel++)
                output[outputFrame * channels + channel] += sample;
        }
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
        bool hasCapitalized = tokens.OfType<VocalTextToken>().Any(token => token.Capitalized);
        if (hasCapitalized) writer.WriteAttributeString("volume", "loud");
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
                WriteText(writer, text, culture, pitchLevel, hasCapitalized, cancellationToken);
            }
            else throw new InvalidOperationException("Unknown Windows speech token.");
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Flush();
        return output.ToString();
    }

    private static void WriteText(
        XmlWriter writer, VocalTextToken token, CultureInfo culture, int pitchLevel,
        bool hasCapitalized, CancellationToken cancellationToken)
    {
        string? volume = token.Emphasis switch
        {
            VocalEmphasis.Whisper => token.Capitalized ? "medium" : "soft",
            VocalEmphasis.Strong => hasCapitalized ? "x-loud" : "loud",
            _ when token.Capitalized => "x-loud",
            _ => null,
        };
        string? pitch = token.Question
            ? PitchNames[Math.Min(pitchLevel + 1, PitchNames.Length - 1)]
            : token.Emphasis switch
        {
            VocalEmphasis.PitchUp => PitchNames[Math.Min(pitchLevel + 1, PitchNames.Length - 1)],
            VocalEmphasis.PitchDown => PitchNames[Math.Max(pitchLevel - 1, 0)],
            _ => null,
        };
        bool lengthened = token.Emphasis == VocalEmphasis.Lengthened;
        if (volume is not null || pitch is not null || lengthened)
        {
            writer.WriteStartElement("prosody", SsmlNamespace);
            if (volume is not null) writer.WriteAttributeString("volume", volume);
            if (pitch is not null) writer.WriteAttributeString("pitch", pitch);
            if (lengthened) writer.WriteAttributeString("rate", "70%");
        }
        WritePronunciation(writer, token.Text, culture, cancellationToken);
        if (volume is not null || pitch is not null || lengthened) writer.WriteEndElement();
    }

    private static void WritePronunciation(XmlWriter writer, string text, CultureInfo culture, CancellationToken cancellationToken)
    {
        bool german = culture.TwoLetterISOLanguageName == "de";
        if (!german && text.Equals("intrigued", StringComparison.OrdinalIgnoreCase))
        {
            WritePhoneme(writer, text, "ih n t r iy g d");
            return;
        }
        int start = 0, index = 0;
        while (index < text.Length)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            char letter = char.ToLowerInvariant(text[index]);
            int end = index + 1;
            while (end < text.Length && char.ToLowerInvariant(text[end]) == letter)
            {
                if ((end & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                end++;
            }
            string? phone = SapiPhone(letter, german, end == text.Length);
            if (end - index >= 3 && phone is not null)
            {
                writer.WriteString(text[start..index]);
                var phones = new StringBuilder((end - index) * (phone.Length + 1));
                for (int i = index; i < end; i++)
                {
                    if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (i > index) phones.Append(' ');
                    phones.Append(phone);
                }
                WritePhoneme(writer, text[index..end], phones.ToString());
                start = end;
            }
            index = end;
        }
        writer.WriteString(text[start..]);
    }

    private static void WritePhoneme(XmlWriter writer, string text, string phones)
    {
        writer.WriteStartElement("phoneme", SsmlNamespace);
        writer.WriteAttributeString("alphabet", "sapi");
        writer.WriteAttributeString("ph", phones);
        writer.WriteString(text);
        writer.WriteEndElement();
    }

    private static string? SapiPhone(char letter, bool german, bool atWordEnd) => letter switch
    {
        'a' => german ? "a" : "ae",
        'e' => german ? "ey" : "iy",
        'i' => german ? "iy" : "ih",
        'o' => german || atWordEnd ? "ow" : "uw",
        'u' => german ? "uw" : "ah",
        '\u00e4' when german => "eh",
        '\u00f6' when german => "eu",
        '\u00fc' when german => "ue",
        'f' or 'l' or 'm' or 'n' or 'r' or 's' => letter.ToString(),
        'v' => german ? "f" : "v",
        'w' => german ? "v" : "w",
        'z' when !german => "z",
        _ => null,
    };

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
        float[] source, double nativePitch, VocalSynthSettings settings, CancellationToken cancellationToken,
        bool applyGeneratedMelody = true)
    {
        if (settings.Singing && applyGeneratedMelody)
            return ColorVoice(VocalSinging.RenderWindows(source, settings, cancellationToken), settings, cancellationToken);
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
