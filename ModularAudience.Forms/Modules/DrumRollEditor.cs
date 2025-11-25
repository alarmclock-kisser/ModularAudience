using ModularAudience.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.ComponentModel;

namespace ModularAudience.Forms.Modules
{
    public partial class DrumRollEditor : Form
    {
        private readonly AudioCollection AudioC = new();
        private AudioCollectionView? CollectionView = null;

        public float Bpm => (float) this.numericUpDown_bpm.Value;
        public int Hits => this.domainUpDown_hits.SelectedItem is null ? 16 : int.Parse(this.domainUpDown_hits.SelectedItem.ToString() ?? "16");
        public float Volume => (float) this.numericUpDown_volume.Value / 100.0f;

        internal readonly BindingList<Panel> Panels = [];

        private WaveOutEvent? waveOut;
        private MixingSampleProvider? mixer;
        private readonly WaveFormat outputFormat;
        private CancellationTokenSource? playbackCts;
        private Task? playbackTask;

        private int currentStep = 0;
        private bool isPlaying = false;


        public DrumRollEditor(IEnumerable<AudioObj>? samples = null)
        {
            this.InitializeComponent();
            this.KeyPreview = true;
            this.panel_pattern.Visible = false;
            this.button_hit.Visible = false;
            this.domainUpDown_hits.SelectedIndex = this.domainUpDown_hits.Items.IndexOf("16");

            this.outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            this.waveOut = null;
            this.mixer = null;

            if (samples != null)
            {
                foreach (AudioObj sample in samples)
                {
                    this.AudioC.Audios.Add((AudioObj) sample.Clone());
                }
            }

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this);

            this.AllowDrop = true;
            this.DragEnter += this.DrumRollEditor_DragEnter;
            this.DragDrop += this.DrumRollEditor_DragDrop;
            this.FormClosing += this.Form_Closing;

            this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            this.domainUpDown_hits.SelectedItemChanged += this.domainUpDown_hits_SelectedItemChanged;

            // Set min/max width
            this.MinimumSize = new Size(720, this.MinimumSize.Height);
            this.MaximumSize = new Size(1280, this.MaximumSize.Height);
            this.Resize += this.DrumRollEditor_Resize;

            // Initial Panels bauen, falls Samples vorhanden
            _ = this.RebuildPatternPanelsAsync();
        }

        private void Form_Closing(object? sender, FormClosingEventArgs e)
        {
            // Events entfernen
            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            this.domainUpDown_hits.SelectedItemChanged -= this.domainUpDown_hits_SelectedItemChanged;
            this.DragEnter -= this.DrumRollEditor_DragEnter;
            this.DragDrop -= this.DrumRollEditor_DragDrop;
            this.AudioC.Dispose();
		}

        private void button_playback_Click(object sender, EventArgs e)
        {
            if (this.isPlaying)
            {
                this.StopPlayback();
            }
            else
            {
                this.StartPlayback();
            }
        }

        private int GetTimerIntervalMs()
        {
            float bpm = this.Bpm;
            int hits = this.Hits;

            // Dauer eines 4/4 Taktes in ms: (60 / BPM) * 4 * 1000
            // Dauer eines Steps: (Takt-Dauer) / Hits
            if (bpm <= 0 || hits <= 0)
            {
                return 100; // Fallback
            }

            return (int) (60000.0f / bpm * 4.0f / hits);
        }

        // Hilfsmethode zum Zurücksetzen der Highlights:
        private async void AudioC_Audios_ListChanged(object? sender, ListChangedEventArgs e)
        {
            await this.RebuildPatternPanelsAsync();
        }

        private async void domainUpDown_hits_SelectedItemChanged(object? sender, EventArgs e)
        {
            await this.RebuildPatternPanelsAsync();
            await this.ResizePanelsAndButtonsAsync();
            if (this.isPlaying)
            {
                this.currentStep = 0;
            }
        }

