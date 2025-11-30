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

        // Scheduler-specific
        private CancellationTokenSource? schedulerCts;
        private Task? schedulerTask;
        private readonly Lock outputLock = new();
        private readonly int schedulingLookaheadMs = 150;
        private volatile int currentStep = 0;
        private bool isPlaying = false;
		private volatile float schedulerBpm;
		private volatile int schedulerHits;


		public DrumRollEditor(IEnumerable<AudioObj>? samples = null)
        {
            this.InitializeComponent();
            this.KeyPreview = true;
            this.panel_pattern.Visible = false;
            this.button_hit.Visible = false;
            this.domainUpDown_hits.SelectedIndex = this.domainUpDown_hits.Items.IndexOf("16");

			int maxHeight = (Screen.PrimaryScreen?.WorkingArea.Height ?? 720) * 3 / 4;
			this.MaximumSize = new Size(this.MaximumSize.Width, maxHeight);

			this.outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            this.waveOut = null;
            this.mixer = null;

            if (samples != null)
            {
                foreach (AudioObj sample in samples)
                {
                    this.AudioC.Audios.Add(sample.Clone());
                }
            }

			this.StartPosition = FormStartPosition.Manual;
			this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false);

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



        private async void AudioC_Audios_ListChanged(object? sender, ListChangedEventArgs e)
        {
            await this.RebuildPatternPanelsAsync();
        }

		private async void domainUpDown_hits_SelectedItemChanged(object? sender, EventArgs e)
		{
			// Update scheduler-safe copy sofort auf UI-Thread
			try
			{
				this.schedulerHits = this.Hits;
			}
			catch { }

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
			// Panels entfernen (sicher)
			foreach (var panel in this.Panels)
			{
				try
				{
					if (panel.Parent != null)
					{
						panel.Parent.Controls.Remove(panel);
					}
					panel.Dispose();
				}
				catch { }
			}
			this.Panels.Clear();

			int audioCount = this.AudioC.Audios.Count;
			if (audioCount == 0)
			{
				try { this.panel_pattern.Visible = false; } catch { }
				return;
			}

			// Panel_pattern positionieren (unterhalb label_info_hits) und Scroll konfigurieren
			try
			{
				int top = Math.Max(0, this.label_info_hits.Bottom + 4);
				int left = Math.Max(0, this.panel_pattern.Left);
				int rightMargin = 12;
				int bottomMargin = 20;

				int width = Math.Max(64, this.ClientSize.Width - left - rightMargin);
				int height = Math.Max(80, this.ClientSize.Height - top - bottomMargin);

				this.panel_pattern.Dock = DockStyle.None;
				this.panel_pattern.Location = new Point(left, top);
				this.panel_pattern.Size = new Size(width, height);

				// Keine horizontale Scroll-Leiste erlauben
				this.panel_pattern.AutoScroll = true;
				try
				{
					this.panel_pattern.HorizontalScroll.Enabled = false;
					this.panel_pattern.HorizontalScroll.Visible = false;
				}
				catch { } // manche Framework-Versionen erlauben nicht direktes Schreiben

				// Sicherstellen, dass Panel keine Innen-Abstände erzeugt
				try { this.panel_pattern.Padding = Padding.Empty; } catch { }
				try { this.panel_pattern.Margin = Padding.Empty; } catch { }

				// Panel automatisch in Höhe und Breite anpassen, wenn Form resized wird
				this.panel_pattern.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

				this.panel_pattern.Controls.Clear();
				this.panel_pattern.Visible = true;
			}
			catch { }

			// Layout-Parameter (einmalig lesen)
			int availableHeight = Math.Max(0, this.panel_pattern.ClientSize.Height);
			int minPanelHeight = 25;
			int maxPanelHeight = 75;
			int panelSpacing = 2;
			int totalSpacing = Math.Max(0, (audioCount - 1) * panelSpacing);
			int panelHeight = Math.Max(minPanelHeight, Math.Min(maxPanelHeight, (availableHeight - totalSpacing) / Math.Max(1, audioCount)));

			// Container-Breite berücksichtigen; wenn vertikale Scrollbar benötigt wird, Platz dafür abziehen
			int containerWidth = Math.Max(64, this.panel_pattern.ClientSize.Width);
			int estimatedContentHeight = audioCount * (panelHeight + panelSpacing) - panelSpacing;
			bool willNeedVScroll = estimatedContentHeight > this.panel_pattern.ClientSize.Height;
			if (willNeedVScroll)
			{
				containerWidth = Math.Max(64, containerWidth - SystemInformation.VerticalScrollBarWidth);
			}

			int hits = this.Hits;

			// Phase 1: parallele Layout-Berechnung pro Panel
			var specTasks = new Task<(int Index, Color BackColor, string NameText, int NameWidth, int NameHeight, int ButtonAreaLeft, int ButtonAreaWidth, int ButtonWidth, int ButtonHeight, List<string> ButtonTexts)>[audioCount];

			string fontFamilyName = this.Font?.FontFamily?.Name ?? SystemFonts.DefaultFont.FontFamily.Name;

			for (int i = 0; i < audioCount; i++)
			{
				int idx = i;
				var audio = this.AudioC.Audios[idx];

				specTasks[idx] = Task.Run(() =>
				{
					Color backColor = (idx % 2 == 0) ? Color.FromArgb(245, 245, 245) : Color.FromArgb(230, 230, 230);
					string nameText = string.IsNullOrWhiteSpace(audio.Name) ? "untitled" : audio.Name;

					int nameWidth = Math.Min(240, Math.Max(80, containerWidth / 4));
					int nameHeight = panelHeight;

					int startFontSize = Math.Max(8, panelHeight / 3);
					int minFontSize = 7;
					int chosenFontSize = startFontSize;
					try
					{
						using var bmp = new Bitmap(Math.Max(1, nameWidth), Math.Max(1, nameHeight));
						using var g = Graphics.FromImage(bmp);
						g.PageUnit = GraphicsUnit.Pixel;
						for (int fs = startFontSize; fs >= minFontSize; fs--)
						{
							using var ff = new Font(new FontFamily(fontFamilyName), fs, FontStyle.Bold);
							var measured = g.MeasureString(nameText, ff, nameWidth);
							if (measured.Height <= nameHeight)
							{
								chosenFontSize = fs;
								break;
							}
						}
					}
					catch
					{
						chosenFontSize = Math.Max(minFontSize, startFontSize);
					}

					int buttonAreaLeft = nameWidth + 5;
					int buttonAreaWidth = Math.Max(0, containerWidth - buttonAreaLeft - 5);

					// Berechne ButtonWidth so, dass Buttons immer in die Breite passen (keine horizontale Scroll)
					int spacing = 3;
					int buttonWidth = Math.Max(12, (buttonAreaWidth - (hits - 1) * spacing) / Math.Max(1, hits));
					int buttonHeight = Math.Max(12, panelHeight - 10);

					var buttonTexts = new List<string>(hits);
					for (int h = 0; h < hits; h++)
					{
						if (hits > 20)
						{
							buttonTexts.Add(string.Empty);
						}
						else
						{
							buttonTexts.Add((h + 1).ToString());
						}
					}

					return (Index: idx, BackColor: backColor, NameText: nameText, NameWidth: nameWidth, NameHeight: nameHeight, ButtonAreaLeft: buttonAreaLeft, ButtonAreaWidth: buttonAreaWidth, ButtonWidth: buttonWidth, ButtonHeight: buttonHeight, ButtonTexts: buttonTexts);
				});
			}

			var specs = await Task.WhenAll(specTasks).ConfigureAwait(false);

			// Phase 2: Controls auf UI-Thread erstellen
			try
			{
				if (this.IsHandleCreated && !this.IsDisposed)
				{
					this.Invoke((MethodInvoker) (() =>
					{
						int y = 0;
						foreach (var spec in specs.OrderBy(s => s.Index))
						{
							int i = spec.Index;
							var audio = this.AudioC.Audios[i];

							Panel panel = new()
							{
								Size = new Size(containerWidth, panelHeight),
								Location = new Point(0, y),
								Visible = true,
								BackColor = spec.BackColor,
								Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
							};

							ContextMenuStrip cms = new();
							var removeItem = new ToolStripMenuItem("Remove");
							removeItem.Click += async (s, e) =>
							{
								var states = this.CapturePatternButtonStates();
								int idxRemove = this.AudioC.Audios.IndexOf(audio);
								if (idxRemove >= 0)
								{
									this.AudioC.Audios.Remove(audio);
									if (idxRemove < states.Count)
									{
										states.RemoveAt(idxRemove);
									}
								}
								await this.RebuildPatternPanelsAsync(states);
							};
							var editItem = new ToolStripMenuItem("Edit Sample");
							editItem.Click += (s, e) =>
							{
								var tv = new TrackView(audio)
								{
									StartPosition = FormStartPosition.Manual,
									Location = WindowsScreenHelper.GetCenterStartingPoint(this)
								};

								tv.FormClosed += async (o, ev) =>
								{
									try
									{
										if (tv.DialogResult == DialogResult.OK || tv.DialogResult == DialogResult.None)
										{
											var edited = tv.OriginalAudio ?? audio;
											int idx2 = this.AudioC.Audios.IndexOf(audio);
											if (idx2 >= 0 && edited != null && !ReferenceEquals(audio, edited))
											{
												this.AudioC.Audios[idx2] = edited;
												await this.RebuildPatternPanelsAsync();
											}
										}
									}
									catch { }
									finally { try { tv.Dispose(); } catch { } }
								};

								tv.Show();
								try { tv.BringToFront(); } catch { }
							};

							var randomizeItem = new ToolStripMenuItem("Randomize");
							randomizeItem.Click += (s, e) =>
							{
								var rand = new Random();
								foreach (Control ctrl in panel.Controls)
								{
									if (ctrl is Button btn)
									{
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
							Label label;
							{
								string nameText = spec.NameText;
								int nameWidth = spec.NameWidth;
								int nameHeight = spec.NameHeight;
								float chosenFontSize = Math.Max(7, (nameHeight / 3f));

								Font font = this.Font ?? SystemFonts.DefaultFont;
								label = new Label
								{
									Text = nameText,
									AutoSize = false,
									TextAlign = ContentAlignment.MiddleLeft,
									Location = new Point(5, 0),
									Size = new Size(nameWidth, nameHeight),
									Font = new Font(font.FontFamily, chosenFontSize, FontStyle.Bold),
									AutoEllipsis = true,
									UseCompatibleTextRendering = true,
									Anchor = AnchorStyles.Left | AnchorStyles.Top
								};

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
							int buttonWidth = spec.ButtonWidth;
							int buttonHeight = spec.ButtonHeight;
							int spacingLocal = 3;
							for (int h = 0; h < hits; h++)
							{
								Button button = new()
								{
									Size = new Size(buttonWidth, buttonHeight),
									Location = new Point(buttonAreaLeft + h * (buttonWidth + spacingLocal), 5),
									BackColor = Color.LightGray,
									Anchor = AnchorStyles.Top
								};

								string text = spec.ButtonTexts.Count > h ? spec.ButtonTexts[h] : string.Empty;
								if (!string.IsNullOrEmpty(text))
								{
									Font font = this.Font ?? new Font(FontFamily.GenericMonospace, 8, FontStyle.Regular);
									if (buttonWidth < 22 && text.Length == 2)
									{
										button.Text = text;
										button.Font = new Font(font.FontFamily, 6, FontStyle.Regular);
									}
									else if (buttonWidth < 30)
									{
										button.Text = text;
										button.Font = new Font(font.FontFamily, 7, FontStyle.Regular);
									}
									else
									{
										button.Text = text;
										button.Font = new Font(font.FontFamily, 8, FontStyle.Regular);
									}
									button.UseCompatibleTextRendering = true;
								}
								button.Click += (s, e) =>
								{
									button.BackColor = button.BackColor == Color.LightGray ? Color.Green : Color.LightGray;
								};
								panel.Controls.Add(button);
							}

							// Panel in den scrollbaren Container einfügen
							this.panel_pattern.Controls.Add(panel);
							this.Panels.Add(panel);

							y += panelHeight + panelSpacing;
						}

						// Restore button states falls angegeben und passend
						if (restoreStates != null && restoreStates.Count == this.Panels.Count && restoreStates.All(row => row.Count == hits))
						{
							this.RestorePatternButtonStates(restoreStates);
						}

						// Setze AutoScrollMinSize damit VerticalScroll korrekt erscheint, aber HorizontalScroll nicht
						int totalHeight = this.Panels.Count * (panelHeight + panelSpacing) - panelSpacing;
						try
						{
							this.panel_pattern.AutoScrollMinSize = new Size(0, Math.Max(0, totalHeight));
						}
						catch { }

						// Scrollposition auf Anfang zurücksetzen (kein leerer Bereich oben)
						try
						{
							this.panel_pattern.AutoScrollPosition = new Point(0, 0);
						}
						catch { }
						try
						{
							if (this.panel_pattern.VerticalScroll != null)
							{
								this.panel_pattern.VerticalScroll.Value = this.panel_pattern.VerticalScroll.Minimum;
							}
						}
						catch { }

						// Sicherstellen, dass erstes Panel sichtbar ist
						try
						{
							if (this.Panels.Count > 0)
							{
								this.panel_pattern.ScrollControlIntoView(this.Panels[0]);
								this.panel_pattern.AutoScrollPosition = new Point(0, 0);
							}
						}
						catch { }

						this.panel_pattern.Visible = true;
					}));
				}
			}
			catch
			{
				try
				{
					if (this.IsHandleCreated && !this.IsDisposed)
					{
						this.Invoke((MethodInvoker) (() =>
						{
							this.panel_pattern.Visible = true;
						}));
					}
				}
				catch { }
			}
		}

		private async Task ResizePanelsAndButtonsAsync()
		{
			int hits = this.Hits;
			int audioCount = this.Panels.Count;
			if (audioCount == 0 || hits <= 0)
			{
				return;
			}

			// Stelle panel_pattern immer direkt unter label_info_hits und skaliere Höhe/Breite
			try
			{
				int top = Math.Max(0, this.label_info_hits.Bottom + 4);
				int left = Math.Max(0, this.panel_pattern.Left);
				int rightMargin = 12;
				int bottomMargin = 20;
				int width = Math.Max(64, this.ClientSize.Width - left - rightMargin);
				int height = Math.Max(80, this.ClientSize.Height - top - bottomMargin);
				this.panel_pattern.Location = new Point(left, top);
				this.panel_pattern.Size = new Size(width, height);
				this.panel_pattern.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
			}
			catch { }

			int availableHeight = Math.Max(0, this.panel_pattern.ClientSize.Height);
			int minPanelHeight = 25;
			int maxPanelHeight = 75;
			int panelSpacing = 2;
			int totalSpacing = (audioCount - 1) * panelSpacing;
			int panelHeight = Math.Max(minPanelHeight, Math.Min(maxPanelHeight, (availableHeight - totalSpacing) / audioCount));

			// Breite berechnen und anpassen, wenn vertikaler Scroll benötigt wird
			int containerWidth = Math.Max(64, this.panel_pattern.ClientSize.Width);
			int estimatedContentHeight = audioCount * (panelHeight + panelSpacing) - panelSpacing;
			bool needVScroll = estimatedContentHeight > this.panel_pattern.ClientSize.Height;
			if (needVScroll)
			{
				containerWidth = Math.Max(64, containerWidth - SystemInformation.VerticalScrollBarWidth);
			}

			int spacing = 3;

			for (int i = 0; i < audioCount; i++)
			{
				var panel = this.Panels[i];
				panel.SuspendLayout();

				panel.Size = new Size(containerWidth, panelHeight);
				panel.Location = new Point(0, i * (panelHeight + panelSpacing));

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
					int nameWidth = Math.Min(240, Math.Max(80, containerWidth / 4));
					int nameHeight = panelHeight;
					label.Size = new Size(nameWidth, nameHeight);
				}

				int buttonAreaLeft = label?.Right + 5 ?? 5;
				int buttonAreaWidth = containerWidth - buttonAreaLeft - 5;
				int buttonWidth = Math.Max(12, (buttonAreaWidth - (hits - 1) * spacing) / Math.Max(1, hits));
				int buttonHeight = Math.Max(12, panelHeight - 10);

				int btnIdx = 0;
				foreach (Control ctrl in panel.Controls)
				{
					if (ctrl is Button btn)
					{
						btn.Size = new Size(buttonWidth, buttonHeight);
						btn.Location = new Point(buttonAreaLeft + btnIdx * (buttonWidth + spacing), 5);
						btnIdx++;
					}
				}

				panel.ResumeLayout();
			}

			// AutoScrollMinSize anpassen und Scroll-Position bereinigen
			try
			{
				int totalHeight = audioCount * (panelHeight + panelSpacing) - panelSpacing;
				this.panel_pattern.AutoScrollMinSize = new Size(0, Math.Max(0, totalHeight));
				try
				{
					this.panel_pattern.HorizontalScroll.Enabled = false;
					this.panel_pattern.HorizontalScroll.Visible = false;
				}
				catch { }

				// Reset scroll to top (verhindert leere Fläche oben nach Resize)
				try { this.panel_pattern.AutoScrollPosition = new Point(0, 0); } catch { }
				try
				{
					if (this.panel_pattern.VerticalScroll != null)
					{
						this.panel_pattern.VerticalScroll.Value = this.panel_pattern.VerticalScroll.Minimum;
					}
				}
				catch { }
			}
			catch { }

			await Task.CompletedTask;
		}



		private void RandomizeAllPanels(bool interleaved = false)
		{
			var rand = new Random();
			int hits = this.Hits;

			if (interleaved)
			{
				// Single drum hit at most per step across all panels,
				// allow "no hit" by selecting Panels.Count as sentinel.
				if (this.Panels.Count == 0)
				{
					return;
				}

				for (int h = 0; h < hits; h++)
				{
					// Choose a random panel to activate this step or none:
					// range [0 .. Panels.Count] inclusive of Panels.Count -> "no hit"
					int choice = rand.Next(this.Panels.Count + 1);

					for (int p = 0; p < this.Panels.Count; p++)
					{
						var panel = this.Panels[p];
						int btnIdx = 0;
						foreach (Control ctrl in panel.Controls)
						{
							if (ctrl is Button btn)
							{
								if (btnIdx == h)
								{
									// If choice == Panels.Count -> treat as "no hit" -> leave all LightGray
									if (p == choice)
									{
										btn.BackColor = Color.Green;
									}
									else
									{
										btn.BackColor = Color.LightGray;
									}
									break;
								}
								btnIdx++;
							}
						}
					}
					// If choice == Panels.Count, none of the panels had p == choice,
					// so all corresponding step buttons remain LightGray -> "silent" step.
				}
			}
			else
			{
				var r = new Random();
				foreach (var panel in this.Panels)
				{
					foreach (Control ctrl in panel.Controls)
					{
						if (ctrl is Button btn)
						{
							btn.BackColor = r.NextDouble() < 0.5 ? Color.Green : Color.LightGray;
						}
					}
				}
			}
		}



		protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

			if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Space) && !e.Handled)
			{
				this.button_playback_Click(this, EventArgs.Empty);
				e.Handled = true;
				return;
			}
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			// R / Ctrl+R -> Randomize (interleaved when Ctrl gedrückt)
			if (keyData == Keys.R || keyData == (Keys.Control | Keys.R))
			{
				try
				{
					bool interleaved = (keyData & Keys.Control) == Keys.Control;
					this.RandomizeAllPanels(interleaved);
				}
				catch { }
				return true;
			}

			// Up / Down -> BPM anpassen (funktioniert auch wenn ein Child Control Fokus hat)
			if (keyData == Keys.Up || keyData == Keys.Down)
			{
				try
				{
					float step = ModifierKeys.HasFlag(Keys.Control) ? 0.1f : 1.0f;
					decimal current = this.numericUpDown_bpm.Value;
					decimal delta = (decimal) (keyData == Keys.Up ? step : -step);
					decimal next = Math.Clamp(current + delta, this.numericUpDown_bpm.Minimum, this.numericUpDown_bpm.Maximum);
					this.numericUpDown_bpm.Value = next;
				}
				catch { }
				return true;
			}

			// Space / Back handled weiterhin in OnKeyDown - fallthrough ansonsten
			return base.ProcessCmdKey(ref msg, keyData);
		}


		private static readonly Color StepActiveFore = Color.White;
        private static readonly Color StepDefaultFore = Color.Black;
        private static readonly Color StepDefaultBack = SystemColors.Control;



        private void HandleCurrentStepUI(int step, int hits)
        {
            int panelIdx = 0;
            foreach (Panel panel in this.Panels)
            {
                int btnIdx = 0;
                Button? playBtn = null;
                foreach (Control control in panel.Controls)
                {
                    if (control is Button btn)
                    {
                        if (btnIdx == step)
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
                panelIdx++;
            }
        }

		private async Task SchedulerLoop(CancellationToken cancellationToken)
		{
			// Start etwas in der Zukunft, damit wir Lookahead nutzen können
			DateTimeOffset nextScheduledTime = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(this.schedulingLookaheadMs);
			int stepIndex = 0;

			while (!cancellationToken.IsCancellationRequested)
			{
				DateTimeOffset now = DateTimeOffset.UtcNow;

				// schedule any steps that fall within now + lookahead
				while (!cancellationToken.IsCancellationRequested)
				{
					if (nextScheduledTime <= now + TimeSpan.FromMilliseconds(this.schedulingLookaheadMs))
					{
						// read live-safe copies
						float bpmNow = this.schedulerBpm > 0 ? this.schedulerBpm : this.Bpm;
						int hitsNow = this.schedulerHits > 0 ? this.schedulerHits : this.Hits;
						if (hitsNow <= 0)
						{
							hitsNow = 1;
						}

						// For each track, if the button at step is active, schedule audio
						for (int trackIdx = 0; trackIdx < this.Panels.Count; trackIdx++)
						{
							if (trackIdx >= this.AudioC.Audios.Count)
							{
								continue;
							}

							var panel = this.Panels[trackIdx];
							int btnIdx = 0;
							foreach (Control ctrl in panel.Controls)
							{
								if (ctrl is Button btn)
								{
									if (btnIdx == (stepIndex % hitsNow))
									{
										if (btn.BackColor == Color.Green)
										{
											var audio = this.AudioC.Audios[trackIdx];
											// schedule audio for exact nextScheduledTime
											try
											{
												this.ScheduleAudioAt(audio, nextScheduledTime, cancellationToken);
											}
											catch
											{
												// swallow scheduling errors
											}
										}
										break;
									}
									btnIdx++;
								}
							}
						}

						// schedule UI update to run exactly at nextScheduledTime
						_ = Task.Run(async () =>
						{
							try
							{
								var delay = nextScheduledTime - DateTimeOffset.UtcNow;
								if (delay > TimeSpan.Zero)
								{
									await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
								}
							}
							catch (TaskCanceledException) { return; }
							catch { }
							try
							{
								if (this.IsHandleCreated && !this.IsDisposed)
								{
									this.Invoke((MethodInvoker) (() =>
									{
										this.currentStep = stepIndex % (this.schedulerHits > 0 ? this.schedulerHits : this.Hits);
										this.HandleCurrentStepUI(this.currentStep, this.Hits);
									}));
								}
							}
							catch { }
						}, cancellationToken);

						// Schritt vorwärts: Intervall aus aktuellen Scheduler-Werten berechnen
						int intervalMs = ComputeIntervalMsFromValues(
							this.schedulerBpm > 0 ? this.schedulerBpm : this.Bpm,
							this.schedulerHits > 0 ? this.schedulerHits : this.Hits);

						nextScheduledTime = nextScheduledTime + TimeSpan.FromMilliseconds(intervalMs);
						stepIndex++;
					}
					else
					{
						break;
					}
				}

				// kurze Pause, responsive zu Änderungen
				try
				{
					await Task.Delay(Math.Max(5, this.schedulingLookaheadMs / 4), cancellationToken).ConfigureAwait(false);
				}
				catch (TaskCanceledException)
				{
					break;
				}
			}
		}



		private void ScheduleAudioAt(AudioObj audio, DateTimeOffset playAt, CancellationToken cancellationToken)
        {
            if (audio.Data == null || audio.Data.LongLength == 0)
            {
                return;
            }

            // Build the sample provider chain (resample, mono->stereo, volume)
            ISampleProvider provider;
            var sampleProvider = new SampleData(audio.Data, audio.SampleRate, audio.Channels);
            provider = sampleProvider;

            // Resample if needed
            if (sampleProvider.WaveFormat.SampleRate != this.outputFormat.SampleRate)
            {
                provider = new WdlResamplingSampleProvider(provider, this.outputFormat.SampleRate);
            }

            // Mono -> Stereo if needed
            if (provider.WaveFormat.Channels == 1 && this.outputFormat.Channels == 2)
            {
                provider = new MonoToStereoSampleProvider(provider);
            }

            var finalVolumeProvider = new VolumeSampleProvider(provider)
            {
                Volume = this.Volume
            };

            // Calculate delay relative to now
            TimeSpan delay = playAt - DateTimeOffset.UtcNow;
            OffsetSampleProvider? offsetProvider = null;
            if (delay > TimeSpan.Zero)
            {
                offsetProvider = new OffsetSampleProvider(finalVolumeProvider)
                {
                    // Delay until playAt. OffsetSampleProvider fills zeros until delay elapses.
                    DelayBy = delay,
                    // Ensure it doesn't trim leading silence:
                    LeadOut = TimeSpan.Zero
                };
            }

            lock (this.outputLock)
            {
                // Ensure output system is up
                this.EnsureOutputReady();

                // Add to mixer (with or without offset)
                if (offsetProvider != null)
                {
                    this.mixer?.AddMixerInput(offsetProvider);
                }
                else
                {
                    this.mixer?.AddMixerInput(finalVolumeProvider);
                }
            }
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
            lock (this.outputLock)
            {
                if (this.mixer == null)
                {
                    this.mixer = new MixingSampleProvider(this.outputFormat) { ReadFully = true };
                }
                if (this.waveOut == null)
                {
                    this.waveOut = new WaveOutEvent()
                    {
                        DesiredLatency = 100
                    };
                    // Init with ISampleProvider
                    this.waveOut.Init(this.mixer);
                }
                if (this.waveOut.PlaybackState != PlaybackState.Playing)
                {
                    try
                    {
                        this.waveOut.Play();
                    }
                    catch
                    {
                        // ignore errors on Play
                    }
                }
            }
        }

		private void StartPlayback()
		{
			if (this.isPlaying)
			{
				return;
			}

			this.isPlaying = true;
			this.currentStep = 0;
			this.button_playback.Text = "■";

			// initiale Scheduler-Werte von UI übernehmen (auf UI-Thread)
			this.schedulerBpm = this.Bpm;
			this.schedulerHits = this.Hits;

			// Play scheduler
			this.schedulerCts = new CancellationTokenSource();
			this.schedulerTask = Task.Run(() => this.SchedulerLoop(this.schedulerCts.Token));

			// Keep numericUpDown subscription to update BPM live
			this.numericUpDown_bpm.ValueChanged += this.Bpm_ValueChanged;
		}

		private void StopPlayback()
		{
			if (!this.isPlaying)
			{
				return;
			}

			this.isPlaying = false;
			this.button_playback.Text = "▶";

			// Cancel scheduler and wait
			try
			{
				this.schedulerCts?.Cancel();
				this.schedulerTask?.Wait(500);
			}
			catch { }
			finally
			{
				try { this.schedulerCts?.Dispose(); } catch { }
				this.schedulerCts = null;
				this.schedulerTask = null;
			}

			// Reset UI highlight
			this.currentStep = -1;
			try
			{
				if (this.IsHandleCreated && !this.IsDisposed)
				{
					this.Invoke((MethodInvoker) (() =>
					{
						this.HandleCurrentStepUI(0, this.Hits);
					}));
				}
			}
			catch { }

			// Unsubscribe BPM update
			try
			{
				this.numericUpDown_bpm.ValueChanged -= this.Bpm_ValueChanged;
			}
			catch { }

			// Stop playback but keep output alive for fast restart
			lock (this.outputLock)
			{
				try { this.waveOut?.Stop(); } catch { }
			}
		}




		public async Task<AudioObj> GenerateSampleAsync()
		{
			// UI-state sicher erfassen (auf UI-Thread)
			int hits = this.Hits;
			float bpm = this.Bpm;
			int sampleRate = 44100;
			int channels = 2;

			if (bpm <= 0f || hits <= 0)
			{
				// Fallbackwerte
				bpm = Math.Max(1f, bpm);
				hits = Math.Max(1, hits);
			}

			// Button-Zustände und Audiodaten als Snapshots erfassen, um Cross-Thread-Access zu vermeiden
			var patternStates = this.CapturePatternButtonStates(); // List<List<bool>> - UI-thread
			var audioSnapshots = new List<(float[] Data, int Channels, int SampleRate)>();
			int panelCount = this.Panels.Count;
			for (int trackIdx = 0; trackIdx < panelCount; trackIdx++)
			{
				if (trackIdx >= this.AudioC.Audios.Count)
				{
					audioSnapshots.Add((Array.Empty<float>(), 1, sampleRate));
					continue;
				}

				var audio = this.AudioC.Audios[trackIdx];
				if (audio?.Data == null || audio.Data.Length == 0)
				{
					audioSnapshots.Add((Array.Empty<float>(), Math.Max(1, audio?.Channels ?? 1), audio?.SampleRate > 0 ? audio.SampleRate : sampleRate));
					continue;
				}

				// Kopie der Audiodaten anfertigen, damit Background-Task nicht auf UI-Objekte zeigt
				float[] copy = new float[audio.Data.Length];
				Array.Copy(audio.Data, copy, audio.Data.Length);
				audioSnapshots.Add((copy, Math.Max(1, audio.Channels), audio.SampleRate > 0 ? audio.SampleRate : sampleRate));
			}

			// Dauer / Samples berechnen
			float secondsPerStep = 60f / bpm * 4f / hits; // 4/4-Takt
			int totalSamples = (int) (secondsPerStep * hits * sampleRate);
			if (totalSamples <= 0)
			{
				totalSamples = 1;
			}

			// Heavy CPU-Arbeit auf ThreadPool ausführen
			var mixBuffer = await Task.Run(() =>
			{
				var mix = new float[totalSamples * channels];

				int usableTracks = Math.Min(audioSnapshots.Count, patternStates.Count);
				for (int trackIdx = 0; trackIdx < usableTracks; trackIdx++)
				{
					var audioSnap = audioSnapshots[trackIdx];
					var statesRow = patternStates[trackIdx];
					if (audioSnap.Data == null || audioSnap.Data.Length == 0)
					{
						continue;
					}

					int audioChannels = audioSnap.Channels > 0 ? audioSnap.Channels : 1;
					float[] audioData = audioSnap.Data;
					int audioLen = audioData.Length / audioChannels;

					int btnIdx = 0;
					int stepsToCheck = Math.Min(hits, statesRow.Count);
					for (int step = 0; step < stepsToCheck; step++)
					{
						bool active = statesRow[step];
						if (active)
						{
							int stepStart = (int) (step * secondsPerStep * sampleRate);
							for (int n = 0; n < audioLen; n++)
							{
								int mixPos = (stepStart + n) * channels;
								int srcPos = n * audioChannels;
								if (mixPos + channels > mix.Length)
								{
									break;
								}

								for (int c = 0; c < channels; c++)
								{
									float sample = audioData[srcPos + (c % audioChannels)];
									mix[mixPos + c] += sample * this.Volume;
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

				// Clipping verhindern
				for (int i = 0; i < mix.Length; i++)
				{
					if (mix[i] > 1f)
					{
						mix[i] = 1f;
					}
					else if (mix[i] < -1f)
					{
						mix[i] = -1f;
					}
				}

				return mix;
			}).ConfigureAwait(false);

			// Ergebnis-Objekt erstellen (leichtgewichtiger UI-unabhängiger Schritt)
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



		private static int ComputeIntervalMsFromValues(float bpm, int hits)
		{
			if (bpm <= 0f || hits <= 0)
			{
				return 100;
			}

			return (int) (60000.0f / bpm * 4.0f / hits);
		}

		private void Bpm_ValueChanged(object? sender, EventArgs e)
		{
			// live übernehmen: sichere Kopie aktualisieren (ValueChanged läuft auf UI-Thread)
			try
			{
				this.schedulerBpm = this.Bpm;
			}
			catch { }
			// keine weitere Aktion nötig: Scheduler liest schedulerBpm regelmäßig
		}
	}
}
