using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;

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



        // Mapping ParameterInfo -> Eingabe-Control zur späteren Auslesung
        private readonly Dictionary<ParameterInfo, Control?> _paramControls = [];

        public DeveloperFunctions()
        {
            this.InitializeComponent();

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, true, false);

            this.numericUpDown_maxProcessors.Maximum = Environment.ProcessorCount;
            this.numericUpDown_maxProcessors.Value = Environment.ProcessorCount;
            this.FillComboBoxMethods(this.comboBox_methods);
            this.UpdateControlStates();

            this.FormClosing += (s, e) =>
            {
                WindowMain.LoopControlWindow = null;
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
            const int labelW = 140;
            const int ctrlW = 220;
            const int rowH = 26;
            const int margin = 6;

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];

                // Optional-Parameter berücksichtigen
                if (!this.ShowOptionalParameters && p.HasDefaultValue)
                {
                    continue;
                }

                var pType = p.ParameterType;

                // Skip generation for AudioObj parameter types (but show an info label if requested)
                bool isAudioObjSingle = pType == typeof(AudioObj);
                bool isAudioObjEnumerable = pType == typeof(IEnumerable<AudioObj>) || pType == typeof(AudioObj[]) || pType == typeof(List<AudioObj>) || pType == typeof(ICollection<AudioObj>);

                // If parameter is AudioObj (or collection) and auto-parameters are enabled, show a small info label and do NOT create an input control
                if ((isAudioObjSingle || isAudioObjEnumerable) && this.ShowAutoParameters)
                {
                    var lbl = new Label
                    {
                        Text = $"{p.Name}: (AudioObj - automatisch)",
                        Location = new Point(margin, y),
                        Size = new Size(labelW + ctrlW, rowH),
                        AutoEllipsis = true
                    };
                    panel.Controls.Add(lbl);
                    y += rowH;
                    continue;
                }

                // IProgress<double> parameter: don't create editable control, show info label that it will be bound to progressbar if present
                if (pType == typeof(IProgress<double>))
                {
                    var lbl = new Label
                    {
                        Text = $"{p.Name}: (IProgress<double> → Fortschrittsbalken)",
                        Location = new Point(margin, y),
                        Size = new Size(labelW + ctrlW, rowH),
                        AutoEllipsis = true
                    };
                    panel.Controls.Add(lbl);
                    y += rowH;
                    this._paramControls[p] = null; // Merk: this param is handled automatically
                    continue;
                }

                // Label
                var label = new Label
                {
                    Text = $"{p.Name} ({GetFriendlyTypeName(pType)})",
                    Location = new Point(margin, y),
                    Size = new Size(labelW, rowH),
                    AutoEllipsis = true
                };
                panel.Controls.Add(label);

                Control inputCtrl;

                // Booleans -> CheckBox
                if (pType == typeof(bool) || pType == typeof(Boolean))
                {
                    var cb = new CheckBox
                    {
                        Location = new Point(margin + labelW + 6, y + 3),
                        Size = new Size(16, 16),
                        Checked = p.HasDefaultValue && p.DefaultValue is bool bv ? bv : false
                    };
                    inputCtrl = cb;
                }
                // Enum -> ComboBox
                else if (pType.IsEnum)
                {
                    var combo = new ComboBox
                    {
                        Location = new Point(margin + labelW + 6, y),
                        Size = new Size(ctrlW - 6, rowH),
                        DropDownStyle = ComboBoxStyle.DropDownList
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
                // Numeric types -> TextBox (parsing beim Auslesen)
                else
                {
                    var txt = new TextBox
                    {
                        Location = new Point(margin + labelW + 6, y),
                        Size = new Size(ctrlW - 6, rowH)
                    };

                    // Prefill default value if any
                    if (p.HasDefaultValue && p.DefaultValue != null && p.DefaultValue != DBNull.Value)
                    {
                        txt.Text = Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    }

                    inputCtrl = txt;
                }

                panel.Controls.Add(inputCtrl);
                this._paramControls[p] = inputCtrl;

                // Optional: tooltip with default value
                if (p.HasDefaultValue)
                {
                    var tt = new ToolTip();
                    try
                    {
                        tt.SetToolTip(inputCtrl, $"Default: {p.DefaultValue ?? "null"}");
                    }
                    catch { }
                }

                y += rowH;
            }

            panel.AutoScroll = true;
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

        private async void button_run_Click(object? sender, EventArgs e)
        {
            var method = StaticAudioObjMethods.FirstOrDefault(m => m.Name.Equals(this.SelectedMethod, StringComparison.OrdinalIgnoreCase));
            if (method == null)
            {
                MessageBox.Show("No method selected.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            // Build args in same order as parameters
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pType = p.ParameterType;

                // AudioObj single -> use SelectedAudio if not having ResultAudioObj
                if (pType == typeof(AudioObj))
                {
                    var sel = this.ResultAudioObj ?? this.SelectedAudio;
                    if (sel == null)
                    {
                        MessageBox.Show($"Parameter '{p.Name}' needs an AudioObj. Please select a track.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    args[i] = sel.CloneAsync();
                    continue;
                }

                // IEnumerable<AudioObj> etc. -> build list from SelectedAudio or available tracks
                if (pType == typeof(IEnumerable<AudioObj>) || pType == typeof(AudioObj[]) || pType == typeof(List<AudioObj>) || pType == typeof(ICollection<AudioObj>))
                {
                    // get single selected or fallback to all open tracks
                    var single = this.SelectedAudio;
                    List<AudioObj> list;
                    if (single != null)
                    {
                        list = [single];
                    }
                    else
                    {
                        // fallback: all non-disposed trackviews with audio
                        list = WindowMain.TrackViews.Where(tv => !tv.IsDisposed && tv.OriginalAudio != null).Select(tv => tv.OriginalAudio!).Where(a => a.Duration > TimeSpan.Zero && a.Data.LongLength > 0).ToList();
                        if (list.Count == 0)
                        {
                            MessageBox.Show($"Parameter '{p.Name}' needs at least an AudioObj!", "Error no track", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            try
            {
                this.button_run.Enabled = false;
                this.Cursor = Cursors.WaitCursor;

                // Invoke
                object? result = method.Invoke(null, args);

                // If returns Task, await it
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);

                    // extract Result for Task<T>
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

                this.ResultAudioObj = result as AudioObj;

                // If method completed, set progress to 100% then 0%
                try
                {
                    if (this.progressBar_processing.InvokeRequired)
                    {
                        this.progressBar_processing.Invoke(() => this.progressBar_processing.Value = this.progressBar_processing.Maximum);
                        this.progressBar_processing.Value = this.progressBar_processing.Minimum;

                    }
                    else
                    {
                        this.progressBar_processing.Value = this.progressBar_processing.Maximum;
                        this.progressBar_processing.Value = this.progressBar_processing.Minimum;
                    }
                }
                catch { }

                // Optional: display brief success
                MessageBox.Show("Method executed.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (TargetInvocationException tie)
            {
                var msg = tie.InnerException?.Message ?? tie.Message;
                MessageBox.Show($"Error executing method: {msg}", "Execution error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.button_run.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }

        private async void button_apply_Click(object sender, EventArgs e)
        {
            if (this.ResultAudioObj == null || this.SelectedAudio == null)
            {
                return;
            }

            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);

            this.SelectedAudio.ReplaceWith(this.ResultAudioObj, ctrlFlag);
            if (ctrlFlag)
            {
                this.ResultAudioObj = null;
            }
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
                return Convert.ChangeType(cb.Checked, Nullable.GetUnderlyingType(targetType) ?? targetType, System.Globalization.CultureInfo.InvariantCulture);
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

                var convType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                if (convType == typeof(string))
                {
                    return text;
                }

                if (convType.IsEnum)
                {
                    return Enum.Parse(convType, text, ignoreCase: true);
                }

                var tc = TypeDescriptor.GetConverter(convType);
                if (tc != null && tc.CanConvertFrom(typeof(string)))
                {
                    return tc.ConvertFromInvariantString(text);
                }

                // last resort
                return Convert.ChangeType(text, convType, System.Globalization.CultureInfo.InvariantCulture);
            }

            // Fallback: return null or attempt Convert
            return null;
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


    }
}
