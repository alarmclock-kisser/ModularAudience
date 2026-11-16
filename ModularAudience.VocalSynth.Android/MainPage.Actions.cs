using System.Diagnostics;
using System.Text.Json;
using ModularAudience.Audio.Vocal;
using ModularAudience.VocalSynth.Android.Services;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android;

public sealed partial class MainPage
{
    private List<string> voiceOptions = [];

    private void RefreshVoicePicker(string? preferredVoice)
    {
        voiceOptions = VocalVoicePreset.BuiltIn.Select(preset => "Built-in: " + preset.Name)
            .Concat(profileStore.LoadAll().Select(profile => "Profile: " + profile.Name))
            .ToList();
        syncing = true;
        try
        {
            voicePicker.ItemsSource = voiceOptions;
            int selected = preferredVoice is null ? -1 : voiceOptions.IndexOf(preferredVoice);
            voicePicker.SelectedIndex = selected >= 0 ? selected : 2;
        }
        finally
        {
            syncing = false;
        }
        ApplyVoiceSelection();
    }

    private void ApplyVoiceSelection()
    {
        if (syncing || voicePicker.SelectedIndex < 0 || voicePicker.SelectedIndex >= voiceOptions.Count) return;
        string? text = textEditor.Text;
        VocalLanguage language = languagePicker.SelectedIndex >= 0
            ? (VocalLanguage)languagePicker.SelectedIndex
            : settings.Language;
        VocalSynthSettings replacement;
        string selected = voiceOptions[voicePicker.SelectedIndex];
        if (selected.StartsWith("Built-in: ", StringComparison.Ordinal))
        {
            string name = selected["Built-in: ".Length..];
            VocalVoicePreset preset = VocalVoicePreset.BuiltIn.First(item => item.Name == name);
            replacement = VocalSynthSettings.FromPreset(preset, language, text ?? string.Empty);
        }
        else
        {
            string name = selected["Profile: ".Length..];
            VocalVoiceProfile profile = profileStore.LoadAll()
                .First(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            replacement = VocalSynthSettings.FromProfile(profile, language, text ?? string.Empty);
        }

        replacement.Text = text ?? string.Empty;
        replacement.Speed = speedSlider.Value;
        replacement.Singing = singingSwitch.IsToggled || replacement.Singing;
        replacement.BeepForUnsupportedSounds = unsupportedBeepSwitch.IsToggled;
        replacement.SingingMidiFile = midi;
        replacement.SingingMidiTrackIndex = settings.SingingMidiTrackIndex;
        replacement.SingingMidiSemitoneOffset = midiOffsetSlider.Value;
        replacement.CustomSampleMappings = customSamples.ToArray();
        replacement.EmphasisSyntax = LoadEmphasisSyntaxFromEditor();
        replacement.UseWindowsSpeech = false;
        settings = replacement;
        UpdateVoiceSliders();
    }

    private void UpdateVoiceSliders()
    {
        syncing = true;
        try
        {
            voiceSliders["BasePitchHz"].Value = Math.Clamp(settings.BasePitchHz, 60, 400);
            voiceSliders["FormantScale"].Value = Math.Clamp(settings.FormantScale, 0.5, 2);
            voiceSliders["Breathiness"].Value = settings.Breathiness;
            voiceSliders["Roughness"].Value = settings.Roughness;
            voiceSliders["VibratoCents"].Value = settings.VibratoCents;
            voiceSliders["VibratoHz"].Value = settings.VibratoHz;
            voiceSliders["Voicing"].Value = settings.Voicing;
            voiceSliders["Noise"].Value = settings.Noise;
            singingSwitch.IsToggled = settings.Singing;
        }
        finally
        {
            syncing = false;
        }
    }

    private void ApplyControlsToSettings()
    {
        settings.Language = languagePicker.SelectedIndex >= 0
            ? (VocalLanguage)languagePicker.SelectedIndex
            : VocalLanguage.English;
        settings.Text = textEditor.Text ?? string.Empty;
        settings.Speed = speedSlider.Value;
        settings.BasePitchHz = voiceSliders["BasePitchHz"].Value;
        settings.FormantScale = voiceSliders["FormantScale"].Value;
        settings.Breathiness = voiceSliders["Breathiness"].Value;
        settings.Roughness = voiceSliders["Roughness"].Value;
        settings.VibratoCents = voiceSliders["VibratoCents"].Value;
        settings.VibratoHz = voiceSliders["VibratoHz"].Value;
        settings.Voicing = voiceSliders["Voicing"].Value;
        settings.Noise = voiceSliders["Noise"].Value;
        settings.Singing = singingSwitch.IsToggled;
        settings.UseWindowsSpeech = false;
        settings.BeepForUnsupportedSounds = unsupportedBeepSwitch.IsToggled;
        settings.SingingMidiFile = midi;
        settings.SingingMidiSemitoneOffset = midiOffsetSlider.Value;
        settings.CustomSampleMappings = customSamples.ToArray();
        settings.EmphasisSyntax = LoadEmphasisSyntaxFromEditor();
        ApplySelectedMidiTrack();
    }

    private VocalEmphasisSyntax LoadEmphasisSyntaxFromEditor()
    {
        string json = emphasisEditor.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(json)) return VocalEmphasisSyntax.Default();
        return JsonSerializer.Deserialize<VocalEmphasisSyntax>(json)
            ?? throw new InvalidDataException("The emphasis syntax JSON is empty.");
    }

