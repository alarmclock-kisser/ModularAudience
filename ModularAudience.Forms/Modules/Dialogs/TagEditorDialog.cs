using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using TagLib;


namespace ModularAudience.Forms.Modules.Dialogs
{
	public partial class TagEditorDialog : Form
	{
		private readonly List<AudioObj> Audios;

		// Tags, die der User typischerweise bearbeiten soll
		public static readonly string[] EditableTagIds =
		[
			"TT2", // Title
            "TT3", // Subtitle
            "TT1", // Grouping
            "TAL", // Album
            "TP1", // Artist / Performer
            "TP2", // Band / Album Artist
            "TP3", // Conductor
            "TP4", // InterpretedBy
            "TCM", // Composer
            "TCO", // Genre
            "TBP", // BPM
            "TYE", // Year
            "TRK", // Track
            "TPA", // PartOfSet
            "COM", // Comment
            "ULT", // Lyrics
            "TXT", // Lyricist
            "TLA", // Language
            "TKE", // InitialKey
            "TLE", // Length
            "TMT", // Media
            "TOA", // OriginalArtist
            "TOT", // OriginalAlbum
            "TOF", // OriginalFileName
            "TOR", // OriginalReleaseYear
            "TOL", // OriginalLyricist
            "TPB", // Publisher
            "TCR", // Copyright
            "TEN", // EncodedBy
            "TSS", // EncoderSettings
            "TS2", // AlbumArtistSortOrder
            "TSA", // AlbumSortOrder
            "TSC", // ComposerSortOrder
            "TSP", // PerformerSortOrder
            "TST"  // TitleSortOrder
        ];

		public static readonly string[] EditableTagNames =
		[
			"Title",
			"Subtitle",
			"Grouping",
			"Album",
			"Artist",
			"Album Artist",
			"Conductor",
			"Interpreted By",
			"Composer",
			"Genre",
			"Beats Per Minute",
			"Year",
			"Track",
			"Part Of Set",
			"Comment",
			"Lyrics",
			"Lyricist",
			"Language",
			"Initial Key",
			"Length",
			"Media",
			"Original Artist",
			"Original Album",
			"Original File Name",
			"Original Release Year",
			"Original Lyricist",
			"Publisher",
			"Copyright",
			"Encoded By",
			"Encoder Settings",
			"Album Artist Sort Order",
			"Album Sort Order",
			"Composer Sort Order",
			"Performer Sort Order",
			"Title Sort Order"
		];

		// „Meta“-Tags – werden nur bei gesetzter Checkbox angezeigt
		public static readonly string[] MetaTagIds =
		[
			"CNT",   // PlayCounter
            "GP1",   // Grouping (alt)
            "IPL",   // InvolvedPeople
            "ITU",   // iTunesU?
            "MVI",   // MovementNumber
            "MVN",   // MovementName
            "PCS",   // Podcast?
            "PIC",   // Picture frame
            "PIC-1", // PictureFormat
            "PIC-2", // PictureType
            "PIC-3", // PictureDescription
            "POP",   // Popularimeter
            "RVA",   // RelativeVolumeAdjustment
            "SLT",   // SynLyrics
            "TDA",   // Date
            "TDY",   // PlaylistDelay
            "TFT",   // FileType
            "TIM",   // Time
            "TSI",   // Size
            "WAF",   // FileURL
            "WAR",   // ArtistURL
            "WAS",   // SourceURL
            "WCM",   // CommercialURL
            "WCP",   // CopyrightURL
            "WPB",   // PublisherURL
            "WXX"	// UserDefinedURL
        ];

		public static readonly string[] MetaTagNames =
		[
			"Play Counter",
			"Grouping (legacy)",
			"Involved People",
			"iTunes U",
			"Movement Number",
			"Movement Name",
			"Podcast",
			"Picture",
			"Picture Format",
			"Picture Type",
			"Picture Description",
			"Popularimeter",
			"Relative Volume Adjustment",
			"Synched Lyrics",
			"Date",
			"Playlist Delay",
			"File Type",
			"Time",
			"Size",
			"File URL",
			"Artist URL",
			"Source URL",
			"Commercial URL",
			"Copyright URL",
			"Publisher URL",
			"User Defined URL"
		];

		// Tags, die bei mehreren Files NICHT angeboten werden sollen
		public static readonly string[] UniquePerFileTagIds =
		[
			"TRK", // Track number
            "TPA", // Part of set
            "TOF", // Original file name
            "TRC", // ISRC
        ];

