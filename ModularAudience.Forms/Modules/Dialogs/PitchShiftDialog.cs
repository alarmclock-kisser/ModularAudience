using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class PitchShiftDialog : Form
    {
        public static readonly float[] SemitoneSteps = [2.0f, 1.0f, 0.5f, 0.3333f, 0.25f, 0.2f, 0.1667f, 0.1429f, 0.125f, 0.1111f, 0.1f];

        private readonly AudioCollection AudioC = new();

        private int SamplesPerAudio => (int) ((float) this.numericUpDown_range.Value / (float.TryParse(this.domainUpDown_step.SelectedItem?.ToString(), out var flt) ? flt : 1.0f));


        internal AudioCollectionView? CollectionView { get; private set; } = null;


        private int ShiftRange => (int) this.numericUpDown_range.Value;
        private float Steps => float.TryParse(this.domainUpDown_step.SelectedItem?.ToString(), out float val) ? val : 1.0f;
        private bool UseFftPv => this.checkBox_fftPv.Checked;

        private float Semitones => (float) this.numericUpDown_semitones.Value;
        private double Percent => (double) this.numericUpDown_percent.Value;




        public PitchShiftDialog(IEnumerable<AudioObj> samples)
        {
            this.InitializeComponent();

            this.domainUpDown_step.Items.AddRange(Array.ConvertAll(SemitoneSteps, s => s.ToString()));
            this.domainUpDown_step.SelectedIndex = this.domainUpDown_step.Items.IndexOf("0,5");

            foreach (AudioObj obj in samples)
            {
                this.AudioC.Audios.Add(obj.Clone());
            }

            this.listBox_samples.Items.Clear();
            this.listBox_samples.DataSource = this.AudioC.Audios;
            this.listBox_samples.DisplayMember = "Name";

            this.numericUpDown_take.Maximum = this.SamplesPerAudio;

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(null, false, false, WindowMain.CurrentScreenId);

            this.FormClosing += (s, e) =>
            {
                this.AudioC.Dispose();
            };
        }

        private async void button_create_Click(object sender, EventArgs e)
        {
            this.button_create.Enabled = false;
            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);

            IProgress<double> progress = new Progress<double>(p =>
            {
                int percent = (int) (p * this.progressBar_processing.Maximum);

                this.progressBar_processing.Value = Math.Min(percent, this.progressBar_processing.Maximum);
            });

            var pitchedSamples = await PitchShifter.CreatePitchShiftsBatchAsync(this.AudioC.Audios, this.ShiftRange, this.Steps, this.UseFftPv, progress);

            this.CollectionView ??= new AudioCollectionView([]);

            foreach (var samples in pitchedSamples)
            {
                int take = Math.Clamp((int) this.numericUpDown_take.Value, 1, samples.Count());

                // Snapshot als IList für effizienten Indexzugriff (falls bereits IList vorhanden, wiederverwenden)
                IList<AudioObj> list = samples is IList<AudioObj> l ? l : [.. samples];
                for (int i = 0; i < list.Count; i++)
                {
                    if (i % take == 0)
                    {
                        this.CollectionView.AudioC.Audios.Add(list[i]);
                    }
                }
            }

            this.CollectionView.Show();
            this.CollectionView.Rename("Pitch-Shifted " + (this.AudioC.Audios.Count == 1 ? ("'" + this.AudioC.Audios.FirstOrDefault()?.Name + "'") : "Samples"));
            this.progressBar_processing.Value = 0;

            if (!ctrlFlag)
            {
                this.Close();
            }
        }

        private void numericUpDown_range_ValueChanged(object sender, EventArgs e)
        {
            this.numericUpDown_take.Maximum = this.SamplesPerAudio;
        }

        private void domainUpDown_step_SelectedItemChanged(object sender, EventArgs e)
        {
            this.numericUpDown_take.Maximum = this.SamplesPerAudio;
        }

        private async void button_shift_Click(object sender, EventArgs e)
        {
            this.button_shift.Enabled = false;

            // Anzahl der ausgewählten Elemente (Audios)
            var selectedItems = this.listBox_samples.SelectedItems.Cast<AudioObj>().ToList();
            int totalItems = selectedItems.Count;

            if (totalItems == 0)
            {
                MessageBox.Show("No audio samples selected.");
                return;
            }

            // Initialisierung
            this.progressBar_processing.Value = 0;
            var pitchedBag = new System.Collections.Concurrent.ConcurrentBag<AudioObj>();
            var elementProgress = new double[totalItems];

            // Erzeuge pro-Element Child-Progress-Objekte, die auf den UI-Thread reporten
            var tasks = selectedItems.Select((obj, idx) =>
            {
                var childProgress = new Progress<double>(p =>
                {
                    // safe update des Element-Fortschritts (läuft auf UI-Thread wegen Progress)
                    elementProgress[idx] = Math.Clamp(p, 0.0, 1.0);

                    // Berechne Gesamtfortschritt als Durchschnitt
                    double sum = 0.0;
                    for (int i = 0; i < elementProgress.Length; i++)
                    {
                        sum += elementProgress[i];
                    }
                    double overall = sum / totalItems;

                    int progressValue = (int) (overall * this.progressBar_processing.Maximum);
                    this.progressBar_processing.Value = Math.Min(progressValue, this.progressBar_processing.Maximum);
                });

                return Task.Run(async () =>
                {
                    try
                    {
                        if (obj == null)
                        {
                            // markiere als fertig
                            ((IProgress<double>)childProgress).Report(1.0);
                            return;
                        }

                        // Pitch shifting (innerhalb PitchShifter wird aufgeteilt und eigene Progress-Reports erzeugt)
                        var result = await PitchShifter.CreatePitchShiftWithoutTimestretchAsync(obj, this.Semitones, childProgress).ConfigureAwait(false);

                        // Ergebnis thread-sicher sammeln
                        pitchedBag.Add(result);
                    }
                    catch
                    {
                        // Fehler nicht werfen lassen; trotzdem als abgeschlossen markieren,
                        // damit die UI nicht auf unendlichen Fortschritt wartet.
                    }
                    finally
                    {
                        // Stelle sicher, dass dieses Element als 100% gemeldet wird
                        try { ((IProgress<double>)childProgress).Report(1.0); } catch { }
                    }
                });
            }).ToArray();

            // Auf Abschluss warten (ohne ConfigureAwait(false) damit Fortsetzung auf UI-Thread läuft)
            await Task.WhenAll(tasks);

            var pitchedCollection = pitchedBag.ToList();

            // Setze den Fortschritt nach Abschluss zurück (UI-Thread)
            this.progressBar_processing.Value = 0;

            // Wenn der Shift abgeschlossen ist, zeige die CollectionView mit den gepitchten Objekten an
            this.CollectionView ??= new AudioCollectionView(pitchedCollection);

            // CollectionView umbenennen
            this.CollectionView.Rename("Pitch-Shifted " + (pitchedCollection.Count == 1
                ? ("'" + pitchedCollection.FirstOrDefault()?.Name + "'")
                : "Samples"));

            // Fortschritt zurücksetzen
            this.progressBar_processing.Value = 0;

            // Dialog schließen, falls Ctrl nicht gedrückt wird
            if (!ModifierKeys.HasFlag(Keys.Control))
            {
                this.Close();
            }
        }


        private void numericUpDown_semitones_ValueChanged(object sender, EventArgs e)
        {
            double percentShift = Math.Pow(2.0, (double) this.Semitones / 12.0) - 1.0;
            this.numericUpDown_percent.Value = (decimal) (percentShift * 100.0);
        }

        private void numericUpDown_percent_ValueChanged(object sender, EventArgs e)
        {
            float semitoneShift = (float) (12.0 * Math.Log2(1.0 + ((double) this.Percent / 100.0)));
            this.numericUpDown_semitones.Value = (decimal) semitoneShift;
        }
    }
}
