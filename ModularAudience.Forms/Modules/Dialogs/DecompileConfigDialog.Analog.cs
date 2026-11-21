using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Helpers;
using System.Globalization;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class DecompileConfigDialog
{
    private Task? analogBandAnalysis;

    private void InitializeAnalogUi()
    {
        if (this.dataGridView_analogBands.Rows.Count > 0)
        {
            return;
        }

        AddAnalogBand("SubBass", 20, 120);
        AddAnalogBand("Bass", 120, 250);
        AddAnalogBand("LowMid", 250, 2000);
        AddAnalogBand("Mid", 2000, 4000);
        AddAnalogBand("High", 4000, 20000);
    }

    private void button_addAnalogBand_Click(object? sender, EventArgs e)
    {
        IReadOnlyList<AnalogSeparationBand> bands = this.TryReadAnalogBands();
        double low = bands.Count > 0 ? bands[^1].HighHz : 20;
        double high = Math.Min(24000, low + 1000);
        AddAnalogBand($"Band {bands.Count + 1}", low, high);
    }

    private void button_removeAnalogBand_Click(object? sender, EventArgs e)
    {
        if (this.dataGridView_analogBands.SelectedRows.Count == 0)
        {
            return;
        }

        foreach (DataGridViewRow row in this.dataGridView_analogBands.SelectedRows
                     .Cast<DataGridViewRow>()
                     .OrderByDescending(row => row.Index)
                     .ToArray())
        {
            this.dataGridView_analogBands.Rows.Remove(row);
        }
    }

    private async void button_autoAnalogBands_Click(object? sender, EventArgs e)
    {
        if (this.analogBandAnalysis is { IsCompleted: false })
        {
            return;
        }

        this.button_autoAnalogBands.Enabled = false;
        this.label_analogStatus.Text = "Analyzing the source spectrum...";
        this.analogBandAnalysis = this.AnalyzeAnalogBandsAsync();
        try
        {
            await this.analogBandAnalysis.ConfigureAwait(true);
        }
        catch (Exception error)
        {
            LogManager.Log($"Decompile analog band analysis failed: {error}");
            WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Analog Band Analysis Failed", error);
        }
        finally
        {
            this.analogBandAnalysis = null;
            this.button_autoAnalogBands.Enabled = true;
            if (this.closeRequested && !this.IsDisposed && !this.Disposing)
            {
                this.BeginInvoke(this.Close);
            }
        }
    }

    private async Task AnalyzeAnalogBandsAsync()
    {
        IReadOnlyList<AnalogSeparationBand> bands = await AnalogSeparationProcessor
            .AnalyzeBandsAsync(
                this.sourceSnapshot,
                maxBands: 8,
                windowSize: (int)this.numeric_analogWindow.Value,
                threads: (int)this.numeric_workers.Value)
            .ConfigureAwait(true);
        if (bands.Count == 0)
        {
            this.label_analogStatus.Text = "No usable spectrum was found.";
            return;
        }

        this.dataGridView_analogBands.Rows.Clear();
        foreach (AnalogSeparationBand band in bands)
        {
            AddAnalogBand(band.Name, band.LowHz, band.HighHz);
        }

        this.label_analogStatus.Text = $"Applied {bands.Count} automatically detected bands.";
    }

    private AnalogAudioStemSeparator CreateAnalogSeparator(int maxWorkers)
    {
        IReadOnlyList<AnalogSeparationBand> bands = this.ReadAnalogBands();
        var settings = new AnalogSeparationSettings(
            (int)this.numeric_analogWindow.Value,
            (double)this.numeric_analogOverlap.Value / 100.0,
            (int)this.numeric_analogSharpness.Value,
            this.checkBox_analogSubtract.Checked,
            maxWorkers,
            this.checkBox_analogTimbre.Checked,
            this.checkBox_analogHarmonic.Checked);
        return new AnalogAudioStemSeparator(bands, settings);
    }

    private IReadOnlyList<AnalogSeparationBand> ReadAnalogBands()
    {
        IReadOnlyList<AnalogSeparationBand> bands = this.TryReadAnalogBands();
        if (bands.Count == 0)
        {
            throw new ArgumentException("Add at least one Analog Separation band.");
        }

        AnalogSeparationBand? invalid = bands.FirstOrDefault(band => band.LowHz >= band.HighHz);
        if (invalid is not null)
        {
            throw new ArgumentException($"Band '{invalid.Name}' has an invalid frequency range.");
        }

        return bands;
    }

    private IReadOnlyList<AnalogSeparationBand> TryReadAnalogBands()
    {
        List<AnalogSeparationBand> bands = [];
        foreach (DataGridViewRow row in this.dataGridView_analogBands.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string name = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            string lowText = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            string highText = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!double.TryParse(lowText, NumberStyles.Float, CultureInfo.InvariantCulture, out double low)
                || !double.TryParse(highText, NumberStyles.Float, CultureInfo.InvariantCulture, out double high))
            {
                throw new ArgumentException($"Band '{name}' contains an invalid frequency.");
            }

            bands.Add(new AnalogSeparationBand(
                string.IsNullOrWhiteSpace(name) ? $"Band {bands.Count + 1}" : name.Trim(),
                low,
                high));
        }

        return bands;
    }

    private void AddAnalogBand(string name, double lowHz, double highHz)
    {
        this.dataGridView_analogBands.Rows.Add(
            name,
            lowHz.ToString("0.###", CultureInfo.InvariantCulture),
            highHz.ToString("0.###", CultureInfo.InvariantCulture));
    }
}