    private void ApplySelectedMidiTrack()
    {
        if (midi is null || midiTrackPicker.SelectedIndex < 0
            || midiTrackPicker.SelectedIndex >= midi.Tracks.Count)
        {
            settings.SingingMidiTrackIndex = null;
            UpdateMidiTrackInfo();
            return;
        }
        settings.SingingMidiTrackIndex = midi.Tracks[midiTrackPicker.SelectedIndex].Index;
        UpdateMidiTrackInfo();
    }

    private void UpdateMidiTrackInfo()
    {
        if (midi is null || midiTrackPicker.SelectedIndex < 0
            || midiTrackPicker.SelectedIndex >= midi.Tracks.Count)
        {
            midiTrackInfoLabel.Text = "Load a MIDI file to see track details.";
            return;
        }

        IVocalMidiTrack track = midi.Tracks[midiTrackPicker.SelectedIndex];
        string name = string.IsNullOrWhiteSpace(track.Name)
            ? $"Track {midiTrackPicker.SelectedIndex + 1}"
            : track.Name;
        long lengthTicks = Math.Max(track.LengthTicks,
            track.Notes.Count == 0 ? 0 : track.Notes.Max(note => note.StartTick + note.DurationTicks));
        double beats = lengthTicks / (double)midi.TicksPerQuarterNote;
        string pitchRange = track.Notes.Count == 0
            ? "no notes"
            : $"{NoteName(track.Notes.Min(note => note.NoteNumber))}-{NoteName(track.Notes.Max(note => note.NoteNumber))}";
        midiTrackInfoLabel.Text =
            $"{name} | {track.Notes.Count} notes | {beats:0.##} beats | {pitchRange}";
    }