        private void DrumRollEditor_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null)
            {
                if (e.Data.GetDataPresent(typeof(AudioObj)) ||
                    e.Data.GetDataPresent(typeof(List<AudioObj>)) ||
                    e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)) ||
                    e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
                if (e.Data.GetDataPresent(DataFormats.Serializable))
                {
                    var data = e.Data.GetData(DataFormats.Serializable);
                    if (data is AudioObj || data is IEnumerable<AudioObj>)
                    {
                        e.Effect = DragDropEffects.Copy;
                        return;
                    }
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void DrumRollEditor_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            // Einzelnes AudioObj
            if (e.Data.GetDataPresent(typeof(AudioObj)))
            {
                var audio = e.Data.GetData(typeof(AudioObj)) as AudioObj;
                if (audio != null && !this.AudioC.Audios.Contains(audio))
                {
                    this.AudioC.Audios.Add(audio);
                }
                return;
            }
            // Liste von AudioObj
            if (e.Data.GetDataPresent(typeof(List<AudioObj>)))
            {
                var audioList = e.Data.GetData(typeof(List<AudioObj>)) as List<AudioObj>;
                if (audioList != null)
                {
                    foreach (var audio in audioList)
                    {
                        if (!this.AudioC.Audios.Contains(audio))
                        {
                            this.AudioC.Audios.Add(audio);
                        }
                    }
                }
                return;
            }
            // IEnumerable<AudioObj>
            if (e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)))
            {
                var enumerable = e.Data.GetData(typeof(IEnumerable<AudioObj>)) as IEnumerable<AudioObj>;
                if (enumerable != null)
                {
                    foreach (var audio in enumerable)
                    {
                        if (audio is AudioObj a && !this.AudioC.Audios.Contains(a))
                        {
                            this.AudioC.Audios.Add(a);
                        }
                    }
                }
                return;
            }
            // Drag aus ListBox.SelectedObjectCollection
            if (e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
            {
                var selected = e.Data.GetData(typeof(ListBox.SelectedObjectCollection)) as ListBox.SelectedObjectCollection;
                if (selected != null)
                {
                    foreach (var item in selected)
                    {
                        if (item is AudioObj audio && !this.AudioC.Audios.Contains(audio))
                        {
                            this.AudioC.Audios.Add(audio);
                        }
                    }
                }
                return;
            }
            // Drag als Serializable
            if (e.Data.GetDataPresent(DataFormats.Serializable))
            {
                var data = e.Data.GetData(DataFormats.Serializable);
                if (data is AudioObj audio && !this.AudioC.Audios.Contains(audio))
                {
                    this.AudioC.Audios.Add(audio);
                    return;
                }
                if (data is IEnumerable<AudioObj> list)
                {
                    foreach (var a in list)
                    {
                        if (!this.AudioC.Audios.Contains(a))
                        {
                            this.AudioC.Audios.Add(a);
                        }
                    }
                    return;
                }
            }
        }

        private async Task RebuildPatternPanelsAsync(List<List<bool>>? restoreStates = null)
        {
            // Panels entfernen
            foreach (var panel in this.Panels)
            {
                if (panel.Parent != null)
                {
                    panel.Parent.Controls.Remove(panel);
                }
                panel.Dispose();
            }
            this.Panels.Clear();

            int audioCount = this.AudioC.Audios.Count;
            if (audioCount == 0)
            {
                this.panel_pattern.Visible = false;
                return;
            }

            // Layout-Parameter
            int availableHeight = this.ClientSize.Height - this.panel_pattern.Top - 20; // ClientSize für exakte Fläche
            int minPanelHeight = 20;
            int maxPanelHeight = 75;
            int panelSpacing = 2; // Abstand zwischen Panels
            int totalSpacing = (audioCount - 1) * panelSpacing;
            int panelHeight = Math.Max(minPanelHeight, Math.Min(maxPanelHeight, (availableHeight - totalSpacing) / audioCount));
            int width = this.panel_pattern.Width;
            int hits = this.Hits;

            // Panel-Vorlage entfernen (Designer-Button und alles aus panel_pattern löschen)
            this.panel_pattern.Controls.Clear();

            int y = this.panel_pattern.Top;
            for (int i = 0; i < audioCount; i++)
            {
                var audio = this.AudioC.Audios[i];
                Panel panel = new()
                {
                    Size = new Size(width, panelHeight),
                    Location = new Point(this.panel_pattern.Left, y),
                    Visible = true,
                    BackColor = (i % 2 == 0) ? Color.FromArgb(245, 245, 245) : Color.FromArgb(230, 230, 230)
                };

                // Rechtsklick-Menü für Entfernen und Edit Sample
                ContextMenuStrip cms = new();
                var removeItem = new ToolStripMenuItem("Remove");
                removeItem.Click += async (s, e) =>
                {
                    // Capture states before removing
                    var states = this.CapturePatternButtonStates();
                    this.AudioC.Audios.Remove(audio);
                    // Remove the corresponding state row
                    if (i < states.Count)
                    {
                        states.RemoveAt(i);
                    }

                    await this.RebuildPatternPanelsAsync(states);
                };
                var editItem = new ToolStripMenuItem("Edit Sample");
                // Open TrackView modeless and update audio on close to avoid forcing other windows to the background
                editItem.Click += (s, e) =>
                {
                    var tv = new TrackView(audio, this.AudioC);
                    tv.StartPosition = FormStartPosition.Manual;
                    tv.Location = WindowsScreenHelper.GetCenterStartingPoint(this);

                    // When the TrackView is closed, check the DialogResult and update the sample if it was changed
                    tv.FormClosed += async (o, ev) =>
                    {
                        try
                        {
                            if (tv.DialogResult == DialogResult.OK || tv.DialogResult == DialogResult.None)
                            {
                                var edited = tv.OriginalAudio ?? audio;
                                int idx = this.AudioC.Audios.IndexOf(audio);
                                if (idx >= 0 && edited != null && !ReferenceEquals(audio, edited))
                                {
                                    this.AudioC.Audios[idx] = edited;
                                    await this.RebuildPatternPanelsAsync();
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            try { tv.Dispose(); } catch { }
                        }
                    };

                    // Show modeless without setting owner to avoid preventing DrumRollEditor from closing
                    tv.Show();
                    try { tv.BringToFront(); } catch { }
                };

                // Randomize-Pattern-Menüpunkt
                var randomizeItem = new ToolStripMenuItem("Randomize");
                randomizeItem.Click += (s, e) =>
                {
                    // Alle Buttons im Panel finden und zufällig toggeln
                    var rand = new Random();
                    foreach (Control ctrl in panel.Controls)
                    {
                        if (ctrl is Button btn)
                        {
                            // 50% Chance toggeln
                            btn.BackColor = rand.NextDouble() < 0.5 ? Color.Green : Color.LightGray;
                        }
                    }
                };

                cms.Items.Add(editItem);
                cms.Items.Add(removeItem);
                cms.Items.Add(new ToolStripSeparator());
                cms.Items.Add(randomizeItem);
                panel.ContextMenuStrip = cms;

                // Label für Audio-Namen
                Label label; // declared in outer scope so following layout code can reference it
                {
                    string nameText = string.IsNullOrWhiteSpace(audio.Name) ? "untitled" : audio.Name;

                    // Breite für den Namen: etwas großzügiger als vorher
                    int nameWidth = Math.Min(240, Math.Max(80, width / 4));
                    int nameHeight = panelHeight;

                    // Start-Schriftgröße (groß, wird bei Bedarf reduziert)
                    int startFontSize = Math.Max(8, panelHeight / 3);
                    int minFontSize = 7;

                    label = new Label
                    {
                        Text = nameText,
                        AutoSize = false,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Location = new Point(5, 0),
                        Size = new Size(nameWidth, nameHeight),
                        Font = new Font(this.Font.FontFamily, startFontSize, FontStyle.Bold),
                        AutoEllipsis = true,
                        UseCompatibleTextRendering = true
                    };

                    // Versuche, Schriftgröße so weit zu reduzieren, bis Text in das Rechteck passt (WordWrap erlaubt)
                    try
                    {
                        using var g = panel.CreateGraphics();
                        for (int fs = startFontSize; fs >= minFontSize; fs--)
                        {
                            using var testFont = new Font(this.Font.FontFamily, fs, FontStyle.Bold);
                            var measured = TextRenderer.MeasureText(
                                g,
                                nameText,
                                testFont,
                                new Size(nameWidth, nameHeight),
                                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                            if (measured.Height <= nameHeight)
                            {
                                label.Font = new Font(this.Font.FontFamily, fs, FontStyle.Bold);
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // ignore measurement failures, benutze Default-Font
                    }

                    // Tooltip mit vollständigem Namen (bei Ellipsis/Wrap hilfreich)
                    try
                    {
                        var tt = new ToolTip();
                        tt.SetToolTip(label, nameText);
                    }
                    catch { }

                    panel.Controls.Add(label);
                }

                // Buttons für jeden Hit
                int buttonAreaLeft = label.Right + 5;
                int buttonAreaWidth = width - buttonAreaLeft - 5;
                int buttonWidth = Math.Max(12, (buttonAreaWidth - (hits - 1) * 3) / hits);
                int buttonHeight = Math.Max(12, panelHeight - 10);
                for (int h = 0; h < hits; h++)
                {
                    Button button = new()
                    {
                        Size = new Size(buttonWidth, buttonHeight),
                        Location = new Point(buttonAreaLeft + h * (buttonWidth + 3), 5),
                        BackColor = Color.LightGray
                    };
                    // Button-Beschriftung: ab 21 Hits keine Beschriftung mehr
                    if (hits > 20)
                    {
                        button.Text = string.Empty;
                    }
                    else
                    {
                        string numStr = (h + 1).ToString();
                        if (buttonWidth < 22 && numStr.Length == 2)
                        {
                            button.Text = numStr;
                            button.Font = new Font(this.Font.FontFamily, 6, FontStyle.Regular);
                        }
                        else if (buttonWidth < 30)
                        {
                            button.Text = numStr;
                            button.Font = new Font(this.Font.FontFamily, 7, FontStyle.Regular);
                        }
                        else
                        {
                            button.Text = numStr;
                            button.Font = new Font(this.Font.FontFamily, 8, FontStyle.Regular);
                        }
                        button.UseCompatibleTextRendering = true;
                    }
                    button.Click += (s, e) =>
                    {
                        button.BackColor = button.BackColor == Color.LightGray ? Color.Green : Color.LightGray;
                    };
                    panel.Controls.Add(button);
                }

                this.Controls.Add(panel);
                this.Panels.Add(panel);
                y += panelHeight + panelSpacing;
            }

            // Restore button states if provided and hits count matches
            if (restoreStates != null && restoreStates.Count == this.Panels.Count && restoreStates.All(row => row.Count == hits))
            {
                this.RestorePatternButtonStates(restoreStates);
            }

            this.panel_pattern.Visible = false;
        }

        private void RandomizeAllPanels()
        {
            var rand = new Random();
            foreach (var panel in this.Panels)
            {
                foreach (Control ctrl in panel.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        btn.BackColor = rand.NextDouble() < 0.5 ? Color.Green : Color.LightGray;
                    }
                }
            }
        }

        // OnKeyDown überschreiben:
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.R && !e.Handled)
            {
                this.RandomizeAllPanels();
                e.Handled = true;
            }
        }





        private static readonly Color StepActiveFore = Color.White;
        private static readonly Color StepDefaultFore = Color.Black;
        private static readonly Color StepDefaultBack = SystemColors.Control;

        private void HandleCurrentStep(int hits)
        {
            // --- UI-Logik: Hervorheben des aktuellen Steps ---
            int panelIdx = 0;
            foreach (Panel panel in this.Panels)
            {
                int btnIdx = 0;
                Button? playBtn = null; // Der Button des aktuellen Steps, falls er existiert
                foreach (Control control in panel.Controls)
                {
                    if (control is Button btn)
                    {
                        if (btnIdx == this.currentStep)
                        {
                            playBtn = btn;

                            // Markierung für den aktuellen Step
                            if (btn.Tag is string tag && tag == "active")
                            {
                                btn.BackColor = Color.Red;
                            }
                            else if (btn.Tag is string tag2 && tag2 == "inactive")
                            {
                                btn.BackColor = Color.Orange;
                            }
                            btn.ForeColor = StepActiveFore;
                        }
                        else
                        {
                            // Zurücksetzen der Farbe
                            if (btn.Tag is string tag && tag == "active")
                            {
                                btn.BackColor = Color.Green;
                            }
                            else if (btn.Tag is string tag2 && tag2 == "inactive")
                            {
                                btn.BackColor = StepDefaultBack;
                            }

                            btn.ForeColor = StepDefaultFore;
                        }
                        btnIdx++;
                    }
                }

                // Audio abspielen, wenn Button im aktuellen Step im Pattern aktiv ist
                if (playBtn != null && (playBtn.Tag as string) == "active")
                {
                    if (panelIdx < this.AudioC.Audios.Count)
                    {
                        var audio = this.AudioC.Audios[panelIdx];
                        // HIER der schnelle, nicht-blockierende Aufruf für überlappende Schläge
                        this.PlayAudioInstant(audio);
                    }
                }
                panelIdx++;
            }

            this.currentStep = (this.currentStep + 1) % hits;
        }

        private async Task PlayAudioAsync(AudioObj audio)
        {
            try
            {
                using var cts = new CancellationTokenSource();
                await audio.PlayAsync(cts.Token, null, this.Volume, 25);
                audio.Dispose(); // Klon nach Wiedergabe entsorgen
            }
            catch
            {
                // Fehler beim Playback ignorieren
                try { audio.Dispose(); } catch { }
            }
        }

        private async Task PlaybackLoop(CancellationToken cancellationToken)
        {
            int totalHits = this.Hits;
            this.currentStep = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                long startTick = DateTime.Now.Ticks;
                int intervalMs = this.GetTimerIntervalMs(); // Liest BPM live

                // UI-Update und Audio-Trigger auf dem UI-Thread ausführen
                this.Invoke((MethodInvoker) (() =>
                {
                    this.HandleCurrentStep(totalHits);
                }));

                // Präzise Verzögerung berechnen
                long elapsedTicks = DateTime.Now.Ticks - startTick;
                int elapsedMs = (int) new TimeSpan(elapsedTicks).TotalMilliseconds;
                int delayMs = Math.Max(0, intervalMs - elapsedMs);

                try
                {
                    // Warten auf den nächsten Step
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void PlayAudioInstant(AudioObj audio)
        {
            if (audio.Data == null || audio.Data.LongLength == 0)
            {
                return;
            }

            var sampleProvider = new SampleData(audio.Data, audio.SampleRate, audio.Channels);

            // Mono zu Stereo konvertieren, falls nötig
            ISampleProvider provider = sampleProvider;
            if (sampleProvider.WaveFormat.Channels == 1 && this.outputFormat.Channels == 2)
            {
                provider = new MonoToStereoSampleProvider(sampleProvider);
            }

            var finalVolumeProvider = new VolumeSampleProvider(provider)
            {
                Volume = this.Volume
            };

            this.EnsureOutputReady();
            this.mixer?.AddMixerInput(finalVolumeProvider);
        }


        private void Button_playback_Click(object sender, EventArgs e)
        {
            if (this.isPlaying)
            {
                this.StopPlayback();
            }
            else
            {
                this.StartPlayback();
            }
        }

        private void EnsureOutputReady()
        {
            if (this.mixer == null)
            {
                this.mixer = new MixingSampleProvider(this.outputFormat) { ReadFully = true };
            }
            if (this.waveOut == null)
            {
                this.waveOut = new WaveOutEvent();
                this.waveOut.Init(this.mixer);
            }
            if (this.waveOut.PlaybackState != PlaybackState.Playing)
            {
                this.waveOut.Play();
            }
        }

        private void StartPlayback()
        {
            if (this.isPlaying)
            {
                return;
            }

            this.isPlaying = true;
            this.currentStep = -1; // -1, damit HandleCurrentStep sofort mit 0 startet
            this.button_playback.Text = "Stop";

            this.playbackCts = new CancellationTokenSource();
            this.playbackTask = this.PlaybackLoop(this.playbackCts.Token);

            // Erlaubt das Anpassen des BPM während der Wiedergabe, da die Loop 
            // den Wert in jeder Iteration neu liest.
            this.numericUpDown_bpm.ValueChanged += this.Bpm_ValueChanged;
        }

        private void StopPlayback()
        {
            if (!this.isPlaying)
            {
                return;
            }

            this.isPlaying = false;
            this.button_playback.Text = "Play";

            this.playbackCts?.Cancel();
            try { this.playbackTask?.Wait(); } catch { }
            this.playbackCts?.Dispose();

            this.currentStep = -1;
            this.HandleCurrentStep(this.Hits);

            this.numericUpDown_bpm.ValueChanged -= this.Bpm_ValueChanged;

            // Mixer und WaveOut aufräumen
            this.waveOut?.Stop();
            this.waveOut?.Dispose();
            this.waveOut = null;
            this.mixer = null;
        }

        private void Bpm_ValueChanged(object? sender, EventArgs e)
        {
            // Da die PlaybackLoop in jeder Iteration this.GetTimerIntervalMs() aufruft, 
            // wird die BPM-Änderung sofort im nächsten Step wirksam. Keine weitere Logik nötig.
        }


        public async Task<AudioObj> GenerateSampleAsync()
        {
            // 1. Pattern auslesen
            int hits = this.Hits;
            float bpm = this.Bpm;
            float secondsPerStep = 60f / bpm * 4f / hits; // 4/4-Takt
            int sampleRate = 44100;
            int channels = 2;

            // 2. Länge berechnen (in Samples)
            int totalSamples = (int)(secondsPerStep * hits * sampleRate);
            float[] mixBuffer = new float[totalSamples * channels];

            // 3. Für jede Spur (Panel) und Step prüfen, ob aktiv, dann Audio einmischen
            for (int trackIdx = 0; trackIdx < this.Panels.Count; trackIdx++)
            {
                if (trackIdx >= this.AudioC.Audios.Count)
                {
                    continue;
                }

                var audio = this.AudioC.Audios[trackIdx];
                if (audio.Data == null || audio.Data.Length == 0)
                {
                    continue;
                }

                int audioChannels = audio.Channels > 0 ? audio.Channels : 1;
                int audioSampleRate = audio.SampleRate > 0 ? audio.SampleRate : sampleRate;
                float[] audioData = audio.Data;
                int audioLen = audioData.Length / audioChannels;

                var panel = this.Panels[trackIdx];
                int btnIdx = 0;
                foreach (Control ctrl in panel.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        if (btn.BackColor == Color.Green)
                        {
                            // Step aktiv: Audio an diese Position mischen
                            int stepStart = (int)(btnIdx * secondsPerStep * sampleRate);
                            for (int n = 0; n < audioLen; n++)
                            {
                                int mixPos = (stepStart + n) * channels;
                                int srcPos = n * audioChannels;
                                if (mixPos + channels > mixBuffer.Length)
                                {
                                    break;
                                }

                                for (int c = 0; c < channels; c++)
                                {
                                    float sample = audioData[srcPos + (c % audioChannels)];
                                    mixBuffer[mixPos + c] += sample * this.Volume;
                                }
                            }
                        }
                        btnIdx++;
                        if (btnIdx >= hits)
                        {
                            break;
                        }
                    }
                }
            }

            // 4. Clipping verhindern
            for (int i = 0; i < mixBuffer.Length; i++)
            {
                if (mixBuffer[i] > 1f)
                {
                    mixBuffer[i] = 1f;
                }

                if (mixBuffer[i] < -1f)
                {
                    mixBuffer[i] = -1f;
                }
            }

            // 5. AudioObj erzeugen
            var result = new AudioObj
            {
                Name = "DrumRollMix_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Data = mixBuffer,
                SampleRate = sampleRate,
                Channels = channels,
                Duration = TimeSpan.FromSeconds(secondsPerStep * hits),
                Length = mixBuffer.Length,
                BitDepth = 32,
                Bpm = bpm
			};


            return result;
        }

        private async void button_export_Click(object sender, EventArgs e)
        {
            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

            var mixed = await this.GenerateSampleAsync();

            if (this.CollectionView == null)
            {
                this.CollectionView = new AudioCollectionView([mixed]);
                this.CollectionView.Rename("Drum Roll Edits");
                WindowMain.CollectionViews.Add(this.CollectionView);
            }
            else
            {
                this.CollectionView.AudioC.Audios.Add(mixed);
            }
            this.CollectionView.Show();
            this.CollectionView.BringToFront();

            if (ctrlFlag)
            {
                string? exported = await this.AudioC.Exporter.ExportWavAsync(mixed);
            }
        }

        private List<List<bool>> CapturePatternButtonStates()
        {
            var states = new List<List<bool>>();
            foreach (var panel in this.Panels)
            {
                var panelStates = new List<bool>();
                foreach (Control ctrl in panel.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        panelStates.Add(btn.BackColor == Color.Green);
                    }
                }
                states.Add(panelStates);
            }
            return states;
        }

        private void RestorePatternButtonStates(List<List<bool>> states)
        {
            for (int i = 0; i < this.Panels.Count && i < states.Count; i++)
            {
                var panel = this.Panels[i];
                var panelStates = states[i];
                int btnIdx = 0;
                foreach (Control ctrl in panel.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        if (btnIdx < panelStates.Count)
                        {
                            btn.BackColor = panelStates[btnIdx] ? Color.Green : Color.LightGray;
                        }
                        btnIdx++;
                    }
                }
            }
        }

        private void DrumRollEditor_Resize(object? sender, EventArgs e)
        {
            _ = this.ResizePanelsAndButtonsAsync();
        }

        private async Task ResizePanelsAndButtonsAsync()
        {
            int width = this.ClientSize.Width - (this.panel_pattern.Left * 2);
            int hits = this.Hits;
            int audioCount = this.Panels.Count;
            if (audioCount == 0 || hits <= 0)
                return;

            int availableHeight = this.ClientSize.Height - this.panel_pattern.Top - 20;
            int minPanelHeight = 20;
            int maxPanelHeight = 75;
            int panelSpacing = 2;
            int totalSpacing = (audioCount - 1) * panelSpacing;
            int panelHeight = Math.Max(minPanelHeight, Math.Min(maxPanelHeight, (availableHeight - totalSpacing) / audioCount));

            for (int i = 0; i < audioCount; i++)
            {
                var panel = this.Panels[i];
                panel.SuspendLayout();
                panel.Size = new Size(width, panelHeight);
                panel.Location = new Point(this.panel_pattern.Left, this.panel_pattern.Top + i * (panelHeight + panelSpacing));

                // Find label and buttons
                Label? label = null;
                foreach (Control ctrl in panel.Controls)
                {
                    if (ctrl is Label lbl)
                    {
                        label = lbl;
                        break;
                    }
                }
                if (label != null)
                {
                    int nameWidth = Math.Min(240, Math.Max(80, width / 4));
                    int nameHeight = panelHeight;
                    label.Size = new Size(nameWidth, nameHeight);
                }
                int buttonAreaLeft = label?.Right + 5 ?? 5;
                int buttonAreaWidth = width - buttonAreaLeft - 5;
                int buttonWidth = Math.Max(12, (buttonAreaWidth - (hits - 1) * 3) / hits);
                int buttonHeight = Math.Max(12, panelHeight - 10);
                int btnIdx = 0;
                foreach (Control ctrl in panel.Controls)
                {
                    if (ctrl is Button btn)
                    {
                        btn.Size = new Size(buttonWidth, buttonHeight);
                        btn.Location = new Point(buttonAreaLeft + btnIdx * (buttonWidth + 3), 5);
                        btnIdx++;
                    }
                }
                panel.ResumeLayout();
            }
            await Task.CompletedTask;
        }
	}
}
