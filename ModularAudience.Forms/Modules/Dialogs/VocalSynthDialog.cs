using ModularAudience.Audio;
using ModularAudience.Audio.Vocal;
using ModularAudience.Forms.Modules;
using NAudio.Wave;
using System.Globalization;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalSynthDialog : Form
{
    private readonly VocalEmphasisSyntaxStore emphasisStore;
    private VocalEmphasisSyntax emphasisSyntax;
    private readonly VocalVoiceProfileStore profileStore;
    private readonly ComboBox languageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox voiceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown speed = new() { Minimum = 0.5m, Maximum = 2.0m, Increment = 0.05m, DecimalPlaces = 2, Value = 1.0m, Width = 80 };
    private readonly TextHistoryTextBox textEditor = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 11f) };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Ready." };
    private readonly Button prehear = new() { Text = "Prehear", AutoSize = true };
    private readonly Button create = new() { Text = "Create", AutoSize = true };
    private readonly Button designProfileBtn = new() { Text = "Design Profile...", AutoSize = true };
    private readonly Button emphasisBtn = new() { Text = "Emphasis Syntax...", AutoSize = true };

    private VocalSynthSettings settings;
    private AudioObj? audio;
    private AudioCollectionView? collection;
    private CancellationTokenSource? rendering;
    private int revision, ready = -1;
    private bool disposedFlag, creating, syncing;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private WaveOut? output;
    private VocalPreview? provider;
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 40 };

    private sealed class VocalPreview : ISampleProvider
    {
        private readonly float[] data;
        private int pos;
        public WaveFormat WaveFormat { get; }
        public bool Loop { get; set; }
        public VocalPreview(float[] data, int sampleRate, int channels)
        {
            this.data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }
        public int Read(Span<float> buffer)
        {
            if (pos >= data.Length)
            {
                if (Loop) { pos = 0; Finished = false; }
                else { Finished = true; return 0; }
            }
            int n = Math.Min(buffer.Length, data.Length - pos);
            data.AsSpan(pos, n).CopyTo(buffer);
            pos += n;
            if (pos >= data.Length && !Loop) Finished = true;
            return n;
        }
        public bool Finished { get; private set; }
    }

    public VocalSynthDialog(VocalVoiceProfileStore? store = null, VocalEmphasisSyntaxStore? estore = null)
    {
        profileStore = store ?? VocalVoiceProfileStore.Default();
        emphasisStore = estore ?? VocalEmphasisSyntaxStore.Default();
        emphasisSyntax = emphasisStore.Load();

        settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.English, "");
        InitializeSpeechBackendControl();
        InitializeProfileControls();
        UpdateSpeechBackend();
        Text = "VocalSynth"; Name = "VocalSynthDialog";
        ClientSize = new Size(900, 650); MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterScreen;

        // language combo
        foreach (var lang in Enum.GetValues<VocalLanguage>()) languageCombo.Items.Add(lang.DisplayName());
        languageCombo.SelectedIndex = 0;
        languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (syncing) return;
            settings.Language = (VocalLanguage)languageCombo.SelectedIndex;
            UpdateSpeechBackend();
            QueueRender();
        };

        // voice combo: built-ins + profiles
        RefreshVoiceCombo();
        voiceCombo.SelectedIndexChanged += (_, _) => ApplyVoiceSelection();

        speed.ValueChanged += (_, _) => { if (!syncing) { settings.Speed = (double)speed.Value; QueueRender(); } };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        void Field(string label, Control c) { top.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 8, 2, 0) }); top.Controls.Add(c); }
        Field("Language", languageCombo);
        Field("Voice", voiceCombo);
        Field("Speed", speed);
        top.Controls.Add(windowsSpeechCheckBox);
        top.Controls.Add(prehear);
        top.SetFlowBreak(prehear, true);
        top.Controls.Add(profileRow);
        top.Controls.Add(create); top.Controls.Add(designProfileBtn); top.Controls.Add(emphasisBtn);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        bottom.Controls.Add(status);

        Controls.Add(textEditor);
        Controls.Add(top);
        Controls.Add(bottom);

        // events
        textEditor.TextChanged += (_, _) =>
        {
            if (syncing) return;
            settings.Text = textEditor.Text;
            UpdateButtonState();
            if (string.IsNullOrWhiteSpace(textEditor.Text))
            {
                revision++;
                rendering?.Cancel();
                debounce.Stop();
                audio?.Dispose();
                audio = null;
                ready = -1;
                status.Text = "Enter text to synthesize.";
                return;
            }
            QueueRender();
        };
        prehear.Click += async (_, _) => await ToggleHearAsync();
        create.Click += async (_, _) => await CreateAsync();
        emphasisBtn.Click += (_, _) => OpenEmphasisDialog();
        designProfileBtn.Click += (_, _) => OpenDesignerDialog();

        debounce.Tick += async (_, _) => { debounce.Stop(); await RenderAsync(); };
        playbackTimer.Tick += (_, _) => { if (provider?.Finished == true) StopHear(); };
        FormClosing += (_, _) => { rendering?.Cancel(); StopHear(); };

        settings.Text = textEditor.Text;
        UpdateButtonState();
        if (!string.IsNullOrWhiteSpace(textEditor.Text)) QueueRender();
    }

    private void UpdateButtonState()
    {
        bool hasText = !string.IsNullOrWhiteSpace(textEditor.Text);
        prehear.Enabled = hasText || output is not null;
        create.Enabled = hasText;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (textEditor.ProcessHistoryCommand(keyData)) return true;
        if (keyData == (Keys.Shift | Keys.Enter) || keyData == Keys.Enter && ActiveControl != textEditor)
        {
            prehear.PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateSpeechBackend()
    {
        bool supported = settings.Language is VocalLanguage.English or VocalLanguage.German;
        windowsSpeechCheckBox.Enabled = supported;
        settings.UseWindowsSpeech = supported && windowsSpeechCheckBox.Checked;
    }

    private void WindowsSpeechCheckedChanged(object? sender, EventArgs e)
    {
        if (syncing) return;
        UpdateSpeechBackend();
        QueueRender();
    }

    private void RefreshVoiceCombo()
    {
        string selected = voiceCombo.SelectedItem?.ToString() ?? "Built-in: " + settings.VoiceName;
        voiceCombo.Items.Clear();
        foreach (var p in VocalVoicePreset.BuiltIn)
            voiceCombo.Items.Add("Built-in: " + p.Name);
        foreach (var p in profileStore.LoadAll())
            voiceCombo.Items.Add("Profile: " + p.Name);
        if (voiceCombo.Items.Count > 0)
        {
            int index = voiceCombo.Items.IndexOf(selected);
            voiceCombo.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private void ApplyVoiceSelection()
    {
        if (syncing || voiceCombo.SelectedIndex < 0) return;
        var item = voiceCombo.SelectedItem?.ToString() ?? "";
        syncing = true;
        try
        {
            if (item.StartsWith("Built-in: "))
            {
                var name = item.Substring("Built-in: ".Length);
                var preset = VocalVoicePreset.BuiltIn.First(p => p.Name == name);
                var s = VocalSynthSettings.FromPreset(preset, settings.Language, settings.Text);
                s.UseWindowsSpeech = settings.UseWindowsSpeech;
                s.Speed = settings.Speed;
                s.EmphasisSyntax = settings.EmphasisSyntax;
                settings = s;
            }
            else if (item.StartsWith("Profile: "))
            {
                var name = item.Substring("Profile: ".Length);
                var profile = profileStore.LoadAll().FirstOrDefault(p => p.Name == name);
                if (profile != null)
                {
                    var s = VocalSynthSettings.FromProfile(profile, settings.Language, settings.Text);
                    s.UseWindowsSpeech = settings.UseWindowsSpeech;
                    s.Speed = settings.Speed;
                    s.EmphasisSyntax = settings.EmphasisSyntax;
                    settings = s;
                }
            }
            speed.Value = (decimal)settings.Speed;
        }
        finally { syncing = false; }
        QueueRender();
    }

    public void SetCollection(AudioCollectionView? collection) => this.collection = collection;

    private void QueueRender()
    {
        if (disposedFlag) return;
        revision++;
        rendering?.Cancel();
        debounce.Stop();
        if (string.IsNullOrWhiteSpace(textEditor.Text))
        {
            ready = -1;
            status.Text = "Enter text to synthesize.";
            return;
        }
        debounce.Start();
        status.Text = "Updating…";
    }

    private async Task RenderAsync()
    {
        if (disposedFlag) return;
        var cts = new CancellationTokenSource();
        rendering?.Cancel();
        rendering = cts;
        int version = revision;
        try
        {
            settings.Text = textEditor.Text;
            settings.Speed = (double)speed.Value;
            var snapshot = settings.Clone();
            var next = await Task.Run(() => VocalSynth.Render(snapshot, cts.Token), cts.Token);
            if (disposedFlag || version != revision || cts.IsCancellationRequested) { next.Dispose(); return; }
            var old = audio; audio = next; ready = version;
            old?.Dispose();
            status.Text = $"Rendered {next.Duration.TotalSeconds:0.###} s · {next.SampleRate} Hz";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposedFlag && version == revision) status.Text = error.Message; }
        finally
        {
            if (ReferenceEquals(rendering, cts)) rendering = null;
            cts.Dispose();
        }
    }

    private async Task<bool> EnsureRenderedAsync()
    {
        if (ready != revision)
        {
            debounce.Stop();
            await RenderAsync();
        }
        return !disposedFlag && ready == revision && audio is not null;
    }

    private async Task ToggleHearAsync()
    {
        if (output != null) { StopHear(); return; }
        try
        {
            if (!await EnsureRenderedAsync() || audio == null) return;
            provider = new VocalPreview(audio.Data, audio.SampleRate, audio.Channels);
            output = new WaveOut { BufferMilliseconds = 60 };
            output.Init(provider);
            output.Play();
            prehear.Text = "Stop";
            playbackTimer.Start();
        }
        catch (Exception error) { StopHear(); status.Text = error.Message; }
    }

    private void StopHear()
    {
        playbackTimer.Stop();
        output?.Stop();
        output?.Dispose();
        output = null;
        provider = null;
        prehear.Text = "Prehear";
        prehear.Enabled = !string.IsNullOrWhiteSpace(textEditor.Text);
    }

    private async Task CreateAsync()
    {
        if (creating) return;
        creating = true;
        create.Enabled = false;
        try
        {
            if (!await EnsureRenderedAsync() || audio == null) return;
            if (collection == null || collection.IsDisposed) collection = new AudioCollectionView([], "VocalSynth");
            var created = audio.Clone();
            string basis = audio.Name, name = basis; int index = 2;
            while (collection.AudioC.Audios.Any(a => a.Name == name)) name = basis + " " + index++;
            created.Rename(name);
            collection.AudioC.Audios.Add(created);
            collection.Show();
            status.Text = "Created: " + name;
        }
        catch (Exception error) { if (!disposedFlag) status.Text = error.Message; }
        finally { creating = false; if (!disposedFlag) UpdateButtonState(); }
    }

    private void OpenEmphasisDialog()
    {
        using var dlg = new VocalEmphasisSyntaxDialog(emphasisSyntax);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            emphasisSyntax = dlg.Syntax;
            emphasisStore.Save(emphasisSyntax);
            settings.EmphasisSyntax = emphasisSyntax;
            QueueRender();
        }
    }

    private void OpenDesignerDialog()
    {
        using var dlg = new VocalVoiceProfileDesignerDialog(null, profileStore);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultProfile is not null)
        {
            RefreshVoiceCombo();
            profileName.Text = dlg.ResultProfile.Name;
            status.Text = "Profile designed: " + dlg.ResultProfile.Name;
        }
    }

    private void SaveProfile()
    {
        var name = profileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { status.Text = "Enter a profile name."; return; }
        var profile = new VocalVoiceProfile
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
        };
        profileStore.Save(profile);
        RefreshVoiceCombo();
        status.Text = "Profile saved: " + name;
    }

    private void LoadProfile()
    {
        // select in combo if exists
        var name = profileName.Text.Trim();
        var idx = -1;
        for (int i = 0; i < voiceCombo.Items.Count; i++)
            if (voiceCombo.Items[i].ToString() == "Profile: " + name) { idx = i; break; }
        if (idx >= 0) { voiceCombo.SelectedIndex = idx; status.Text = "Profile loaded: " + name; }
        else status.Text = "Profile not found.";
    }

    private void DeleteProfile()
    {
        var name = profileName.Text.Trim();
        if (profileStore.Delete(name)) { RefreshVoiceCombo(); status.Text = "Profile deleted: " + name; }
        else status.Text = "Profile not found.";
    }

    private void RenameProfile()
    {
        var oldName = profileName.Text.Trim();
        // ask new name via simple input
        using var dlg = new Form { Text = "Rename Profile", ClientSize = new Size(320, 100), StartPosition = FormStartPosition.CenterParent };
        var tb = new TextBox { Left = 12, Top = 12, Width = 290 };
        var ok = new Button { Text = "OK", Left = 12, Top = 45, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 100, Top = 45, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var newName = tb.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (profileStore.Rename(oldName, newName)) { RefreshVoiceCombo(); profileName.Text = newName; status.Text = "Profile renamed."; }
            else status.Text = "Rename failed.";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            disposedFlag = true;
            rendering?.Cancel();
            StopHear();
            debounce.Dispose();
            playbackTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}