    private static string NoteName(int noteNumber)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int note = Math.Clamp(noteNumber, 0, 127);
        return names[note % 12] + (note / 12 - 1);
    }

    private async Task LoadMidiAsync()
    {
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync();
            if (file is null) return;
            string extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".mid", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select a .mid or .midi file.");

            string cachePath = Path.Combine(FileSystem.CacheDirectory, Guid.NewGuid() + extension);
            await using (Stream input = await file.OpenReadAsync())
            await using (FileStream output = File.Create(cachePath))
                await input.CopyToAsync(output);

            VocalMidiFileData loaded = await Task.Run(() => VocalMidiFileData.Load(cachePath));
            midi = loaded;
            settings.SingingMidiFile = loaded;
            int preferredTrack = Preferences.Default.Get("vocal.midiTrackIndex", -1);
            List<string> tracks = loaded.Tracks.Select((track, index) =>
            {
                string name = string.IsNullOrWhiteSpace(track.Name) ? $"Track {index + 1}" : track.Name;
                return $"{index + 1}. {name} ({track.Notes.Count} notes)";
            }).ToList();
            midiTrackPicker.ItemsSource = tracks;
            midiTrackPicker.SelectedIndex = Enumerable.Range(0, loaded.Tracks.Count)
                .FirstOrDefault(index => loaded.Tracks[index].Index == preferredTrack, -1);
            if (midiTrackPicker.SelectedIndex < 0)
                midiTrackPicker.SelectedIndex = Enumerable.Range(0, loaded.Tracks.Count)
                    .FirstOrDefault(index => loaded.Tracks[index].Notes.Count > 0, -1);
            midiTrackPicker.IsEnabled = tracks.Count > 0;
            clearMidiButton.IsEnabled = true;
            singingSwitch.IsToggled = true;
            midiFileLabel.Text =
                $"{file.FileName} | {loaded.DefaultBpm:0.0} BPM | {loaded.TimeSignatureNumerator}/{loaded.TimeSignatureDenominator}";
            ApplySelectedMidiTrack();
            statusLabel.Text = "MIDI loaded for singing.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidDataException or FormatException)
        {
            await ShowErrorAsync("MIDI load failed", error);
        }
    }

    private void ClearMidi()
    {
        midi = null;
        settings.SingingMidiFile = null;
        settings.SingingMidiTrackIndex = null;
        midiTrackPicker.ItemsSource = null;
        midiTrackPicker.SelectedIndex = -1;
        midiTrackPicker.IsEnabled = false;
        clearMidiButton.IsEnabled = false;
        midiFileLabel.Text = "No MIDI loaded.";
        midiTrackInfoLabel.Text = "Load a MIDI file to see track details.";
        syllableLabel.Text = "Syllable preview is off.";
        PersistSession();
        statusLabel.Text = "MIDI cleared.";
    }

    private async Task UpdateSyllablesAsync()
    {
        if (!showSyllablesSwitch.IsToggled)
        {
            syllableLabel.Text = "Syllable preview is off.";
            return;
        }
        try
        {
            ApplyControlsToSettings();
            if (string.IsNullOrWhiteSpace(settings.Text))
            {
                syllableLabel.Text = "Enter text to preview syllables.";
                return;
            }
            VocalSynthSettings snapshot = settings.Clone();
            IReadOnlyList<VocalSyllableTextRange> ranges = await Task.Run(
                () => VocalSynthEngine.GetSyllableTextRanges(snapshot));
            string preview = string.Join(" | ", ranges.Select((range, index) =>
            {
                if (range.Start < 0 || range.Length <= 0 || range.Start > snapshot.Text.Length - range.Length)
                    return $"#{index + 1}";
                return snapshot.Text.Substring(range.Start, range.Length);
            }));
            syllableLabel.Text = $"Syllables ({ranges.Count}): {preview}";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or InvalidDataException or FormatException)
        {
            await ShowErrorAsync("Syllable preview failed", error);
        }
    }

    private async void FillMissingSyllables()
    {
        try
        {
            ApplyControlsToSettings();
            VocalMidiSyllableFillResult result = VocalSynthEngine.FillMissingMidiSyllables(
                settings, settings.Text);
            if (result.AppendedText.Length > 0)
                textEditor.Text = (textEditor.Text ?? string.Empty) + result.AppendedText;
            settings.Text = textEditor.Text ?? string.Empty;
            statusLabel.Text = result.AddedSyllables > 0
                ? $"Added {result.AddedSyllables} syllables to match the MIDI track."
                : "The text already matches the selected MIDI track.";
            if (showSyllablesSwitch.IsToggled) await UpdateSyllablesAsync();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or InvalidDataException)
        {
            await ShowErrorAsync("Could not fill syllables", error);
        }
    }

    private void SaveProfile()
    {
        try
        {
            ApplyControlsToSettings();
            string name = profileName.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Enter a profile name before saving.");
            VocalVoiceProfile profile = new()
            {
                Name = name,
                BasePitchHz = settings.BasePitchHz,
                FormantScale = settings.FormantScale,
                Breathiness = settings.Breathiness,
                Roughness = settings.Roughness,
                VibratoCents = settings.VibratoCents,
                VibratoHz = settings.VibratoHz,
                Voicing = settings.Voicing,
                Noise = settings.Noise,
                Singing = settings.Singing,
                BasePresetId = VocalVoicePreset.BuiltIn
                    .FirstOrDefault(preset => preset.Name == settings.VoiceName)?.Id,
            };
            profileStore.Save(profile);
            RefreshVoicePicker("Profile: " + profile.Name);
            PersistSession();
            statusLabel.Text = $"Saved profile '{profile.Name}'.";
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _ = ShowErrorAsync("Profile save failed", error);
        }
    }

    private async void DeleteSelectedProfile()
    {
        try
        {
            if (voicePicker.SelectedIndex < 0
                || !voiceOptions[voicePicker.SelectedIndex].StartsWith("Profile: ", StringComparison.Ordinal))
                throw new InvalidOperationException("Select a saved profile to delete.");
            string name = voiceOptions[voicePicker.SelectedIndex]["Profile: ".Length..];
            bool confirm = await DisplayAlert("Delete profile", $"Delete '{name}'?", "Delete", "Cancel");
            if (!confirm) return;
            if (!profileStore.Delete(name))
                throw new FileNotFoundException("The selected voice profile no longer exists.");
            RefreshVoicePicker("Built-in: " + settings.VoiceName);
            PersistSession();
            statusLabel.Text = $"Deleted profile '{name}'.";
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Profile delete failed", error);
        }
    }

    private async void SaveEmphasisSyntax()
    {
        try
        {
            VocalEmphasisSyntax syntax = LoadEmphasisSyntaxFromEditor();
            emphasisStore.Save(syntax);
            settings.EmphasisSyntax = syntax;
            statusLabel.Text = "Emphasis syntax saved.";
        }
        catch (Exception error) when (error is JsonException or InvalidDataException
            or IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Emphasis syntax save failed", error);
        }
    }

    private async Task AddCustomSampleAsync()
    {
        try
        {
            string ipa = customSampleIpa.Text?.Trim() ?? string.Empty;
            if (!VocalSynthEngine.IsValidCustomSampleIpa(ipa))
                throw new ArgumentException("Enter a valid IPA sequence before adding a sample.");
            FileResult? file = await FilePicker.Default.PickAsync();
            if (file is null) return;
            await using Stream input = await file.OpenReadAsync();
            WaveAudioData wave = WaveAudioReader.Read(input);
            if (wave.Samples.Length / wave.Channels > wave.SampleRate * 60L)
                throw new InvalidDataException("Custom sample audio must be shorter than one minute.");
            VocalCustomSampleMapping mapping = new(ipa, wave.Samples, wave.SampleRate, wave.Channels);
            int existing = customSamples.FindIndex(
                item => string.Equals(item.IpaSubstring, ipa, StringComparison.Ordinal));
            if (existing >= 0) customSamples[existing] = mapping;
            else customSamples.Add(mapping);
            settings.CustomSampleMappings = customSamples.ToArray();
            customSampleFileLabel.Text = $"{file.FileName} mapped to {ipa}";
            statusLabel.Text = "Custom sample mapping added.";
        }
        catch (Exception error) when (error is ArgumentException or IOException
            or InvalidDataException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Custom sample import failed", error);
        }
    }

    private async Task PreviewAsync()
    {
        if (androidSpeechSwitch.IsToggled)
        {
            try
            {
                string text = textEditor.Text?.Trim() ?? string.Empty;
                if (text.Length == 0) throw new ArgumentException("Enter text before previewing.");
                StopPlayback();
                await TextToSpeech.Default.SpeakAsync(text);
                statusLabel.Text = "Android speech finished.";
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException
                or FeatureNotSupportedException)
            {
                await ShowErrorAsync("Android speech failed", error);
            }
            return;
        }

        VocalAudioData? audio = await RenderAsync();
        if (audio is null) return;
        try
        {
            stopButton.IsEnabled = stopButton.IsVisible = true;
            await player.PlayAsync(audio, renderCancellation?.Token ?? CancellationToken.None);
            statusLabel.Text = "Preview finished.";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Playback stopped.";
        }
        catch (Exception error)
        {
            await ShowErrorAsync("Playback failed", error);
        }
        finally
        {
            stopButton.IsEnabled = stopButton.IsVisible = false;
        }
    }

    private async Task CreateAndShareAsync()
    {
        VocalAudioData? audio = await RenderAsync();
        if (audio is null) return;
        try
        {
            string baseName = SafeFileName(audio.Name);
            if (baseName.Length > 48) baseName = baseName[..48];
            string fileName = $"{baseName}-{Guid.NewGuid():N}.wav";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            int trailingSilenceMilliseconds = (int)wavTrailingSilenceSlider.Value;
            await WaveFileWriter.WritePcm16Async(
                path, audio, trailingSilenceMilliseconds, CancellationToken.None);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share VocalSynth audio",
                File = new ShareFile(path, "audio/wav"),
            });
            double exportedDuration = audio.Duration.TotalSeconds + trailingSilenceMilliseconds / 1000d;
            statusLabel.Text =
                $"Created {fileName} ({exportedDuration:0.00}s, including {trailingSilenceMilliseconds} ms trailing silence).";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await ShowErrorAsync("WAV export failed", error);
        }
    }

    private async Task<VocalAudioData?> RenderAsync()
    {
        try
        {
            ApplyControlsToSettings();
            settings.Validate();
            StopPlayback();
            CancellationTokenSource cancellation = new();
            renderCancellation = cancellation;
            busyIndicator.IsVisible = busyIndicator.IsRunning = true;
            renderButton.IsEnabled = createButton.IsEnabled = false;
            statusLabel.Text = "Synthesizing...";
            VocalSynthSettings snapshot = settings.Clone();
            VocalAudioData audio = await Task.Run(
                () => VocalSynthEngine.Render(snapshot, cancellation.Token), cancellation.Token);
            statusLabel.Text = $"Ready: {audio.Duration.TotalSeconds:0.00}s | {audio.Name}";
            return audio;
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Synthesis cancelled.";
            return null;
        }
        catch (Exception error)
        {
            await ShowErrorAsync("Synthesis failed", error);
            return null;
        }
        finally
        {
            renderCancellation?.Dispose();
            renderCancellation = null;
            busyIndicator.IsVisible = busyIndicator.IsRunning = false;
            renderButton.IsEnabled = createButton.IsEnabled = true;
        }
    }

    private void StopPlayback()
    {
        renderCancellation?.Cancel();
        player.Stop();
        stopButton.IsEnabled = stopButton.IsVisible = false;
    }

    private static string SafeFileName(string name)
    {
        string safe = string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "VocalSynth" : safe;
    }

    private async Task ShowErrorAsync(string title, Exception error)
    {
        Debug.WriteLine(error);
        statusLabel.Text = $"{title}: {error.Message}";
        await DisplayAlert(title, error.Message, "OK");
    }
}