		public static readonly string[] UniquePerFileTagNames =
		[
			"Track",
			"Part Of Set",
			"Original File Name",
			"ISRC"
		];

		// internes Modell der UI-Items
		private sealed class TagEntry
		{
			public string Id { get; }
			public string Name { get; }
			public string? OriginalValue { get; }
			public string? CurrentValue { get; set; }
			public bool IsMeta { get; }

			public TagEntry(string id, string name, string? originalValue, bool isMeta)
			{
				this.Id = id;
				this.Name = name;
				this.OriginalValue = originalValue;
				this.CurrentValue = originalValue;
				this.IsMeta = isMeta;
			}

			public bool IsModified =>
				!string.Equals(this.OriginalValue ?? string.Empty,
							   this.CurrentValue ?? string.Empty,
							   StringComparison.Ordinal);
		}

		private readonly List<TagEntry> tagEntries = [];
		private readonly Dictionary<string, TagEntry> tagEntriesById =
			new(StringComparer.OrdinalIgnoreCase);

		private string? currentTagIdForEdit;

		public TagEditorDialog(IEnumerable<AudioObj> audios)
		{
			this.InitializeComponent();
			this.Audios = audios?.ToList() ?? [];

			// ContextMenu an ListBox hängen
			this.listBox_tags.ContextMenuStrip = this.contextMenuStrip_tagMenu;
			this.listBox_tags.MouseDown += this.listBox_tags_MouseDown;
			this.toolStripTextBox_value.KeyDown += this.toolStripTextBox_value_KeyDown;

			this.InitializeTagEntries(false);

			this.FormClosing += (s, e) =>
			{
				WindowMain.UpdateTrackDependentUI();
			};
		}

        // ===================== Initialisierung =====================

        private void InitializeTagEntries(bool preserveModifications)
        {
            Dictionary<string, string?>? previousValues = null;

            if (preserveModifications && this.tagEntries.Count > 0)
            {
                previousValues = this.tagEntries.ToDictionary(
                    t => t.Id,
                    t => t.CurrentValue,
                    StringComparer.OrdinalIgnoreCase);
            }

            this.tagEntries.Clear();
            this.tagEntriesById.Clear();
            this.currentTagIdForEdit = null;

            if (this.Audios.Count == 0)
            {
                this.button_write.Enabled = false;
                return;
            }

            var firstAudio = this.Audios[0];
            bool multipleAudios = this.Audios.Count > 1;

            var uniqueIds = new HashSet<string>(UniquePerFileTagIds, StringComparer.OrdinalIgnoreCase);

            // Hier stellen wir sicher, dass wir CustomTags oder Tags aus der Datei einlesen
            TagLib.File? tagFile = null;
            try
            {
                if (!string.IsNullOrEmpty(firstAudio.FilePath) && System.IO.File.Exists(firstAudio.FilePath))
                {
                    tagFile = TagLib.File.Create(firstAudio.FilePath);
                }
            }
            catch
            {
                // Wenn Tag-Lesen fehlschlägt, bleiben OriginalValues einfach null
            }

            // Nun holen wir die Meta-Tags, wenn aktiviert
            if (this.checkBox_metaTags.Checked)
            {
                this.AddEntries(MetaTagIds, MetaTagNames, true);
            }

            // Fügen die Tags für die Haupt-Tags (EditableTagIds) hinzu
            this.AddEntries(EditableTagIds, EditableTagNames, false);

            tagFile?.Dispose();

            // Tags in der ListBox anzeigen
            this.RefreshTagListBox();
            this.UpdateTitle();
        }

