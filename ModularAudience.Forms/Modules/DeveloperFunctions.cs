using ModularAudience.Audio;
using ModularAudience.Onnx;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    public partial class DeveloperFunctions : Form
    {
        private TrackView? CurrentTrackView => WindowMain.LastSelectedTrackView;
        private AudioObj? SelectedAudio => this.comboBox_track.SelectedIndex <= 0 ? this.CurrentTrackView?.OriginalAudio : WindowMain.TrackViews.Where(tv => !tv.IsDisposed && tv.OriginalAudio != null).Select(tv => tv.OriginalAudio).Where(a => a.Duration > TimeSpan.Zero && a.Data.LongLength > 0).ElementAtOrDefault(this.comboBox_track.SelectedIndex - 1);


        public static IEnumerable<MethodInfo> StaticAudioObjMethods { get; private set; } = ReflectionGetAccessibleMethodsConsuming(typeof(AudioObj));

        internal string? SelectedMethod => this.comboBox_methods.SelectedItem as string;

        private int Threads => (int) this.numericUpDown_maxProcessors.Value;
        private bool ShowAutoParameters => this.checkBox_autoParameters.Checked;
        private bool ShowOptionalParameters => this.checkBox_optionalParameters.Checked;

        public AudioObj? ResultAudioObj { get; private set; } = null;
        public AudioObj? ProcessedAudioObj { get; private set; }

        private bool _isRunning = false;
        private CancellationTokenSource? _cancellationTokenSource = null;
        private CancellationToken? _cancellationToken = null;

        // Timer + Stopwatch für Anzeige der verstrichenen Zeit
        private System.Windows.Forms.Timer? _elapsedTimer = null;
        private Stopwatch? _elapsedStopwatch = null;



        // Mapping ParameterInfo -> Eingabe-Control zur späteren Auslesung
        private readonly Dictionary<ParameterInfo, Control?> _paramControls = [];

        public DeveloperFunctions()
        {
            this.InitializeComponent();

            WindowMain.DeveloperFunctionsWindow = this;

            // Timer initialisieren (0.5s Intervall)
            this._elapsedTimer = new System.Windows.Forms.Timer
            {
                Interval = 500
            };
            this._elapsedTimer.Tick += (s, e) =>
            {
                try
                {
                    if (this._elapsedStopwatch != null)
                    {
                        var ts = this._elapsedStopwatch.Elapsed;
                        // mm:ss Format
                        string txt = $"{ts:mm\\:ss}";
                        if (this.label_elapsedProcessingTime != null)
                        {
                            if (this.label_elapsedProcessingTime.InvokeRequired)
                            {
                                this.label_elapsedProcessingTime.Invoke(() => this.label_elapsedProcessingTime.Text = txt);
                            }
                            else
                            {
                                this.label_elapsedProcessingTime.Text = txt;
                            }
                        }
                    }
                }
                catch { }
            };



            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, true, false, WindowMain.CurrentScreenId);

            this.numericUpDown_maxProcessors.Maximum = Environment.ProcessorCount;
            this.numericUpDown_maxProcessors.Value = Environment.ProcessorCount;
            this.FillComboBoxMethods(this.comboBox_methods);
            this.UpdateControlStates();
            this.comboBox_track.SelectedIndex = -1;
            this.comboBox_track.SelectedIndex = 0;

            this.FormClosing += (s, e) =>
            {
                WindowMain.DeveloperFunctionsWindow = null;
                e.Cancel = true;
                this.Hide();
            };

            this.Show();
        }

        internal void UpdateControlStates()
        {
            this.FillComboBoxTracks(this.comboBox_track);
            var audio = this.ResultAudioObj ?? this.SelectedAudio;

            this.textBox_trackInfo.Text = audio?.GetInfoString() ?? string.Empty;

        }

        // Generic dialog to show arbitrary result entries (Key=>Value).
        // numericPairs: optional parsed int->float pairs when available; if provided, Convert timestamps button will be enabled.
        private void ShowResultEntriesDialog(string title, List<KeyValuePair<object, object>> entries, Dictionary<int, float>? numericPairs, AudioObj? audioForMapping, bool monoBase)
        {
            try
            {
                using var dlg = new Form
                {
                    Text = title,
                    StartPosition = FormStartPosition.CenterParent,
                    Size = new Size(820, 480),
                    MinimizeBox = false,
                    MaximizeBox = false,
                    FormBorderStyle = FormBorderStyle.SizableToolWindow
                };

                var listBox = new ListBox { Dock = DockStyle.Fill, Font = SystemFonts.MessageBoxFont };
                foreach (var kv in entries)
                {
                    listBox.Items.Add($"{kv.Key} => {kv.Value}");
                }

                var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
                var btnCopy = new Button { Text = "Copy", AutoSize = true, Location = new Point(8, 8) };
                var btnConvert = new Button { Text = "Convert timestamps", AutoSize = true, Location = new Point(96, 8), Enabled = numericPairs != null };
                var chkMono = new CheckBox { Text = "Base indices are mono", Checked = monoBase, Location = new Point(260, 12), AutoSize = true };
                var lblInfo = new Label { Text = audioForMapping != null ? $"Map to original: {audioForMapping.SampleRate} Hz, {audioForMapping.Channels} ch" : "No audio metadata (fallback 44100 Hz, 1 ch)", AutoSize = true, Location = new Point(420, 12) };

                btnCopy.Click += (s, e) =>
                {
                    try
                    {
                        var sb = new StringBuilder();
                        foreach (var it in listBox.Items)
                        {
                            sb.AppendLine(it?.ToString());
                        }
                        Clipboard.SetText(sb.ToString());
                    }
                    catch { }
                };

                btnConvert.Click += (s, e) =>
                {
                    try
                    {
                        if (numericPairs == null || numericPairs.Count == 0)
                        {
                            return;
                        }

                        int channels = audioForMapping?.Channels ?? 1;
                        int sampleRate = Math.Max(1, audioForMapping?.SampleRate ?? 44100);
                        double trackDuration = audioForMapping?.Duration.TotalSeconds ?? double.PositiveInfinity;

                        var mapped = new List<string>();
                        foreach (var kv in numericPairs.OrderBy(k => k.Key))
                        {
                            int keyIndex = kv.Key;

                            // Compute both interpretations
                            double secondsInterleaved = keyIndex / (double) channels / (double) sampleRate; // if key is interleaved samples
                            double secondsMono = keyIndex / (double) sampleRate; // if key is mono samples

                            bool useMonoInterpretation;
                            if (double.IsFinite(trackDuration))
                            {
                                // Prefer interpretation that fits inside track duration (with small margin)
                                double margin = 1.0; // 1 second margin
                                bool interleavedFits = secondsInterleaved <= trackDuration + margin;
                                bool monoFits = secondsMono <= trackDuration + margin;

                                if (interleavedFits && !monoFits)
                                {
                                    useMonoInterpretation = false;
                                }
                                else if (!interleavedFits && monoFits)
                                {
                                    useMonoInterpretation = true;
                                }
                                else
                                {
                                    // both fit or both too large -> prefer interleaved (most methods return interleaved indices)
                                    useMonoInterpretation = false;
                                }
                            }
                            else
                            {
                                // No duration available: fall back to checkbox
                                useMonoInterpretation = chkMono.Checked;
                            }

                            int interleavedSamples = useMonoInterpretation && channels > 1 ? Math.Max(0, keyIndex) * channels : keyIndex;
                            double seconds = interleavedSamples / (double) channels / (double) sampleRate;
                            var ts = TimeSpan.FromSeconds(seconds);
                            string formatted = string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2:D3}", (int) ts.TotalMinutes, ts.Seconds, ts.Milliseconds);
                            mapped.Add($"{keyIndex} -> {formatted} (mappedSamples:{interleavedSamples}) => conf:{kv.Value:F6}");
                        }

                        this.ShowResultValueDialog(title + " - timestamps", string.Join(Environment.NewLine, mapped));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error converting timestamps: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                btnPanel.Controls.Add(btnCopy);
                btnPanel.Controls.Add(btnConvert);
                btnPanel.Controls.Add(chkMono);
                btnPanel.Controls.Add(lblInfo);

                dlg.Controls.Add(listBox);
                dlg.Controls.Add(btnPanel);
                dlg.ShowDialog(this);
            }
            catch { }
        }

        private void FillComboBoxMethods(ComboBox comboBox)
        {
            comboBox.SuspendLayout();
            comboBox.Items.Clear();

            string[] methodNames = StaticAudioObjMethods.Select(m => m.Name).ToArray();
            comboBox.Items.AddRange(methodNames);

            comboBox.ResumeLayout();
        }

        private void FillComboBoxTracks(ComboBox comboBox)
        {
            int selectedIndex = comboBox.SelectedIndex;

            comboBox.SuspendLayout();

            comboBox.Items.Clear();
            comboBox.Items.Add("Auto last focussed track");

            var openTracks = WindowMain.TrackViews.Where(tv => !tv.IsDisposed && tv.OriginalAudio != null).Select(tv => tv.OriginalAudio).Where(a => a.Duration > TimeSpan.Zero && a.Data.LongLength > 0).ToList();
            string[] trackNames = openTracks.Select(a => a.OriginalName).ToArray();
            comboBox.Items.AddRange(trackNames);

            comboBox.SelectedIndex = comboBox.Items.Count > selectedIndex ? selectedIndex : 0;

            comboBox.ResumeLayout();
        }

        private void BuildParametersPanel(Panel panel)
        {
            panel.SuspendLayout();
            panel.Controls.Clear();
            this._paramControls.Clear();

            var method = StaticAudioObjMethods.FirstOrDefault(m => m.Name.Equals(this.SelectedMethod, StringComparison.OrdinalIgnoreCase));
            if (method == null)
            {
                panel.ResumeLayout();
                return;
            }

            var parameters = method.GetParameters();

            int y = 6;
            const int rowH = 26;
            const int margin = 6;

            // Verwende die tatsächliche Panel-Breite (ClientSize). Falls 0, Fallback auf 480.
            int totalWidth = panel.ClientSize.Width > 0 ? panel.ClientSize.Width : Math.Max(480, panel.Width);
            // Platz für Scrollbar und Padding abziehen
            int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
            int innerPadding = margin * 3 + scrollBarWidth;

            // Label etwa 33% der verfügbaren Breite, aber mindestens 100px
            int labelW = Math.Max(100, (int) (totalWidth * 0.33));
            // Rest für das Eingabefeld
            int ctrlW = Math.Max(80, totalWidth - labelW - innerPadding);

            // Falls ctrlW zu klein wird, passe labelW entsprechend an
            if (ctrlW < 80)
            {
                ctrlW = 80;
                labelW = Math.Max(60, totalWidth - ctrlW - innerPadding);
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];

                // Optional-Parameter berücksichtigen
                if (!this.ShowOptionalParameters && p.HasDefaultValue)
                {
                    continue;
                }

                var rawType = p.ParameterType;
                var pType = Nullable.GetUnderlyingType(rawType) ?? rawType;

                // Skip generation für Parameter mit "thread" oder "worker" im Namen (case-insensitive).
                if (!string.IsNullOrEmpty(p.Name) &&
                    (p.Name.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.Name.IndexOf("worker", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var lblThread = new Label
                    {
                        Text = $"{p.Name}: (int → automatisch befüllt von numericUpDown_maxProcessors)",
                        Location = new Point(margin, y),
                        Size = new Size(Math.Min(labelW + ctrlW, totalWidth - margin * 2), rowH),
                        AutoEllipsis = true,
                        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    panel.Controls.Add(lblThread);
                    y += rowH;
                    this._paramControls[p] = null; // wird automatisch behandelt
                    continue;
                }

                // Skip generation für CancellationToken-Parameter, stattdessen Hinweis anzeigen
                if (pType == typeof(System.Threading.CancellationToken))
                {
                    var lblCt = new Label
                    {
                        Text = $"{p.Name}: (CancellationToken → Run-Button kann abbrechen)",
                        Location = new Point(margin, y),
                        Size = new Size(Math.Min(labelW + ctrlW, totalWidth - margin * 2), rowH),
                        AutoEllipsis = true,
                        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    panel.Controls.Add(lblCt);
                    y += rowH;
                    this._paramControls[p] = null;
                    continue;
                }

                // Skip generation for AudioObj parameter types
                bool isAudioObjSingle = pType == typeof(AudioObj);
                bool isAudioObjEnumerable = pType == typeof(IEnumerable<AudioObj>) || pType == typeof(AudioObj[]) || pType == typeof(List<AudioObj>) || pType == typeof(ICollection<AudioObj>);

                if (isAudioObjSingle || isAudioObjEnumerable)
                {
                    continue;
                }

                // IProgress<double> parameter: info-Label
                if (pType == typeof(IProgress<double>))
                {
                    var lbl = new Label
                    {
                        Text = $"{p.Name}: (IProgress<double> → ProgressBar)",
                        Location = new Point(margin, y),
                        Size = new Size(Math.Min(labelW + ctrlW, totalWidth - margin * 2), rowH),
                        AutoEllipsis = true,
                        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    panel.Controls.Add(lbl);
                    y += rowH;
                    this._paramControls[p] = null;
                    continue;
                }

                // Label
                var label = new Label
                {
                    Text = $"{p.Name} ({GetFriendlyTypeName(pType)})",
                    Location = new Point(margin, y),
                    Size = new Size(labelW, rowH),
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top
                };
                panel.Controls.Add(label);

                Control inputCtrl;

                int ctrlX = margin + labelW + 6;
                int ctrlInnerWidth = Math.Max(80, ctrlW - 6);
                // Stelle sicher, dass das Control nicht über die Panel-Gesamtbreite hinausgeht
                if (ctrlX + ctrlInnerWidth > totalWidth - margin)
                {
                    ctrlInnerWidth = Math.Max(80, totalWidth - margin - ctrlX);
                }

                // Booleans -> CheckBox
                if (pType == typeof(bool) || pType == typeof(Boolean))
                {
                    var cb = new CheckBox
                    {
                        Location = new Point(ctrlX, y + 3),
                        Size = new Size(Math.Min(16, ctrlInnerWidth), 16),
                        Checked = p.HasDefaultValue && p.DefaultValue is bool bv ? bv : false,
                        Anchor = AnchorStyles.Left | AnchorStyles.Top
                    };
                    inputCtrl = cb;
                }
                // Enum -> ComboBox
                else if (pType.IsEnum)
                {
                    var combo = new ComboBox
                    {
                        Location = new Point(ctrlX, y),
                        Size = new Size(ctrlInnerWidth, rowH),
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
                    };
                    foreach (var name in Enum.GetNames(pType))
                    {
                        combo.Items.Add(name);
                    }
                    if (p.HasDefaultValue && p.DefaultValue != null)
                    {
                        combo.SelectedItem = p.DefaultValue.ToString();
                    }
                    else if (combo.Items.Count > 0)
                    {
                        combo.SelectedIndex = 0;
                    }
                    inputCtrl = combo;
                }
                // Numeric types -> NumericUpDown
                else if (pType == typeof(int) || pType == typeof(long) || pType == typeof(float) || pType == typeof(double) || pType == typeof(decimal))
                {
                    var nud = new NumericUpDown
                    {
                        Location = new Point(ctrlX, y),
                        Size = new Size(ctrlInnerWidth, rowH),
                        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
                    };

                    // defaults
                    decimal min = -1_000_000_000m;
                    decimal max = 1_000_000_000m;
                    decimal increment = 1m;
                    int decimalPlaces = 0;
                    decimal defaultValue = 0m;

                    if (p.HasDefaultValue && p.DefaultValue != null && p.DefaultValue != DBNull.Value)
                    {
                        try
                        {
                            defaultValue = Convert.ToDecimal(p.DefaultValue, CultureInfo.InvariantCulture);
                        }
                        catch { defaultValue = 0m; }
                    }

                    if (pType == typeof(int))
                    {
                        min = int.MinValue;
                        max = int.MaxValue;
                        increment = 1m;
                        decimalPlaces = 0;
                    }
                    else if (pType == typeof(long))
                    {
                        // NumericUpDown arbeitet mit decimal; sichere Grenzen verwenden (long range fits into decimal)
                        min = (decimal) long.MinValue;
                        max = (decimal) long.MaxValue;
                        increment = 1m;
                        decimalPlaces = 0;
                    }
                    else if (pType == typeof(float))
                    {
                        decimalPlaces = 6;
                        increment = 0.001m;
                        min = -1_000_000_000m;
                        max = 1_000_000_000m;
                    }
                    else if (pType == typeof(double))
                    {
                        decimalPlaces = 9;
                        increment = 0.00001m;
                        min = -1_000_000_000m;
                        max = 1_000_000_000m;
                    }
                    else if (pType == typeof(decimal))
                    {
                        decimalPlaces = 6;
                        increment = 0.000001m;
                        min = decimal.MinValue / 1000m;
                        max = decimal.MaxValue / 1000m;
                    }

                    // Setze Minimum/Maximum korrekt (vorher war Math.Min/Max falsch und führte zu 100-Limit)
                    try { nud.Minimum = min; } catch { nud.Minimum = decimal.MinValue; }
                    try { nud.Maximum = max; } catch { nud.Maximum = decimal.MaxValue; }

                    // Set DecimalPlaces & Increment zuletzt
                    nud.DecimalPlaces = decimalPlaces;
                    nud.Increment = increment;

                    try
                    {
                        if (defaultValue < nud.Minimum)
                        {
                            defaultValue = nud.Minimum;
                        }

                        if (defaultValue > nud.Maximum)
                        {
                            defaultValue = nud.Maximum;
                        }

                        nud.Value = defaultValue;
                    }
                    catch
                    {
                        try { nud.Value = Math.Clamp(defaultValue, nud.Minimum, nud.Maximum); } catch { nud.Value = nud.Minimum; }
                    }

                    inputCtrl = nud;
                }
                // Fallback TextBox für andere Typen
                else
                {
                    var txt = new TextBox
                    {
                        Location = new Point(ctrlX, y),
                        Size = new Size(ctrlInnerWidth, rowH),
                        Text = p.HasDefaultValue && p.DefaultValue != null ? Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty,
                        Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
                    };

                    if (p.HasDefaultValue && p.DefaultValue != null && p.DefaultValue != DBNull.Value)
                    {
                        txt.Text = Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? string.Empty;
                    }

                    inputCtrl = txt;
                }

                panel.Controls.Add(inputCtrl);
                this._paramControls[p] = inputCtrl;

                y += rowH;
            }

            panel.VerticalScroll.Enabled = y > panel.Height;
            panel.ResumeLayout();
        }

        private void comboBox_track_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.label_trackName.Text = this.SelectedAudio != null ? $"'{this.SelectedAudio.OriginalName}'" : "No track currently selected.";
        }

        private void comboBox_methods_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Rebuild parameters panel for chosen method
            this.BuildParametersPanel(this.panel_parameters);
        }

        private void checkBox_autoParameters_CheckedChanged(object sender, EventArgs e)
        {
            this.BuildParametersPanel(this.panel_parameters);
        }

        private void checkBox_optionalParameters_CheckedChanged(object sender, EventArgs e)
        {
            this.BuildParametersPanel(this.panel_parameters);
        }

        public async void button_run_Click(object? sender, EventArgs e)
        {
            // Toggle: wenn bereits läuft und Run-Button gedrückt -> Abbruch (falls CT vorhanden)
            if (this._isRunning)
            {
                if (this._cancellationTokenSource != null && !this._cancellationTokenSource.IsCancellationRequested)
                {
                    try { this._cancellationTokenSource.Cancel(); } catch { }
                }
                return;
            }

            var method = StaticAudioObjMethods.FirstOrDefault(m => m.Name.Equals(this.SelectedMethod, StringComparison.OrdinalIgnoreCase));
            if (method == null)
            {
                MessageBox.Show("No method selected.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.ProcessedAudioObj = this.SelectedAudio;

            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            // Prüfe ob Methode einen CancellationToken-Parameter besitzt
            bool hasCancellationParam = parameters.Any(p => (Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType) == typeof(System.Threading.CancellationToken));

            // Reset progress bar & elapsed label
            try
            {
                if (this.progressBar_processing.InvokeRequired)
                {
                    this.progressBar_processing.Invoke(() => this.progressBar_processing.Value = this.progressBar_processing.Minimum);
                }
                else
                {
                    this.progressBar_processing.Value = this.progressBar_processing.Minimum;
                }
            }
            catch { }

            if (this.label_elapsedProcessingTime != null)
            {
                try
                {
                    if (this.label_elapsedProcessingTime.InvokeRequired)
                    {
                        this.label_elapsedProcessingTime.Invoke(() => this.label_elapsedProcessingTime.Text = "00:00");
                    }
                    else
                    {
                        this.label_elapsedProcessingTime.Text = "00:00";
                    }
                }
                catch { }
            }

            // Wenn CT vorhanden, erstelle CTS und halte Button aktiv (Text = Cancel)
            if (hasCancellationParam)
            {
                try
                {
                    this._cancellationTokenSource?.Dispose();
                }
                catch { }

                this._cancellationTokenSource = new System.Threading.CancellationTokenSource();
                this._cancellationToken = this._cancellationTokenSource.Token;
                this.button_run.Text = "Cancel";
                this.button_run.Enabled = true; // bleibt aktiv, damit Cancel gedrückt werden kann
            }
            else
            {
                // keine Abbruchmöglichkeit -> Button deaktivieren während Lauf
                this.button_run.Enabled = false;
            }

            this._isRunning = true;
            this.Cursor = Cursors.WaitCursor;

            // Start stopwatch / timer
            try
            {
                this._elapsedStopwatch?.Reset();
                this._elapsedStopwatch ??= new Stopwatch();
                this._elapsedStopwatch.Start();
                this._elapsedTimer?.Start();
            }
            catch { }

            // Track erstes Audio-Argument (falls vorhanden) damit ResultAudioObj korrekt gesetzt wird
            AudioObj? firstAudioArg = null;

            // Build args in same order as parameters
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pType = p.ParameterType;

                // Thread/Worker-named int parameter -> befülle aus numericUpDown_maxProcessors (Threads property)
                if (!string.IsNullOrEmpty(p.Name) &&
                    (p.Name.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.Name.IndexOf("worker", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (pType == typeof(int) || pType == typeof(System.Int32)))
                {
                    args[i] = this.Threads;
                    continue;
                }

                // CancellationToken param -> verwende erzeugten Token
                if ((Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType) == typeof(System.Threading.CancellationToken))
                {
                    args[i] = this._cancellationToken ?? CancellationToken.None;
                    continue;
                }

                // AudioObj single -> use SelectedAudio if not having ResultAudioObj
                if (pType == typeof(AudioObj))
                {
                    var sel = this.ResultAudioObj ?? this.SelectedAudio;
                    if (sel == null)
                    {
                        MessageBox.Show($"Parameter '{p.Name}' needs an AudioObj. Please select a track.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        // cleanup
                        this._isRunning = false;
                        this.button_run.Text = "Run";
                        this.button_run.Enabled = true;
                        this.Cursor = Cursors.Default;
                        try { this._elapsedTimer?.Stop(); this._elapsedStopwatch?.Stop(); } catch { }
                        return;
                    }
                    var argAudio = await sel.CloneAsync(); // Supply a working clone
                    args[i] = argAudio;
                    if (firstAudioArg == null)
                    {
                        firstAudioArg = argAudio;
                    }
                    continue;
                }

                // IEnumerable<AudioObj> etc. -> build list from SelectedAudio or available tracks
                if (pType == typeof(IEnumerable<AudioObj>) || pType == typeof(AudioObj[]) || pType == typeof(List<AudioObj>) || pType == typeof(ICollection<AudioObj>))
                {
                    var single = this.SelectedAudio;
                    List<AudioObj> list;
                    if (single != null)
                    {
                        list = [single];
                    }
                    else
                    {
                        // fallback: all non-disposed trackviews with audio
                        list = WindowMain.TrackViews
                            .Where(tv => !tv.IsDisposed && tv.OriginalAudio != null)
                            .Select(tv => tv.OriginalAudio!)
                            .Where(a => a.Duration > TimeSpan.Zero && a.Data.LongLength > 0)
                            .ToList();
                        if (list.Count == 0)
                        {
                            MessageBox.Show($"Parameter '{p.Name}' needs at least an AudioObj!", "Error no track", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            // cleanup
                            this._isRunning = false;
                            this.button_run.Text = "Run";
                            this.button_run.Enabled = true;
                            this.Cursor = Cursors.Default;
                            try { this._elapsedTimer?.Stop(); this._elapsedStopwatch?.Stop(); } catch { }
                            return;
                        }
                    }

                    if (pType == typeof(AudioObj[]))
                    {
                        args[i] = list.ToArray();
                    }
                    else if (pType == typeof(List<AudioObj>))
                    {
                        args[i] = list;
                    }
                    else if (pType == typeof(ICollection<AudioObj>))
                    {
                        args[i] = (ICollection<AudioObj>) list;
                    }
                    else
                    {
                        args[i] = list.AsEnumerable();
                    }

                    continue;
                }

                // IProgress<double> -> wire to progressBar_processing
                if (pType == typeof(IProgress<double>))
                {
                    this.progressBar_processing.Value = 0;
                    var progress = new Progress<double>(d =>
                    {
                        try
                        {
                            var v = (int) Math.Clamp(d * this.progressBar_processing.Maximum, 0.0, this.progressBar_processing.Maximum);
                            if (this.progressBar_processing.InvokeRequired)
                            {
                                this.progressBar_processing.Invoke(() => this.progressBar_processing.Value = v);
                            }
                            else
                            {
                                this.progressBar_processing.Value = v;
                            }
                        }
                        catch { }
                    });
                    args[i] = progress;
                    continue;
                }

                // If control exists for parameter, read it and convert
                if (this._paramControls.TryGetValue(p, out var ctrl) && ctrl != null)
                {
                    try
                    {
                        object? val = ReadControlValueAndConvert(ctrl, pType);
                        args[i] = val;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error reading parameter '{p.Name}': {ex.Message}", "Parse error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        // cleanup
                        this._isRunning = false;
                        this.button_run.Text = "Run";
                        this.button_run.Enabled = true;
                        this.Cursor = Cursors.Default;
                        try { this._elapsedTimer?.Stop(); this._elapsedStopwatch?.Stop(); } catch { }
                        return;
                    }
                }
                else
                {
                    // Fallback: default value / null / activator
                    if (p.HasDefaultValue)
                    {
                        args[i] = p.DefaultValue;
                    }
                    else
                    {
                        var underlying = Nullable.GetUnderlyingType(pType) ?? pType;
                        args[i] = underlying.IsValueType ? Activator.CreateInstance(underlying) : null;
                    }
                }
            }

            object? result = null;
            try
            {
                // Invoke method with args
                result = method.Invoke(null, args);

                // If returns Task, await it
                if (result is Task task)
                {
                    await task.ConfigureAwait(true);

                    // Extract Result for Task<T>
                    var ttype = task.GetType();
                    if (ttype.IsGenericType)
                    {
                        var prop = ttype.GetProperty("Result");
                        result = prop?.GetValue(task);
                    }
                    else
                    {
                        result = null;
                    }
                }

                // Set ResultAudioObj:
                // - if method returned an AudioObj -> use returned (new) AudioObj
                // - if method returned non-AudioObj value -> keep ResultAudioObj = null and display value
                // - if method returned null and we passed an audio arg -> use the passed audio (in-place mutation)
                if (result is AudioObj returnedAo)
                {
                    this.ResultAudioObj = returnedAo;
                }
                else if (result is IEnumerable<AudioObj> audioEnumerable)
                {
                    var list = audioEnumerable.Where(a => a != null).ToList();
                    if (list.Count > 0)
                    {
                        var view = new AudioCollectionView(list);
                        view.Show();
                    }
                    this.ResultAudioObj = null;
                }
                else if (result != null)
                {
                    // Nicht-Audio-Ergebnis: ResultAudioObj bleibt null, Ergebnis zur strukturierten Anzeige prüfen
                    this.ResultAudioObj = null;
                    try
                    {
                        // Versuche IDictionary (non-generic)
                        var entries = new List<KeyValuePair<object, object>>();
                        if (result is System.Collections.IDictionary nid)
                        {
                            foreach (System.Collections.DictionaryEntry de in nid)
                            {
                                entries.Add(new KeyValuePair<object, object>(de.Key!, de.Value!));
                            }
                        }
                        else if (result is System.Collections.IEnumerable ienum)
                        {
                            // Prüfe auf KeyValuePair<,> Elemente via Reflection
                            foreach (var el in ienum)
                            {
                                if (el == null)
                                {
                                    continue;
                                }

                                var t = el.GetType();
                                var kp = t.GetProperty("Key");
                                var vp = t.GetProperty("Value");
                                if (kp != null && vp != null)
                                {
                                    var k = kp.GetValue(el);
                                    var v = vp.GetValue(el);
                                    entries.Add(new KeyValuePair<object, object>(k!, v!));
                                }
                                else
                                {
                                    // Nicht KeyValuePair-Enumerable -> brich ab
                                    entries.Clear();
                                    break;
                                }
                            }
                        }

                        if (entries.Count > 0)
                        {
                            // Versuche keys->int und values->float zu parsen
                            var intFloat = new Dictionary<int, float>();
                            foreach (var kv in entries)
                            {
                                if (kv.Key == null)
                                {
                                    continue;
                                }

                                int ik = 0; bool keyOk = false;
                                try { ik = Convert.ToInt32(kv.Key, CultureInfo.InvariantCulture); keyOk = true; } catch { keyOk = false; }
                                if (!keyOk)
                                {
                                    continue;
                                }

                                float fv = 0f; bool valOk = false;
                                if (kv.Value is float f) { fv = f; valOk = true; }
                                else if (kv.Value is double d) { fv = (float) d; valOk = true; }
                                else if (kv.Value is decimal dec) { fv = (float) dec; valOk = true; }
                                else if (kv.Value is string s && float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var pf)) { fv = pf; valOk = true; }
                                else
                                {
                                    try { fv = Convert.ToSingle(kv.Value, CultureInfo.InvariantCulture); valOk = true; } catch { valOk = false; }
                                }

                                if (valOk)
                                {
                                    intFloat[ik] = fv;
                                }
                            }

                            if (intFloat.Count > 0)
                            {
                                // Erkenne mono-Argument falls vorhanden
                                bool monoArg = false;
                                try
                                {
                                    for (int pi = 0; pi < parameters.Length; pi++)
                                    {
                                        var p = parameters[pi];
                                        if (string.Equals(p.Name, "mono", StringComparison.OrdinalIgnoreCase) && args.Length > pi && args[pi] is bool bv)
                                        {
                                            monoArg = bv;
                                            break;
                                        }
                                    }
                                }
                                catch { }

                                var audioForMapping = firstAudioArg ?? this.SelectedAudio;
                                // Zeige generischen Entries-Dialog und übergebe die erkannten numerischen Paare
                                this.ShowResultEntriesDialog(method.Name + " result", entries, intFloat, audioForMapping, monoArg);
                                return;
                            }
                            else
                            {
                                // Falls nicht numeric pairs, zeige generische Key=>Value Liste im Entries-Dialog
                                this.ShowResultEntriesDialog(method.Name + " result", entries, null, firstAudioArg ?? this.SelectedAudio, false);
                                return;
                            }
                        }

                        // Fallback: ToString
                        string txt = result.ToString() ?? string.Empty;
                        this.ShowResultValueDialog(method.Name + " result", txt);
                    }
                    catch { }
                }
                else
                {
                    // result == null
                    this.ResultAudioObj = firstAudioArg;
                }
            }
            catch (TargetInvocationException tie)
            {
                var msg = tie.InnerException?.Message ?? tie.Message;
                MessageBox.Show($"Error executing method: {msg}", "Execution error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Operation cancelled.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Cleanup CTS
                try
                {
                    if (this._cancellationTokenSource != null)
                    {
                        try { this._cancellationTokenSource.Dispose(); } catch { }
                        this._cancellationTokenSource = null;
                        this._cancellationToken = null;
                    }
                }
                catch { }

                this._isRunning = false;

                // Stop timer/stopwatch but leave final elapsed time visible
                try
                {
                    this._elapsedTimer?.Stop();
                    this._elapsedStopwatch?.Stop();
                }
                catch { }

                // Button-Text & Enabled zurücksetzen: wenn Methode keinen CT-Param hatte, Button war deaktiviert während Lauf -> wieder aktivieren.
                try
                {
                    this.button_run.Text = "Run";
                    this.button_run.Enabled = true;
                }
                catch { }

                this.Cursor = Cursors.Default;

                // Wenn ein IProgress verwendet wurde, setze Balken auf Minimum nach kurzer Visualisierung
                try
                {
                    if (this.progressBar_processing.InvokeRequired)
                    {
                        this.progressBar_processing.Invoke(() => this.progressBar_processing.Value = this.progressBar_processing.Minimum);
                    }
                    else
                    {
                        this.progressBar_processing.Value = this.progressBar_processing.Minimum;
                    }
                }
                catch { }
            }

            // Optional: display brief success (nur wenn kein Fehler und wenn ein AudioObj result vorhanden ist)
            if (this.ResultAudioObj != null)
            {
                MessageBox.Show("Method executed.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // Ensure progress bar is reset once all dialogs are closed
            try
            {
                if (this.progressBar_processing.InvokeRequired)
                {
                    this.progressBar_processing.Invoke(() => this.progressBar_processing.Value = this.progressBar_processing.Minimum);
                }
                else
                {
                    this.progressBar_processing.Value = this.progressBar_processing.Minimum;
                }
            }
            catch { }
        }

        /// <summary>
        /// Zeigt ein kleines Dialogfenster mit Text an, damit Benutzer Ergebnis auswählen/kopieren können.
        /// </summary>
        private void ShowResultValueDialog(string title, string text)
        {
            try
            {
                using var dlg = new Form
                {
                    Text = title,
                    StartPosition = FormStartPosition.CenterParent,
                    Size = new Size(640, 240),
                    MinimizeBox = false,
                    MaximizeBox = false,
                    FormBorderStyle = FormBorderStyle.SizableToolWindow
                };

                var tb = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    Font = SystemFonts.MessageBoxFont,
                    Text = text
                };

                dlg.Controls.Add(tb);
                dlg.ShowDialog(this);
            }
            catch { }
        }



        private void button_apply_Click(object sender, EventArgs e)
        {
            if (this.ResultAudioObj == null || this.ProcessedAudioObj == null)
            {
                return;
            }

            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);

            // Korrigiert: Kein vollqualifizierter Name nötig, da kein Namenskonflikt mit Namespace und Typ besteht.
            this.ProcessedAudioObj?.ReplaceWith(this.ResultAudioObj!, ctrlFlag);
            if (ctrlFlag)
            {
                this.ResultAudioObj = null;
            }
        }

        private static string GetFriendlyTypeName(Type t)
        {
            if (t == typeof(string))
            {
                return "string";
            }

            if (t == typeof(int))
            {
                return "int";
            }

            if (t == typeof(long))
            {
                return "long";
            }

            if (t == typeof(float))
            {
                return "float";
            }

            if (t == typeof(double))
            {
                return "double";
            }

            if (t == typeof(bool))
            {
                return "bool";
            }

            if (t.IsEnum)
            {
                return $"enum {t.Name}";
            }

            if (Nullable.GetUnderlyingType(t) != null)
            {
                return $"{GetFriendlyTypeName(Nullable.GetUnderlyingType(t)!)}?";
            }

            return t.Name;
        }

        internal static IEnumerable<MethodInfo> ReflectionGetAccessibleMethodsConsuming(Type type, BindingFlags bindingFlags = (BindingFlags.Public | BindingFlags.Static))
        {
            // Durchsuche alle Typen in derselben Assembly.
            // Wichtig: statische Klassen sind in C# "abstract" UND "sealed" — diese nicht ausschließen.
            var methods = type.Assembly.GetTypes()
                .Where(t => t.IsClass && (!t.IsAbstract || (t.IsAbstract && t.IsSealed)))
                .SelectMany(t => t.GetMethods(bindingFlags)
                    .Where(m => m.GetParameters().Length > 0
                                && m.GetParameters().Count(p => ParameterTypeMatches(p.ParameterType, type)) == 1));

            return methods;
        }

        private static bool ParameterTypeMatches(Type paramType, Type targetType)
        {
            // exakt gleiche Typen
            if (paramType == targetType)
            {
                return true;
            }

            // Array (AudioObj[])
            if (paramType.IsArray && paramType.GetElementType() == targetType)
            {
                return true;
            }

            // Generische Typen wie IEnumerable<AudioObj>, List<AudioObj>, ICollection<AudioObj>
            if (paramType.IsGenericType)
            {
                var args = paramType.GetGenericArguments();
                if (args.Any(a => a == targetType))
                {
                    return true;
                }
            }

            // Zusätzlich: typkompatible IEnumerable (falls jemand z.B. eine nicht-generische IEnumerable verwendet)
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(paramType) && paramType != typeof(string))
            {
                // Bei nicht-generischen IEnumerable kann man hier nicht zuverlässig das Elementtyp prüfen.
                // Wir ignorieren string, da string IEnumerable<char> ist.
                // Optional: Rückgabe 'true' hier würde viele Treffer bringen; bessere Lösung ist generischer Check oben.
            }

            return false;
        }

        private static object? ReadControlValueAndConvert(Control ctrl, Type targetType)
        {
            // Handle checkbox
            if (ctrl is CheckBox cb)
            {
                if (targetType == typeof(bool) || targetType == typeof(Boolean))
                {
                    return cb.Checked;
                }

                // attempt to convert
                return Convert.ChangeType(cb.Checked, Nullable.GetUnderlyingType(targetType) ?? targetType, CultureInfo.InvariantCulture);
            }

            // ComboBox (enums)
            if (ctrl is ComboBox combo)
            {
                var sel = combo.SelectedItem?.ToString();
                if (targetType.IsEnum)
                {
                    if (sel == null)
                    {
                        return Enum.GetValues(targetType).GetValue(0);
                    }

                    return Enum.Parse(targetType, sel);
                }
                return sel;
            }

            // NumericUpDown
            if (ctrl is NumericUpDown nud)
            {
                var convType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                decimal val = nud.Value;

                if (convType == typeof(int))
                {
                    return Convert.ChangeType((int) val, convType, CultureInfo.InvariantCulture);
                }
                if (convType == typeof(long))
                {
                    return Convert.ChangeType((long) val, convType, CultureInfo.InvariantCulture);
                }
                if (convType == typeof(float))
                {
                    return Convert.ChangeType((float) val, convType, CultureInfo.InvariantCulture);
                }
                if (convType == typeof(double))
                {
                    return Convert.ChangeType((double) val, convType, CultureInfo.InvariantCulture);
                }
                if (convType == typeof(decimal))
                {
                    return val;
                }

                // fallback
                return Convert.ChangeType(val, convType, CultureInfo.InvariantCulture);
            }

            // TextBox
            if (ctrl is TextBox txt)
            {
                var text = txt.Text ?? string.Empty;
                if (string.IsNullOrEmpty(text))
                {
                    if (Nullable.GetUnderlyingType(targetType) != null)
                    {
                        return null;
                    }

                    if (targetType == typeof(string))
                    {
                        return string.Empty;
                    }
                }

                var convType2 = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (convType2 == typeof(string))
                {
                    return text;
                }

                if (convType2.IsEnum)
                {
                    return Enum.Parse(convType2, text, ignoreCase: true);
                }

                var tc = TypeDescriptor.GetConverter(convType2);
                if (tc != null && tc.CanConvertFrom(typeof(string)))
                {
                    return tc.ConvertFromInvariantString(text);
                }

                // last resort
                return Convert.ChangeType(text, convType2, CultureInfo.InvariantCulture);
            }

            // Fallback: return null or attempt Convert
            return null;
        }

        private async void button_test_onnxStems_Click(object sender, EventArgs e)
        {
            using var onnx = new OnnxService();
            if (!onnx.IsOnline)
            {
                MessageBox.Show("ONNX session is not initialized. Please check model paths and availability.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string testPath = this.textBox_testPath.Text;
            if (!File.Exists(testPath))
            {
                // Choose random audio < 5 minutes from MyMusic
                string musicDir = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                var audioFiles = Directory.GetFiles(musicDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (audioFiles.Count == 0)
                {
                    MessageBox.Show("No audio files found in My Music folder for testing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var rnd = new Random();
                string randomFile = audioFiles[rnd.Next(audioFiles.Count)];
                while (File.Exists(randomFile) && new FileInfo(randomFile).Length < 1 * 1024 * 1024 && new FileInfo(randomFile).Length > 25 * 1024 * 1024) // zwischen 1MB und 25MB (ungefähr <5min)
                {
                    randomFile = audioFiles[rnd.Next(audioFiles.Count)];
                }

                if (!File.Exists(randomFile))
                {
                    MessageBox.Show("No suitable audio file found in My Music folder for testing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                testPath = randomFile;
            }

            var acv = new AudioCollectionView([]);
            string[] stems = ["drums", "bass", "other", "vocals"];
            var audio = new AudioObj(testPath);
            var results = new Dictionary<string, float[]>();
            foreach (var ste in stems)
            {
                try
                {
                    var stemData = await onnx.ExtractStemAsync(audio, ste);
                    results[ste] = stemData;

                    var clone = await audio.CloneAsync();
                    clone.Data = stemData;
                    clone.Rename(audio.OriginalName + $"_{ste}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error extracting stem '{ste}': {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void button_browseTestDir_Click(object sender, EventArgs e)
        {
            // Dialog öffnen zum Ordner oder Datei auswählen, Pfad in textBox_testPath einfügen
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select a folder or file for testing.",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                ShowHiddenFiles = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            };

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                this.textBox_testPath.Text = fbd.SelectedPath;
            }
        }

        private void textBox_testPath_TextChanged(object sender, EventArgs e)
        {
            string? text = this.textBox_testPath.Text;
            if (Directory.Exists(text) || File.Exists(text))
            {
                this.textBox_testPath.ForeColor = SystemColors.WindowText;
            }
            else
            {
                this.textBox_testPath.ForeColor = Color.Red;
            }
        }
    }
}