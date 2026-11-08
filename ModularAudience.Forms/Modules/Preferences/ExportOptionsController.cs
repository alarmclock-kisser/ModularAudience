using ModularAudience.Audio;
using ModularAudience.Forms.Settings;

namespace ModularAudience.Forms;

/// <summary>Rebinds the two export controls as one coherent setting, without saving intermediate selections.</summary>
internal sealed class ExportOptionsController : IDisposable
{
    private readonly ComboBox formats;
    private readonly ComboBox bits;
    private bool applying;

    internal ExportOptionsController(ComboBox formats, ComboBox bits, string format, int bitDepth)
    {
        this.formats = formats;
        this.bits = bits;
        this.formats.Items.AddRange(AudioExporter.AvailableExportFormats.Keys
            .OrderBy(value => value.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        this.Apply(format, bitDepth);
        this.formats.SelectedIndexChanged += this.FormatChanged;
        this.bits.SelectedIndexChanged += this.BitsChanged;
    }

    internal string Format { get; private set; } = ".wav";
    internal int Bits { get; private set; } = 16;
    internal event EventHandler? Changed;

    internal void Apply(string format, int bitDepth)
    {
        ApplicationPreferences normalized = new ApplicationPreferences { ExportFormat = format, ExportBits = bitDepth }.Normalize();
        this.applying = true;
        try
        {
            this.Format = normalized.ExportFormat;
            this.Bits = normalized.ExportBits;
            this.formats.SelectedItem = this.Format;
            this.bits.BeginUpdate();
            try
            {
                this.bits.Items.Clear();
                if (AudioExporter.AvailableExportFormats.TryGetValue(this.Format, out int[]? options))
                    this.bits.Items.AddRange(options.Cast<object>().ToArray());
                this.bits.SelectedItem = this.Bits;
            }
            finally { this.bits.EndUpdate(); }
        }
        finally { this.applying = false; }
    }

    private void FormatChanged(object? sender, EventArgs e)
    {
        if (this.applying || this.formats.SelectedItem is not string format) return;
        this.Apply(format, ApplicationPreferences.DefaultBits(format));
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void BitsChanged(object? sender, EventArgs e)
    {
        if (this.applying || this.bits.SelectedItem is not int value || value == this.Bits) return;
        this.Bits = value;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        this.formats.SelectedIndexChanged -= this.FormatChanged;
        this.bits.SelectedIndexChanged -= this.BitsChanged;
    }
}