        private static string? ReadTagFromFile(TagLib.File file, string id)
		{
			var tag = file.Tag;
			string upper = id.ToUpperInvariant();

			// High-Level Mapping für die üblichen Kandidaten
			switch (upper)
			{
				case "TT2": return tag.Title;
				case "TT3": return tag.Subtitle;
				case "TT1": return tag.Grouping;
				case "TAL": return tag.Album;
				case "TP1":
					return tag.Performers != null && tag.Performers.Length > 0
						? string.Join("; ", tag.Performers)
						: null;
				case "TP2":
					return tag.AlbumArtists != null && tag.AlbumArtists.Length > 0
						? string.Join("; ", tag.AlbumArtists)
						: null;
				case "TP3":
					return tag.Conductor != null && tag.Conductor.Length > 0
						? string.Join("; ", tag.Conductor)
						: null;
				case "TP4":
					return tag.PerformersSort != null && tag.PerformersSort.Length > 0
						? string.Join("; ", tag.PerformersSort)
						: null;
				case "TCM":
					return tag.Composers != null && tag.Composers.Length > 0
						? string.Join("; ", tag.Composers)
						: null;
				case "TCO":
					return tag.Genres != null && tag.Genres.Length > 0
						? string.Join("; ", tag.Genres)
						: null;
				case "COM":
					return tag.Comment;
				case "TYE":
					return tag.Year > 0 ? tag.Year.ToString() : null;
				case "TRK":
					return tag.Track > 0 ? tag.Track.ToString() : null;
				case "TPA":
					return tag.Disc > 0 ? tag.Disc.ToString() : null;
				case "TBP":
					return tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : null;
			}

			// generisches Id3v2-TextFrame als Fallback
			if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
			{
				var id3 = (TagLib.Id3v2.Tag) file.GetTag(TagLib.TagTypes.Id3v2);

				if (upper.Length == 4)
				{
					var frame = TagLib.Id3v2.TextInformationFrame.Get(id3, upper, false);
					if (frame != null && frame.Text != null && frame.Text.Length > 0)
						return string.Join("; ", frame.Text);
				}
				else
				{
					// Fallback: Suche unter vorhandenen TextInformationFrames nach FrameId, die mit 'upper' endet
					var frame = id3.GetFrames()
					              .OfType<TagLib.Id3v2.TextInformationFrame>()
					              .FirstOrDefault(f => f.FrameId.ToString().EndsWith(upper, StringComparison.OrdinalIgnoreCase));
					if (frame != null && frame.Text != null && frame.Text.Length > 0)
						return string.Join("; ", frame.Text);
				}
			}

			return null;
		}

        private void RefreshTagListBox()
        {
            this.listBox_tags.BeginUpdate();
            this.listBox_tags.Items.Clear();

            foreach (var entry in this.tagEntries)
            {
                this.listBox_tags.Items.Add(this.GetDisplayText(entry));
            }

            this.listBox_tags.EndUpdate();
        }

        private string GetDisplayText(TagEntry entry)
        {
            string prefix = entry.IsModified ? "* " : "  "; // Markiert geänderte Tags mit *
            string valuePart = string.IsNullOrWhiteSpace(entry.CurrentValue) ? string.Empty : " = " + entry.CurrentValue;

            return prefix + entry.Name + valuePart;
        }


        private void UpdateTitle()
		{
			int modifiedCount = this.tagEntries.Count(t => t.IsModified);
			this.Text = $"Tag Editor Dialog ({this.Audios.Count})";
		}

		// ===================== Events =====================

		private void listBox_tags_MouseDown(object? sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
			{
				return;
			}

			int index = this.listBox_tags.IndexFromPoint(e.Location);
			if (index >= 0 && index < this.listBox_tags.Items.Count)
			{
				this.listBox_tags.SelectedIndex = index;
				this.currentTagIdForEdit = this.tagEntries[index].Id;
			}
		}

        private void button_write_Click(object sender, EventArgs e)
        {
            if (this.Audios.Count == 0)
            {
                return;
            }

            var modified = this.tagEntries.Where(t => t.IsModified).ToList();
            if (modified.Count == 0)
            {
                // Nichts geändert → einfach schließen
                this.Close();
                return;
            }

            foreach (var audio in this.Audios)
            {
                foreach (var tag in modified)
                {
                    var value = tag.CurrentValue ?? string.Empty;

                    // Temporäre CustomTags aktualisieren
                    audio.CustomTags[tag.Id] = value;

                    // Zusätzlich: falls möglich, direkt ins AudioObj spiegeln
                    ApplyToAudioObjProperties(audio, tag.Id, value);
                }

                // Apply aggregated mappings (BPM, Length, Key, ...) from modified tags to the AudioObj
                ApplyModifiedTagsToAudio(audio, modified);
            }

            // Tags sind jetzt im AudioObj gespeichert, aber noch nicht auf die Files geschrieben.
            this.Close();
        }


        private void checkBox_metaTags_CheckedChanged(object sender, EventArgs e)
		{
			// Meta-Tags ein-/ausblenden, bestehende Eingaben behalten
			this.InitializeTagEntries(true);
		}

		private void resetdefaultToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (this.listBox_tags.SelectedIndex < 0 ||
				this.listBox_tags.SelectedIndex >= this.tagEntries.Count)
			{
				return;
			}

