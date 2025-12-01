using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System.ComponentModel;
using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Forms
{
	public partial class WindowMain : Form
	{
		public static WindowMain? Instance { get; private set; }

		public readonly AudioCollection AudioC = new();

		internal static readonly BindingList<AudioCollectionView> CollectionViews = [];
		internal static int TotalTracks => CollectionViews.Sum(cv => cv.AudioCount);
		internal static IEnumerable<AudioObj> SelectedTracks => CollectionViews.Where(cv => !cv.IsDisposed).SelectMany(cv => cv.SelectedAudios);
		internal static IEnumerable<TrackView> PlayingTrackViews => TrackViews.Where(tv => tv.OriginalAudio.PlayerPlaying);
		internal static bool IsAnyTrackPlaying => PlayingTrackViews.Any();
		internal static IEnumerable<TrackView> SyncedTrackViews => TrackViews.Where(tv => tv.Synced);

		internal static readonly BindingList<TrackView> TrackViews = [];
		internal static List<int> TrackViewIds { get; set; } = [];
		private static TrackView? _lastSelectedTrackView = null;
		internal static TrackView? LastSelectedTrackView
		{
			get => _lastSelectedTrackView;
			set
			{
				if (!ReferenceEquals(_lastSelectedTrackView, value))
				{
					_lastSelectedTrackView = value;
					UpdateTrackDependentUI();
					HighlightSelectedTrackView();
				}
			}
		}


		// Map AudioObj.Id -> Collection number (01-based) to restore distribution
		private static readonly Dictionary<Guid, int> AudioCollectionTags = [];
		private static readonly HashSet<string> AllowedImportExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac" };
		private static readonly Random ResourceRandom = new();
		private static readonly Size CollectionCascadeOffset = new(26, 28);
		private const int CollectionBaseMargin = 5;
		private static readonly Padding TrackViewScreenMargin = new(20, 20, 20, 20);
		private static readonly Size TrackViewSpacing = new(15, 12);
		private bool suppressExportFormatEvent;
		private string lastImportFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
		internal bool StructuredImports => this.checkBox_structure.Checked;

		internal static bool SuppressCollectionViewPositioning = false;

		// Recording timer
		private System.Windows.Forms.Timer? recordingTimer = null;
		private DateTime _infoCtrlToStopAppeared = DateTime.MinValue;

		// Copy + Paste AudioObj
		internal static AudioObj? ClipboardAudioObj = null;


		public WindowMain()
		{
			Instance = this;
			this.InitializeComponent();
			this.StartPosition = FormStartPosition.Manual;
			this.Location = WindowsScreenHelper.GetCornerPosition(this, false, true);

			this.Register_ListBox_Log();
			TrackViews.ListChanged += this.TrackViews_ListChanged;
			CollectionViews.ListChanged += this.CollectionViews_ListChanged;

			this.FormClosing += this.WindowMain_FormClosing;
			this.LocationChanged += (_, __) => this.PositionCollectionViews();
			this.SizeChanged += (_, __) => this.PositionCollectionViews();

			this.AllowDrop = true;
			this.DragEnter += this.WindowMain_DragEnter;
			this.DragDrop += this.WindowMain_DragDrop;

			this.textBox_scanBpmResult.DoubleClick += this.textBox_scanBpmResult_DoubleClick;

			this.InitializeExportControls();
			UpdateTrackDependentUI();
		}

		private async void WindowMain_FormClosing(object? sender, FormClosingEventArgs e)
		{
			// Kinder schliessen (deren FormClosing Handler lassen App-Exit jetzt durch)
			foreach (var cv in CollectionViews.ToList())
			{
				cv.Close();
			}
			foreach (var tv in TrackViews.ToList())
			{
				tv.Close();
			}

			await this.AudioC.ClearAsync();
			// Nicht abbrechen, echte Beendigung
			// e.Cancel bleibt false
			Application.ExitThread();
			Environment.Exit(0); // harte Prozessbeendigung falls noch Threads leben
		}

		private void Register_ListBox_Log()
		{
			this.listBox_log.Items.Clear();
			this.listBox_log.DataSource = LogCollection.Logs;

			// Add horizontal scrollbar
			this.listBox_log.HorizontalScrollbar = true;

			// Scroll auto to latest entry
			LogCollection.Logs.ListChanged += (s, e) =>
			{
				if (LogCollection.AutoScroll && e.ListChangedType == ListChangedType.ItemAdded)
				{
					InvokeIfRequired(() =>
					{
						try
						{
							this.listBox_log.TopIndex = LogCollection.Logs.Count - 1;
						}
						catch { }
					});
				}
			};

			// Double click to copy to clipboard
			this.listBox_log.DoubleClick += (s, e) =>
			{
				if (this.listBox_log.SelectedItem is string selectedLog)
				{
					Clipboard.SetText(selectedLog);
				}
			};
		}


		private async void button_import_Click(object sender, EventArgs e)
		{
			IEnumerable<string> filesToImport = [];
			bool fromResources = false;

			if (ModifierKeys.HasFlag(Keys.Shift))
			{
				// FBD from Resources
				using FolderBrowserDialog folderBrowserDialog = new()
				{
					Description = "Select Resource Folder to Import Audio Files From",
					SelectedPath = this.lastImportFolder,
					ShowNewFolderButton = false
				};

				if (folderBrowserDialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

				this.lastImportFolder = folderBrowserDialog.SelectedPath;

				try
				{
					filesToImport = Directory
						.EnumerateFiles(folderBrowserDialog.SelectedPath, "*.*", SearchOption.AllDirectories)
						.Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)));
				}
				catch (Exception ex)
				{
					LogCollection.Log($"Failed to scan resources at '{folderBrowserDialog.SelectedPath}': {ex.Message}");
					return;
				}
			}
			else if (ModifierKeys.HasFlag(Keys.Alt))
			{
				string? resourceFile = TryGetRandomResourceFile();
				if (resourceFile == null)
				{
					LogCollection.Log("No resource audio files found for import.");
					return;
				}
				filesToImport = [resourceFile];
				fromResources = true;
			}
			else
			{
				string initialDir = this.AudioC.ImportDirectory;

                // Set initial dir to Resources if ctrl own
				if (ModifierKeys.HasFlag(Keys.Control))
                {
					initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
					LogCollection.Log("Import: Using Resources folder as initial directory.");
                }

				using OpenFileDialog openFileDialog = new()
				{
					InitialDirectory = initialDir,
					Filter = "Audio File|*.wav;*.mp3;*.flac",
					Multiselect = true,
					Title = "Import Audio Files / Loops",
					RestoreDirectory = true
				};
				if (openFileDialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}
				filesToImport = openFileDialog.FileNames;
			}

			await this.ImportAndPlaceAsync(filesToImport, fromResources);
		}

		private static string? TryGetRandomResourceFile()
		{
			var candidates = new List<string>();
			foreach (var root in EnumerateResourceRoots())
			{
				if (!Directory.Exists(root))
				{
					continue;
				}

				try
				{
					candidates.AddRange(Directory
						.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
						.Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f))));
				}
				catch (Exception ex)
				{
					LogCollection.Log($"Failed to scan resources at '{root}': {ex.Message}");
				}
			}

			if (candidates.Count == 0)
			{
				return null;
			}

			lock (ResourceRandom)
			{
				return candidates[ResourceRandom.Next(candidates.Count)];
			}
		}


		private static IEnumerable<string> EnumerateResourceRoots()
		{
			DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
			var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			while (current != null)
			{
				string candidate = Path.Combine(current.FullName, "Resources");
				if (yielded.Add(candidate))
				{
					yield return candidate;
				}
				current = current.Parent;
			}
		}

		private void WindowMain_DragEnter(object? sender, DragEventArgs e)
		{
			try
			{
				if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
				{
					var items = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
					if (items.Any(p => !string.IsNullOrWhiteSpace(p) &&
						(Directory.Exists(p) || AllowedImportExtensions.Contains(Path.GetExtension(p)))))
					{
						e.Effect = DragDropEffects.Copy;
						return;
					}
				}
			}
			catch { }

			e.Effect = DragDropEffects.None;
		}

		private async void WindowMain_DragDrop(object? sender, DragEventArgs e)
		{
			if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				LogCollection.Log("DragDrop: no FileDrop data.");
				return;
			}

			string[] dropped;
			try
			{
				dropped = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
			}
			catch (Exception ex)
			{
				LogCollection.Log($"DragDrop: failed to read dropped data: {ex.Message}");
				return;
			}

			LogCollection.Log($"DragDrop: {dropped.Length} item(s) dropped.");
			var candidates = dropped.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
			LogCollection.Log("Dropped (sample): " + string.Join(", ", candidates.Take(10)));

			// Expand directories (recursive) and collect allowed files
			var collectedPaths = new List<string>();
			foreach (var path in candidates)
			{
				try
				{
					if (Directory.Exists(path))
					{
						var found = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
							.Where(f => AllowedImportExtensions.Contains(Path.GetExtension(f)));
						collectedPaths.AddRange(found);
					}
					else if (File.Exists(path) && AllowedImportExtensions.Contains(Path.GetExtension(path)))
					{
						collectedPaths.Add(path);
					}
				}
				catch (Exception ex)
				{
					LogCollection.Log($"DragDrop: error scanning '{path}': {ex.Message}");
				}
			}

			var validPaths = collectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (validPaths.Count == 0)
			{
				LogCollection.Log("DragDrop: No allowed audio files found in drop.");
				return;
			}

			try
			{
				this.lastImportFolder = Path.GetDirectoryName(validPaths[0]) ?? this.lastImportFolder;
			}
			catch { }

			// Lade jede Datei parallel (je Task = LoadAsync)
			var loadTasks = new List<Task<AudioObj?>>();
			foreach (var file in validPaths)
			{
				try
				{
					loadTasks.Add(this.AudioC.LoadAsync(file));
				}
				catch (Exception ex)
				{
					LogCollection.Log($"DragDrop: enqueue LoadAsync failed for '{file}': {ex.Message}");
					loadTasks.Add(Task.FromResult<AudioObj?>(null));
				}
			}

			AudioObj?[] loadedResults;
			try
			{
				// Absichtlich ohne ConfigureAwait(false): Fortsetzung soll auf UI-Thread laufen
				loadedResults = await Task.WhenAll(loadTasks);
			}
			catch (Exception ex)
			{
				LogCollection.Log($"DragDrop: Task.WhenAll failed: {ex.Message}");
				loadedResults = loadTasks.Select(t => t.IsCompletedSuccessfully ? t.Result : null).ToArray();
			}

			// Pair file path with created AudioObj
			var pairs = validPaths
				.Select((p, i) => new { Path = Path.GetFullPath(p), Audio = i < loadedResults.Length ? loadedResults[i] : null })
				.Where(x => x.Audio != null)
				.ToList();

			var importedCount = pairs.Count;
			LogCollection.Log($"DragDrop: {importedCount} audio object(s) created.");
			if (importedCount == 0)
			{
				LogCollection.Log("DragDrop: no files could be loaded (all loads failed).");
				return;
			}

			// UI-Operationen auf UI-Thread: Views erzeugen / zeigen / positionieren
			InvokeIfRequired(() =>
			{
				bool prevSuppress = SuppressCollectionViewPositioning;
				SuppressCollectionViewPositioning = true;
				try
				{
					if (this.StructuredImports)
					{
						// Gruppiere nach Verzeichnis und für jede Gruppe eine View erstellen
						var groups = pairs.GroupBy(x => Path.GetDirectoryName(x.Path) ?? string.Empty);
						foreach (var g in groups)
						{
							var audios = g.Select(x => x.Audio!).ToList();
							if (audios.Count == 0)
							{
								continue;
							}

							var newView = new AudioCollectionView(audios);
							// Setze Name auf Ordnername (Fallback: Voller Pfad)
							string folderName = Path.GetFileName(g.Key);
							if (string.IsNullOrWhiteSpace(folderName))
							{
								folderName = g.Key;
							}

							try { newView.Rename(folderName); } catch { }

							int num = GetCollectionNumber(newView);
							foreach (var audio in audios)
							{
								AudioCollectionTags[audio.Id] = num;
							}

							newView.Show();
						}
					}
					else
					{
						// Bisheriges Verhalten: alle Audios in eine View / vorhandene leere View
						var audioList = pairs.Select(x => x.Audio!).ToList();
						var last = CollectionViews.LastOrDefault();
						if (last == null)
						{
							var newView = new AudioCollectionView(audioList);
							int num = GetCollectionNumber(newView);
							foreach (var audio in audioList)
							{
								AudioCollectionTags[audio.Id] = num;
							}

							newView.Show();
						}
						else if (last.AudioCount == 0)
						{
							int num = GetCollectionNumber(last);
							foreach (var audio in audioList)
							{
								last.AudioC.Audios.Add(audio);
								AudioCollectionTags[audio.Id] = num;
							}
							last.Show();
						}
						else
						{
							var newView = new AudioCollectionView(audioList);
							int num = GetCollectionNumber(newView);
							foreach (var audio in audioList)
							{
								AudioCollectionTags[audio.Id] = num;
							}

							newView.Show();
						}
					}
				}
				finally
				{
					SuppressCollectionViewPositioning = prevSuppress;
				}

				// Einmalige, sichere Neupositionierung
				try { this.PositionCollectionViews(); } catch { }
			});

			// Entfernen aus temporärer Sammlung ohne Dispose
			try
			{
				foreach (var p in pairs)
				{
					this.AudioC.Audios.Remove(p.Audio!);
				}
			}
			catch { }
		}

		private async Task ImportAndPlaceAsync(IEnumerable<string> filePaths, bool fromResources)
		{
			// Laden und Liste neu importierter Audios bestimmen ohne spätere Dispose
			var validPaths = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
			if (validPaths.Count == 0)
			{
				return;
			}

			// Lade alle Dateien (LoadManyAsync gibt AudioObj?[] zurück)
			var loaded = await this.AudioC.LoadManyAsync(validPaths);
			// Pair paths mit Ergebnissen
			var pairs = validPaths
				.Select((p, i) => new { Path = Path.GetFullPath(p), Audio = i < loaded.Count() ? loaded.ElementAt(i) : null })
				.Where(x => x.Audio != null)
				.ToList();

			var importedAudios = pairs.Select(x => x.Audio!).ToList();
			if (importedAudios.Count == 0)
			{
				return;
			}

			// Logging nur für neu importierte
			foreach (var audio in importedAudios)
			{
				LogCollection.Log(fromResources ? $"{audio.Name} imported from resources." : $"{audio.Name} imported.");
			}

			// Wenn StructuredImports aktiv: gruppiere nach Verzeichnis und erstelle pro Ordner eine View
			if (this.StructuredImports)
			{
				InvokeIfRequired(() =>
				{
					bool prevSuppress = SuppressCollectionViewPositioning;
					SuppressCollectionViewPositioning = true;
					try
					{
						var groups = pairs.GroupBy(x => Path.GetDirectoryName(x.Path) ?? string.Empty);
						foreach (var g in groups)
						{
							var audios = g.Select(x => x.Audio!).ToList();
							if (audios.Count == 0)
							{
								continue;
							}

							var newView = new AudioCollectionView(audios);
							string folderName = Path.GetFileName(g.Key);
							if (string.IsNullOrWhiteSpace(folderName))
							{
								folderName = g.Key;
							}

							try { newView.Rename(folderName); } catch { }

							int num = GetCollectionNumber(newView);
							foreach (var audio in audios)
							{
								AudioCollectionTags[audio.Id] = num;
							}

							newView.Show();
						}
					}
					finally
					{
						SuppressCollectionViewPositioning = prevSuppress;
					}

					try { this.PositionCollectionViews(); } catch { }
				});
			}
			else
			{
				// Single-batch wie vorher: in bestehende leere View oder neue View
				InvokeIfRequired(() =>
				{
					var last = CollectionViews.LastOrDefault();
					if (last == null)
					{
						var newView = new AudioCollectionView(importedAudios);
						int num = GetCollectionNumber(newView);
						foreach (var audio in importedAudios)
						{
							AudioCollectionTags[audio.Id] = num;
						}
						newView.Show();
					}
					else if (last.AudioCount == 0)
					{
						int num = GetCollectionNumber(last);
						foreach (var audio in importedAudios)
						{
							last.AudioC.Audios.Add(audio);
							AudioCollectionTags[audio.Id] = num;
						}
						last.Show();
					}
					else
					{
						var newView = new AudioCollectionView(importedAudios);
						int num = GetCollectionNumber(newView);
						foreach (var audio in importedAudios)
						{
							AudioCollectionTags[audio.Id] = num;
						}
						newView.Show();
					}

					try { this.PositionCollectionViews(); } catch { }
				});
			}

			// Entfernen aus temporärer Sammlung ohne Dispose (damit Views gültige Objekte behalten)
			foreach (var audio in importedAudios)
			{
				this.AudioC.Audios.Remove(audio);
			}
		}

		private void InitializeExportControls()
		{
			var orderedFormats = AudioExporter.AvailableExportFormats.Keys
				.OrderBy(f => f.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
				.ToList();

			this.comboBox_exportFormat.BeginUpdate();
			this.comboBox_exportFormat.Items.Clear();
			foreach (var format in orderedFormats)
			{
				this.comboBox_exportFormat.Items.Add(format);
			}
			this.comboBox_exportFormat.EndUpdate();

			if (orderedFormats.Count == 0)
			{
				this.comboBox_exportFormat.SelectedIndex = -1;
				this.comboBox_exportBits.Items.Clear();
				this.comboBox_exportBits.SelectedIndex = -1;
				return;
			}

			string defaultFormat = orderedFormats.FirstOrDefault(f => f.Equals(".wav", StringComparison.OrdinalIgnoreCase)) ?? orderedFormats[0];

			this.suppressExportFormatEvent = true;
			this.comboBox_exportFormat.SelectedItem = defaultFormat;
			this.suppressExportFormatEvent = false;

			this.UpdateExportBitOptions(selectMiddleOnChange: true);
		}

		private void UpdateExportBitOptions(bool selectMiddleOnChange = false)
		{
			if (AudioExporter.AvailableExportFormats.Count == 0)
			{
				this.comboBox_exportBits.Items.Clear();
				this.comboBox_exportBits.SelectedIndex = -1;
				return;
			}

			string? selectedFormat = this.comboBox_exportFormat.SelectedItem as string;
			if (string.IsNullOrWhiteSpace(selectedFormat) || !AudioExporter.AvailableExportFormats.ContainsKey(selectedFormat))
			{
				string fallback = AudioExporter.AvailableExportFormats.Keys.First();
				this.suppressExportFormatEvent = true;
				this.comboBox_exportFormat.SelectedItem = fallback;
				this.suppressExportFormatEvent = false;
				selectedFormat = fallback;
			}

			if (!AudioExporter.AvailableExportFormats.TryGetValue(selectedFormat!, out var bitOptions) || bitOptions.Length == 0)
			{
				this.comboBox_exportBits.Items.Clear();
				this.comboBox_exportBits.SelectedIndex = -1;
				return;
			}

			int? preferredBit = null;
			int middleIndex = Math.Clamp(bitOptions.Length / 2, 0, bitOptions.Length - 1);
			int middleBit = bitOptions[middleIndex];
			if (!selectMiddleOnChange && this.comboBox_exportBits.SelectedItem is int existing && bitOptions.Contains(existing))
			{
				preferredBit = existing;
			}
			else
			{
				preferredBit = middleBit;
			}

			this.comboBox_exportBits.BeginUpdate();
			this.comboBox_exportBits.Items.Clear();
			this.comboBox_exportBits.Items.AddRange(bitOptions.Cast<object>().ToArray());
			this.comboBox_exportBits.EndUpdate();

			if (preferredBit.HasValue)
			{
				this.comboBox_exportBits.SelectedItem = preferredBit.Value;
			}
		}

		private void button_browse_Click(object sender, EventArgs e)
		{
			string workingDir = this.AudioC.WorkingDirectory;
			if (!Directory.Exists(workingDir))
			{
				// FBD to select working directory
				using FolderBrowserDialog folderBrowserDialog = new()
				{
					Description = "Select Working Directory for Audio Files",
					SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					ShowNewFolderButton = true
				};
				if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
				{
					workingDir = folderBrowserDialog.SelectedPath;
					this.AudioC.WorkingDirectory = workingDir;
				}
			}
			else
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
				{
					FileName = workingDir,
					UseShellExecute = true,
					Verb = "open"
				});
			}
		}



		private void TrackViews_ListChanged(object? sender, ListChangedEventArgs e)
		{
			if (e.ListChangedType == ListChangedType.ItemAdded)
			{
				this.ReflowTrackViews();
				TrackView newTrackView = TrackViews[e.NewIndex];
				newTrackView.Show();
			}
			else if (e.ListChangedType == ListChangedType.ItemDeleted || e.ListChangedType == ListChangedType.Reset)
			{
				this.ReflowTrackViews();
			}
		}

		private void ReflowTrackViews()
		{
			if (TrackViews.Count == 0)
			{
				return;
			}

			Rectangle workingArea = Screen.FromControl(this).WorkingArea;
			int currentX = workingArea.Left;
			int currentY = workingArea.Top;
			int columnWidth = 0;
			int maxRight = workingArea.Right - TrackViewScreenMargin.Right;
			int maxBottom = workingArea.Bottom - TrackViewScreenMargin.Bottom;

			TrackView? lastPlaced = null;
			foreach (var view in TrackViews.Where(tv => tv != null && !tv.IsDisposed))
			{
				if (view == null || view.IsDisposed)
				{
					continue;
				}

				view.StartPosition = FormStartPosition.Manual;

				view.Location = new Point(currentX, currentY);

				// Merken, welches View zuletzt platziert wurde
				lastPlaced = view;

				// Breiteste View in dieser Spalte merken (falls wir auf die klassische Spalten-Logik zurückfallen)
				columnWidth = Math.Max(columnWidth, view.Width);

				// Nächstes Y vorbereiten (gleiches Verhalten wie vorher; erstes Item hat keinen extra margin)
				currentY += view.Height + TrackViewSpacing.Height;

				bool exceedsBottom = currentY + view.Height > maxBottom;
				if (exceedsBottom)
				{
					// Wenn möglich: neue Spalte direkt rechts neben der zuletzt platzierten View beginnen
					if (lastPlaced != null)
					{
						int margin = TrackViewSpacing.Width; // kleines Margin zwischen Spalten
						int proposedX = lastPlaced.Location.X + lastPlaced.Width + margin;

						// Falls proposedX zu weit rechts wäre, auf linken Rand zurückfallen
						if (proposedX + view.Width > maxRight)
						{
							proposedX = workingArea.Left;
						}

						currentX = proposedX;
					}
					else
					{
						// Fallback: klassische Spalten-Logik
						currentX += columnWidth + TrackViewSpacing.Width;
						if (currentX + view.Width > maxRight)
						{
							currentX = workingArea.Left;
						}
					}

					// Neue Spalte von oben beginnen
					currentY = workingArea.Top;
					columnWidth = 0;
				}
			}
		}

		private void CollectionViews_ListChanged(object? sender, ListChangedEventArgs e)
		{
			if (e.ListChangedType == ListChangedType.ItemAdded)
			{
				var addedView = CollectionViews[e.NewIndex];
				if (addedView == null || addedView.IsDisposed)
				{
					return;
				}

				// Wenn bereits Handle vorhanden: sofort diese View positionieren
				if (addedView.IsHandleCreated)
				{
					try { this.PositionCollectionView(addedView); } catch { }
					return;
				}

				// Sonst: einmalig positionieren, sobald Handle erstellt wird
				EventHandler? handleCreatedHandler = null;
				handleCreatedHandler = (s, ea) =>
				{
					try
					{
						addedView.HandleCreated -= handleCreatedHandler;
						if (!addedView.IsDisposed)
						{
							this.PositionCollectionView(addedView);
						}
					}
					catch { }
				};
				addedView.HandleCreated += handleCreatedHandler;

				// Zusätzlich Fallback: bei Shown nochmal sicherstellen
				EventHandler? shownHandler = null;
				shownHandler = (s, ea) =>
				{
					try
					{
						addedView.Shown -= shownHandler;
						if (!addedView.IsDisposed)
						{
							this.PositionCollectionView(addedView);
						}
					}
					catch { }
				};
				addedView.Shown += shownHandler;
			}
			else
			{
				// keine Änderung erforderlich
			}
		}

		private void checkBox_singleCollection_CheckedChanged(object? sender, EventArgs e)
		{
			/*if (this.checkBox_singleCollection.Checked)
            {
                this.MergeCollectionsToSingle();
            }
            else
            {
                this.RebuildCollectionsFromTags();
            }*/
		}



		private async void button_scanBpm_Click(object sender, EventArgs e)
		{
			if (LastSelectedTrackView == null)
			{
				return;
			}

			double scannedBpm = await BeatScanner.ScanBpmAsync(LastSelectedTrackView.OriginalAudio);

			this.textBox_scanBpmResult.Text = scannedBpm.ToString("F3") + " BPM";
			LastSelectedTrackView.OriginalAudio.ScannedBpm = (float) scannedBpm;
		}

		private async void button_scanTiming_Click(object sender, EventArgs e)
		{
			if (LastSelectedTrackView == null)
			{
				return;
			}

			float scannedTiming = await BeatScanner_V2.ScanTimingAsync(LastSelectedTrackView.OriginalAudio);

			this.textBox_scanTimingResult.Text = GetTimingString(scannedTiming);
			LastSelectedTrackView.OriginalAudio.ScannedTiming = scannedTiming;
		}

		private async void button_scanKey_Click(object sender, EventArgs e)
		{
			if (LastSelectedTrackView == null)
			{
				return;
			}

			string scannedKey = await BeatScanner_V2.ScanKeyAsync(LastSelectedTrackView.OriginalAudio);

			this.textBox_scanKeyResult.Text = scannedKey;
			LastSelectedTrackView.OriginalAudio.ScannedKey = scannedKey;
		}

		private void textBox_scanBpmResult_DoubleClick(object? sender, EventArgs e)
		{
			if (LastSelectedTrackView == null)
			{
				return;
			}

			// Editiermodus aktivieren
			this.textBox_scanBpmResult.ReadOnly = false;
			this.textBox_scanBpmResult.Text = LastSelectedTrackView.OriginalAudio.ScannedBpm > 0
				? LastSelectedTrackView.OriginalAudio.ScannedBpm.ToString("0.###")
				: "";
			this.textBox_scanBpmResult.Focus();
			this.textBox_scanBpmResult.SelectAll();

			// Event-Handler nur einmal anhängen
			this.textBox_scanBpmResult.Leave -= this.TextBox_scanBpmResult_LeaveOrEndEdit;
			this.textBox_scanBpmResult.KeyDown -= this.TextBox_scanBpmResult_KeyDown;
			this.textBox_scanBpmResult.Leave += this.TextBox_scanBpmResult_LeaveOrEndEdit;
			this.textBox_scanBpmResult.KeyDown += this.TextBox_scanBpmResult_KeyDown;
			this.textBox_scanBpmResult.TabStop = false;
			this.textBox_scanBpmResult.ReadOnly = true;
		}

		private void TextBox_scanBpmResult_LeaveOrEndEdit(object? sender, EventArgs e)
		{
			this.ApplyBpmEditAndReset();
		}

		private void TextBox_scanBpmResult_KeyDown(object? sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				this.ApplyBpmEditAndReset();
			}
			else if (e.KeyCode == Keys.Escape)
			{
				// Abbrechen, Wert zurücksetzen
				this.ResetBpmEdit();
			}
		}


		private void ApplyBpmEditAndReset()
		{
			if (LastSelectedTrackView == null)
			{
				return;
			}

			string input = this.textBox_scanBpmResult.Text.Trim();
			input = input.Replace(',', '.'); // Komma zu Punkt für Parsing

			if (float.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bpm) && bpm > 0)
			{
				LastSelectedTrackView.OriginalAudio.ScannedBpm = bpm;
				this.textBox_scanBpmResult.Text = bpm.ToString("0.###") + " BPM";
			}
			else
			{
				// Bei ungültigem oder leerem Wert: alten Wert anzeigen
				float oldBpm = LastSelectedTrackView.OriginalAudio.ScannedBpm;
				this.textBox_scanBpmResult.Text = oldBpm > 0 ? oldBpm.ToString("0.###") + " BPM" : "";
			}

			this.textBox_scanBpmResult.ReadOnly = true;
			this.textBox_scanBpmResult.Leave -= this.TextBox_scanBpmResult_LeaveOrEndEdit;
			this.textBox_scanBpmResult.KeyDown -= this.TextBox_scanBpmResult_KeyDown;
		}

		private void ResetBpmEdit()
		{
			if (LastSelectedTrackView == null)
			{
				return;
			}

			float oldBpm = LastSelectedTrackView.OriginalAudio.ScannedBpm;
			this.textBox_scanBpmResult.Text = oldBpm > 0 ? oldBpm.ToString("0.###") + " BPM" : "";
			this.textBox_scanBpmResult.ReadOnly = true;
			this.textBox_scanBpmResult.Leave -= this.TextBox_scanBpmResult_LeaveOrEndEdit;
			this.textBox_scanBpmResult.KeyDown -= this.TextBox_scanBpmResult_KeyDown;
		}

		
		
		private static int GetCollectionNumber(AudioCollectionView view)
		{
			try
			{
				var text = view.Text;
				int idx = text.LastIndexOf('#');
				if (idx >= 0 && idx + 3 <= text.Length)
				{
					var numStr = text.Substring(idx + 1, 2);
					if (int.TryParse(numStr, out int num))
					{
						return num;
					}
				}
			}
			catch { }
			// Fallback: 1-basierter Index
			int index = CollectionViews.ToList().IndexOf(view);
			return index >= 0 ? index + 1 : 1;
		}

		private void MergeCollectionsToSingle()
		{
			if (CollectionViews.Count == 0)
			{
				return;
			}

			// Alle aktuellen Tags nach Nummer des Views setzen
			foreach (var cv in CollectionViews)
			{
				int num = GetCollectionNumber(cv);
				foreach (var audio in cv.AudioC.Audios.ToList())
				{
					AudioCollectionTags[audio.Id] = num;
				}
			}

			var baseView = CollectionViews[0];

			for (int i = 1; i < CollectionViews.Count; i++)
			{
				var cv = CollectionViews[i];
				var toMove = cv.AudioC.Audios.ToList();
				foreach (var audio in toMove)
				{
					baseView.AudioC.Audios.Add(audio);
				}
				cv.AudioC.Audios.Clear();
				cv.Hide();
			}

			baseView.Show();
			this.PositionCollectionViews();
		}

		private void RebuildCollectionsFromTags()
		{
			if (CollectionViews.Count == 0)
			{
				return;
			}

			// Alle Audios einsammeln
			var allAudios = new List<AudioObj>();
			foreach (var cv in CollectionViews)
			{
				allAudios.AddRange(cv.AudioC.Audios.ToList());
			}

			// Max Nummer bestimmen
			int maxNum = AudioCollectionTags.Count > 0 ? AudioCollectionTags.Values.Max() : 1;
			if (maxNum < 1)
			{
				maxNum = 1;
			}

			// Genug Views bereitstellen
			while (CollectionViews.Count < maxNum)
			{
				var emptyView = new AudioCollectionView([]);
			}

			// Alle leeren und ausblenden
			foreach (var cv in CollectionViews)
			{
				cv.AudioC.Audios.Clear();
				cv.Hide();
			}

			// Verteilen gem�� Tags
			foreach (var audio in allAudios)
			{
				int num = 1;
				if (!AudioCollectionTags.TryGetValue(audio.Id, out num))
				{
					num = 1;
				}
				int index = Math.Clamp(num - 1, 0, CollectionViews.Count - 1);
				CollectionViews[index].AudioC.Audios.Add(audio);
			}

			// Anzeigen der Views mit Inhalt
			foreach (var cv in CollectionViews)
			{
				if (cv.AudioC.Audios.Count > 0)
				{
					cv.Show();
				}
			}

			this.PositionCollectionViews();
		}

		private void PositionCollectionViews()
		{
			if (CollectionViews.Count == 0)
			{
				return;
			}

			Point basePoint = new(this.Location.X, this.Location.Y + this.Height + CollectionBaseMargin);
			for (int i = 0; i < CollectionViews.Count; i++)
			{
				var view = CollectionViews[i];
				if (view == null || view.IsDisposed)
				{
					continue;
				}

				view.StartPosition = FormStartPosition.Manual;
				var offset = new Point(CollectionCascadeOffset.Width * i, CollectionCascadeOffset.Height * i);
				var location = new Point(basePoint.X + offset.X, basePoint.Y + offset.Y);
				try
				{
					view.Invoke(() =>
					{
						view.Location = location;
					});
				}
				catch { }
			}
		}

		private void PositionCollectionView(AudioCollectionView? view)
		{
			if (view == null || view.IsDisposed)
			{
				return;
			}

			Rectangle workingArea = Screen.FromControl(this).WorkingArea;
			AudioCollectionView? anchor = null;
			foreach (var existing in CollectionViews)
			{
				if (existing == view)
				{
					break;
				}
				if (existing != null && !existing.IsDisposed)
				{
					anchor = existing;
				}
			}

			Point basePoint = new(this.Location.X, this.Location.Y + this.Height + CollectionBaseMargin);
			Point targetLocation;
			if (anchor == null)
			{
				targetLocation = basePoint;
			}
			else
			{
				targetLocation = new Point(anchor.Location.X + CollectionCascadeOffset.Width, anchor.Location.Y + CollectionCascadeOffset.Height);
				bool exceedsBottom = targetLocation.Y + view.Height > workingArea.Bottom;
				if (exceedsBottom)
				{
					targetLocation = new Point(anchor.Location.X + anchor.Width + CollectionCascadeOffset.Width, basePoint.Y);
				}

				bool exceedsRight = targetLocation.X + view.Width > workingArea.Right;
				if (exceedsRight)
				{
					targetLocation = basePoint;
				}
			}

			view.StartPosition = FormStartPosition.Manual;
			view.Location = targetLocation;
		}

		internal static void UpdateCollectionTag(AudioObj audio, AudioCollectionView targetView)
		{
			if (audio == null || targetView == null)
			{
				return;
			}

			int num = GetCollectionNumber(targetView);
			AudioCollectionTags[audio.Id] = num;
		}

		internal static void UpdateTrackDependentUI()
		{
			if (Instance == null)
			{
				return;
			}

			// Buttons aktivieren/deaktivieren
			Instance.button_scanBpm.Enabled = LastSelectedTrackView != null;
			Instance.button_scanTiming.Enabled = LastSelectedTrackView != null;
			Instance.button_scanKey.Enabled = LastSelectedTrackView != null;
			Instance.button_timeStretch.Enabled = LastSelectedTrackView != null;
			Instance.button_export.Enabled = LastSelectedTrackView != null;
			Instance.comboBox_exportFormat.Enabled = LastSelectedTrackView != null;
			Instance.comboBox_exportBits.Enabled = LastSelectedTrackView != null;
			Instance.button_autoSamples.Enabled = LastSelectedTrackView != null;
			Instance.textBox_info.Text = LastSelectedTrackView != null ? LastSelectedTrackView.OriginalAudio.GetInfoString() : "";

			// Set scanned values to textboxes
			if (LastSelectedTrackView != null)
			{
				Instance.textBox_scanBpmResult.Text = LastSelectedTrackView.OriginalAudio.ScannedBpm > 0 ? $"{LastSelectedTrackView.OriginalAudio.ScannedBpm:F3} BPM" : "";
				Instance.textBox_scanTimingResult.Text = LastSelectedTrackView.OriginalAudio.ScannedTiming > 0.0f ? GetTimingString(LastSelectedTrackView.OriginalAudio.ScannedTiming) : "";
				Instance.textBox_scanKeyResult.Text = !string.IsNullOrEmpty(LastSelectedTrackView.OriginalAudio.ScannedKey) ? LastSelectedTrackView.OriginalAudio.ScannedKey : "";
			}
			else
			{
				Instance.textBox_scanBpmResult.Text = "";
				Instance.textBox_scanTimingResult.Text = "";
				Instance.textBox_scanKeyResult.Text = "";
			}
		}

		private static void HighlightSelectedTrackView()
		{
			LastSelectedTrackView?.HighlightBorder();
			foreach (var tv in TrackViews)
			{
				if (tv != LastSelectedTrackView)
				{
					tv.NormalightBorder();
				}
			}
		}

		internal static void RefreshAllCollectionViews()
		{
			foreach (var cv in CollectionViews)
			{
				cv.RefreshList();
			}

			UpdateTrackDependentUI();
		}

		internal static void InvokeIfRequired(Action action)
		{
			if (Instance == null || Instance.IsDisposed)
			{
				return;
			}

			if (Instance.InvokeRequired)
			{
				try { Instance.BeginInvoke(action); } catch { }
			}
			else
			{
				try { action(); } catch { }
			}
		}

		internal static void UnselectAll(AudioCollectionView? collectionView = null)
		{
			CollectionViews.Where(cv => cv != collectionView).ToList().ForEach(cv => cv.UnselectAll());
		}





		public static string GetTimingString(float timing)
		{
			if (timing <= 0f)
			{
				return "-";
			}

			// Standard: 4/4 als Basis
			// timing = Bruchteil eines Takts (z.B. 0.25 = Viertel, 0.5 = Halbe, 1.0 = Ganze, 0.75 = punktierte Halbe, etc.)
			// Wir suchen Nenner als Potenz von 2 (1, 2, 4, 8, 16, ...)
			int maxDenominator = 64; // bis 1/64-Noten
			for (int denom = 1; denom <= maxDenominator; denom *= 2)
			{
				float num = timing * denom * 4; // 4/4 als Basis
				if (Math.Abs(num - MathF.Round(num)) < 0.0001f)
				{
					int numerator = (int) MathF.Round(num);
					int denominator = denom * 4;
					return $"{numerator} / {denominator}";
				}
			}
			// Fallback: Dezimalwert
			return timing.ToString("0.###");
		}

		private void button_timeStretch_Click(object sender, EventArgs e)
		{
			if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
			{
				MessageBox.Show(this, "No track selected.", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using var dlg = new Modules.Dialogs.TimeStretchDialog(LastSelectedTrackView);
			dlg.ShowDialog(this);

			UpdateTrackDependentUI();
		}

		private async void button_export_Click(object sender, EventArgs e)
		{
			if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
			{
				MessageBox.Show(this, "No track selected.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);
			string formatKey = NormalizeFormatExtension(this.comboBox_exportFormat.SelectedItem as string);
			string normalizedFormat = formatKey.TrimStart('.');
			int bits = ResolveBitSelection(formatKey, this.comboBox_exportBits.SelectedItem);

			string exportFilePath = this.AudioC.ExportPath;
			if (ctrlFlag)
			{
				SaveFileDialog saveFileDialog = new()
				{
					Filter = $"{normalizedFormat.ToUpperInvariant()} files|*{formatKey}",
					FileName = Path.GetFileName(exportFilePath),
					InitialDirectory = Path.GetDirectoryName(exportFilePath) ?? this.AudioC.ExportPath,
					OverwritePrompt = true,
					Title = "Select Export File Location",
					DefaultExt = normalizedFormat
				};

				if (saveFileDialog.ShowDialog() == DialogResult.OK)
				{
					exportFilePath = saveFileDialog.FileName;
				}
				else
				{
					return;
				}
			}

			string? resultPath;
			if (normalizedFormat.Equals("mp3", StringComparison.OrdinalIgnoreCase))
			{
				resultPath = await this.AudioC.Exporter.ExportMp3Async(LastSelectedTrackView.OriginalAudio, bits, Environment.ProcessorCount - 1, exportFilePath);
			}
			else
			{
				resultPath = await this.AudioC.Exporter.ExportWavAsync(LastSelectedTrackView.OriginalAudio, bits, exportFilePath);
			}

			if (string.IsNullOrEmpty(resultPath))
			{
				MessageBox.Show(this, "Export failed.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			else
			{
				MessageBox.Show(this, $"Exported to:\n{resultPath}", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private static string NormalizeFormatExtension(string? formatCandidate)
		{
			if (string.IsNullOrWhiteSpace(formatCandidate))
			{
				return ".wav";
			}

			string normalized = formatCandidate.Trim();
			if (!normalized.StartsWith(".", StringComparison.Ordinal))
			{
				normalized = "." + normalized;
			}

			return normalized.ToLowerInvariant();
		}

		private static int ResolveBitSelection(string formatKey, object? selectedBit)
		{
			if (selectedBit is int bitValue)
			{
				return bitValue;
			}

			if (AudioExporter.AvailableExportFormats.TryGetValue(formatKey, out var bits) && bits.Length > 0)
			{
				if (formatKey.Equals(".wav", StringComparison.OrdinalIgnoreCase) && bits.Contains(24))
				{
					return 24;
				}

				return bits[0];
			}

			var fallback = AudioExporter.AvailableExportFormats.FirstOrDefault();
			if (fallback.Value != null && fallback.Value.Length > 0)
			{
				return fallback.Value[0];
			}

			return 16;
		}

		private void comboBox_exportFormat_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.suppressExportFormatEvent)
			{
				return;
			}

			this.UpdateExportBitOptions(selectMiddleOnChange: true);
		}



		private void button_autoSamples_Click(object sender, EventArgs e)
		{
			if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
			{
				MessageBox.Show(this, "No track selected.", "Auto Samples", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using var dlg = new Modules.Dialogs.AutoSamplesDialog(LastSelectedTrackView.OriginalAudio);
			dlg.ShowDialog(this);

			if (dlg.DialogResult != DialogResult.OK || dlg.ResultSamples.Count == 0)
			{
				return;
			}

			var samples = dlg.ResultSamples.ToList();
			AudioCollectionView collection = new(samples);
			int num = GetCollectionNumber(collection);
			foreach (var audio in samples)
			{
				AudioCollectionTags[audio.Id] = num;
			}
			collection.Rename("Samples [" + LastSelectedTrackView.OriginalAudio.Name + "]");
			collection.Show();
		}

		private void button_newBag_Click(object sender, EventArgs e)
		{
			AudioCollectionView collection = new([]);
			collection.Show();
		}

		private void button_drumRoll_Click(object sender, EventArgs e)
		{
			// New drum roll editor window with all selected tracks
			DrumRollEditor editor = new(SelectedTracks);
			editor.Show();
		}


		

		private async void button_record_Click(object sender, EventArgs e)
		{
			try
			{
				if (!AudioRecorder.IsRecording)
				{
					// Timer anlegen, Event anhängen und starten
					this.recordingTimer = new System.Windows.Forms.Timer { Interval = 500 };
					this.recordingTimer.Tick += async (s, ev) => await this.RecordingTimer_TickAsync();
					this.recordingTimer.Start();

					// Sicherstellen, dass Aufnahme-Ordner existiert
					string recordDir = this.AudioC.RecordPath;
					try { System.IO.Directory.CreateDirectory(recordDir); } catch { }

					// Aufnahme-Dateiname mit .wav-Endung
					string fileName = "Recording" + DateTime.Now.ToString("_yyyyMMdd_HHmmss") + ".wav";
					string fullPath = System.IO.Path.Combine(recordDir, fileName);

					// Aufnahme starten (Dateiname mit Timestamp + .wav)
					await AudioRecorder.StartRecording(fullPath);

					// UI-Status
					this.button_record.ForeColor = Color.Red;
					this.label_stopRecordInfo.Visible = false;
					this._infoCtrlToStopAppeared = DateTime.MinValue;
					this.button_record.Enabled = true; // sicher stellen, dass Button aktiv bleibt beim Aufnehmen
				}
				else
				{
					// Wenn kein Ctrl gedrückt ist: nur Info anzeigen (4s), Aufnahme läuft weiter
					if (!ModifierKeys.HasFlag(Keys.Control))
					{
						this.label_stopRecordInfo.Visible = true;
						this._infoCtrlToStopAppeared = DateTime.Now;
					}
					else
					{
						// Ctrl gedrückt: Aufnahme beenden, Button sofort deaktivieren bis Recorder wirklich gestoppt ist
						this.button_record.Enabled = false;
						AudioRecorder.StopRecording(normalizeOutput: true);

						// Info kurz zeigen bis Timer das Ende erkennt
						this.label_stopRecordInfo.Visible = true;
						this._infoCtrlToStopAppeared = DateTime.Now;
					}
				}
			}
			catch (Exception ex)
			{
				LogCollection.Log($"Recording button error: {ex.Message}");
				try { this.recordingTimer?.Stop(); this.recordingTimer?.Dispose(); } catch { }
				this.recordingTimer = null;
				this.button_record.ForeColor = Color.Black;
				this.label_stopRecordInfo.Visible = false;
				this.button_record.Enabled = true;
			}
		}

		private async Task RecordingTimer_TickAsync()
		{
			// Verbergen des Info-Labels nach 4s (wenn angezeigt)
			if (this.label_stopRecordInfo.Visible && this._infoCtrlToStopAppeared != DateTime.MinValue)
			{
				TimeSpan elapsedSinceInfoShown = DateTime.Now - this._infoCtrlToStopAppeared;
				if (elapsedSinceInfoShown.TotalSeconds >= 4)
				{
					this.label_stopRecordInfo.Visible = false;
					this._infoCtrlToStopAppeared = DateTime.MinValue;
				}
			}

			// Anzeige der Laufzeit aktualisieren
			if (AudioRecorder.IsRecording && AudioRecorder.RecordingTime.HasValue)
			{
				this.textBox_recordingTime.Text = AudioRecorder.RecordingTime.Value.ToString(@"hh\:mm\:ss");
			}
			else
			{
				// Wenn Aufnahme nicht mehr läuft, UI aufräumen und Timer stoppen
				this.textBox_recordingTime.Text = "";
				this.label_stopRecordInfo.Visible = false;

				if (this.recordingTimer != null)
				{
					try
					{
						this.recordingTimer.Stop();
						this.recordingTimer.Dispose();
					}
					catch { }
					this.recordingTimer = null;
				}

				// Button-Farbe zurücksetzen und Button wieder aktivieren
				this.button_record.ForeColor = Color.Black;
				this.button_record.Enabled = true;
			}

			await Task.CompletedTask;
		}



		private void button_newTrack_Click(object sender, EventArgs e)
		{
			var cv = CollectionViews.LastOrDefault();
			if (cv == null)
			{
				cv = new AudioCollectionView([]);
			}

			var track = new AudioObj
			{
				Name = "New Track #" + cv.AudioCount.ToString("D2"),
				SampleRate = 44100,
				Channels = 2,
				BitDepth = 32
			};
			cv.AudioC.Audios.Add(track);
			var trackView = new TrackView(track);
			trackView.Show();
			cv.Show();
		}

		private void button_breakbeatArchitect_Click(object sender, EventArgs e)
		{
			var breakbeatWindow = new Modules.Dialogs.BreakbeatGeneratorDialog(SelectedTracks);	
			breakbeatWindow.Show();
		}

		private void button_pitchShift_Click(object sender, EventArgs e)
		{
			using var dlg = new Modules.Dialogs.PitchShiftDialog(SelectedTracks);
			dlg.ShowDialog(this);
		}

		private async void button_applyCloseAll_Click(object sender, EventArgs e)
		{
			var openTrackViews = TrackViews.Where(tv => tv != null && !tv.IsDisposed).ToList();
			
			var tasks = openTrackViews.Select(async tv =>
			{
				await tv.ApplyTrackAsync(true);
			});

			await Task.WhenAll(tasks);
		}

	}
}