			var entry = this.tagEntries[this.listBox_tags.SelectedIndex];
			entry.CurrentValue = entry.OriginalValue;

			this.RefreshTagListBox();
			this.UpdateTitle();
		}

		private void modifyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (this.listBox_tags.SelectedIndex < 0 ||
				this.listBox_tags.SelectedIndex >= this.tagEntries.Count)
			{
				return;
			}

			var entry = this.tagEntries[this.listBox_tags.SelectedIndex];
			this.currentTagIdForEdit = entry.Id;

			string text;
			if (this.Audios.Count <= 1)
			{
				// Ein einzelnes Audio: ganz normal aktuellen/ursprünglichen Wert nehmen
				text = entry.CurrentValue ?? entry.OriginalValue ?? string.Empty;
			}
			else
			{
				// Mehrere Audios: nur dann vorbefüllen, wenn ALLE denselben Wert haben
				string? common = null;
				bool first = true;

				foreach (var audio in this.Audios)
				{
					var val = this.GetTagValueForAudio(audio, entry.Id);

					if (first)
					{
						common = val;
						first = false;
					}
					else if (!string.Equals(common ?? string.Empty, val ?? string.Empty, StringComparison.Ordinal))
					{
						common = null;
						break;
					}
				}

				text = common ?? string.Empty;
			}

			this.toolStripTextBox_value.Text = text;
			this.toolStripTextBox_value.SelectAll();
			this.toolStripTextBox_value.Focus();

		}

		private void toolStripTextBox_value_Click(object sender, EventArgs e)
		{
			this.toolStripTextBox_value.SelectAll();
		}

		private void toolStripTextBox_value_KeyDown(object? sender, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Enter)
			{
				return;
			}

			if (this.listBox_tags.SelectedIndex >= 0 &&
				this.listBox_tags.SelectedIndex < this.tagEntries.Count)
			{
				var entry = this.tagEntries[this.listBox_tags.SelectedIndex];
				entry.CurrentValue = this.toolStripTextBox_value.Text;
			}

			this.RefreshTagListBox();
			this.UpdateTitle();

			this.contextMenuStrip_tagMenu.Close();

			e.Handled = true;
			e.SuppressKeyPress = true;
		}


		private static void ApplyToAudioObjProperties(AudioObj audio, string tagId, string value)
		{
			if (audio == null)
			{
				return;
			}

			var id = tagId.ToUpperInvariant();

			switch (id)
			{
				case "TT2": // Title -> Name / OriginalName
					if (!string.IsNullOrWhiteSpace(value))
					{
						// wichtig: Rename statt direkte Name-Property
						audio.Rename(value);
					}
					break;

				case "TBP": // BPM
					if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm) ||
						float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out bpm))
					{
						audio.Bpm = bpm;
					}
					break;

					// Falls du später noch andere Mappings willst (z.B. Genre, Artist ...),
					// kannst du sie hier ergänzen.
			}
		}

        // Apply a set of modified tags to an AudioObj's runtime properties (Bpm, Duration/Length, Key, etc.)
        private static void ApplyModifiedTagsToAudio(AudioObj audio, IEnumerable<TagEntry> modifiedTags)
        {
            if (audio == null || modifiedTags == null)
            {
                return;
            }

            foreach (var tag in modifiedTags)
            {
                if (string.IsNullOrWhiteSpace(tag.CurrentValue))
                    continue;

                var id = tag.Id.ToUpperInvariant();
                var val = tag.CurrentValue.Trim();

                try
                {
                    switch (id)
                    {
                        case "TBP": // BPM: handle legacy integer-misplaced decimals (e.g. 13990 -> 139.90)
                        {
                            string norm = val.Replace(',', '.');
                            if (float.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedBpm) ||
                                float.TryParse(norm, NumberStyles.Float, CultureInfo.CurrentCulture, out parsedBpm))
                            {
                                // Heuristic: if value is extremely large (e.g. >1000) it likely misses a decimal/centiplier
                                if (parsedBpm > 1000f)
                                {
                                    parsedBpm = parsedBpm / 100.0f;
                                }

                                audio.Bpm = parsedBpm;
                            }
                        }
                        break;

                        case "TLE": // Length: try to parse TimeSpan (hh:mm:ss, mm:ss) or seconds as number
                        {
                            // Try TimeSpan first
                            if (TimeSpan.TryParse(val, CultureInfo.InvariantCulture, out var ts) || TimeSpan.TryParse(val, CultureInfo.CurrentCulture, out ts))
                            {
                                audio.Duration = ts;
                                if (audio.SampleRate > 0 && audio.Channels > 0)
                                {
                                    long frames = (long)Math.Round(ts.TotalSeconds * audio.SampleRate);
                                    audio.Length = Math.Max(0L, frames * Math.Max(1, audio.Channels));
                                }
                            }
                            else if (double.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                                     double.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
                            {
                                if (seconds > 0)
                                {
                                    var duration = TimeSpan.FromSeconds(seconds);
                                    audio.Duration = duration;
                                    if (audio.SampleRate > 0 && audio.Channels > 0)
                                    {
                                        long frames = (long)Math.Round(duration.TotalSeconds * audio.SampleRate);
                                        audio.Length = Math.Max(0L, frames * Math.Max(1, audio.Channels));
                                    }
                                }
                            }
                        }
                        break;

                        case "TKE": // Initial Key -> map to AudioObj.Key
                            audio.Key = val;
                            break;

                        default:
                            break;
                    }
                }
                catch
                {
                    // best-effort: ignore parse/apply errors for individual tags
                }
            }

            // Ensure UI updates reflecting changed track metadata
            try { WindowMain.UpdateTrackDependentUI(); } catch { }
        }

		private void AddEntries(string[] ids, string[] names, bool isMeta)
		{
            Dictionary<string, string?>? previousValues = null;
            bool multipleAudios = this.Audios.Count > 1; // Flag für mehrere Audios
            var uniqueIds = new HashSet<string>(UniquePerFileTagIds, StringComparer.OrdinalIgnoreCase);

            // Diese Schleife durchläuft alle IDs (Tags), die wir hinzufügen möchten
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];

                // Überprüfen, ob wir mehrere Audios haben und ob die ID in UniquePerFileTagIds enthalten ist
                if (multipleAudios && uniqueIds.Contains(id))
                {
                    continue;
                }

                string name = i < names.Length ? names[i] : id;

                // Versuchen, den Wert aus den CustomTags der Audios zu holen
                string? originalValue = null;
                foreach (var audio in this.Audios)
                {
                    // Hole den Wert aus CustomTags des aktuellen Audioobjekts
                    if (audio.CustomTags != null && audio.CustomTags.Values.ContainsKey(id))
                    {
                        originalValue = audio.CustomTags.Values[id];
                        if (!string.IsNullOrEmpty(originalValue)) break;
                    }
                }

                // Wenn kein temporärer Wert (CustomTag) vorhanden ist, lade den Wert aus der Datei
                if (string.IsNullOrEmpty(originalValue))
                {
                    TagLib.File? tagFile = null;
                    if (!string.IsNullOrEmpty(this.Audios[0].FilePath) && System.IO.File.Exists(this.Audios[0].FilePath))
                    {
                        tagFile = TagLib.File.Create(this.Audios[0].FilePath);
                    }

                    originalValue = tagFile != null ? ReadTagFromFile(tagFile, id) : null;
                }

                // Erstelle das TagEntry mit der gefundenen originalen Wert und weiteren Informationen
                var entry = new TagEntry(id, name, originalValue, isMeta);

                // Falls wir vorher gespeicherte Werte haben (beispielsweise durch das Bearbeiten und Speichern früher), setzen wir den aktuellen Wert
                if (previousValues != null && previousValues.TryGetValue(id, out var prev))
                {
                    entry.CurrentValue = prev;
                }

                // Das TagEntry zur Liste hinzufügen
                this.tagEntries.Add(entry);

                // Speichere das TagEntry in der dictionary (nach Tag-ID geordnet)
                this.tagEntriesById[id] = entry;
            }
        }

        private string? GetTagValueForAudio(AudioObj audio, string id)
        {
            // 1. In-Memory-CustomTags falls vorhanden
            if (audio.CustomTags != null)
            {
                var fromCustom = audio.CustomTags.Values.ContainsKey(id) ? audio.CustomTags.Values[id] : null;
                if (!string.IsNullOrEmpty(fromCustom))
                {
                    return fromCustom;
                }
            }

            // 2. Fallback: direkt aus Datei lesen
            if (string.IsNullOrEmpty(audio.FilePath) || !System.IO.File.Exists(audio.FilePath))
            {
                return null;
            }

            try
            {
                using var f = TagLib.File.Create(audio.FilePath);
                return ReadTagFromFile(f, id);
            }
            catch
            {
                return null;
            }
        }




    }
}
