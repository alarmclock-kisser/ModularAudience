using System.Diagnostics;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class ProgressDialog : Form
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly IProgress<double>? progress;
    private readonly string timeFormat;
    private readonly double windowCloseDelay;
    private readonly CancellationTokenRegistration cancellationRegistration;
    private readonly CancellationTokenSource? cancellationSource;
    private bool closeRequested;
    private bool operationCompleted;

    public ProgressDialog(
        string title = "Processing ...",
        IProgress<double>? progress = null,
        string timeFormat = "mm\\:ss\\.fff",
        double windowCloseDelay = 2.0d,
        CancellationToken ct = default,
        CancellationTokenSource? cancellationSource = null)
    {
        this.progress = progress;
        this.timeFormat = string.IsNullOrWhiteSpace(timeFormat) ? "mm\\:ss\\.fff" : timeFormat;
        this.windowCloseDelay = Math.Max(0.0d, windowCloseDelay);
        this.cancellationSource = cancellationSource;
        this.InitializeComponent();
        this.Text = title;
        this.label_operation.Text = title;
        this.progressBar.Visible = progress != null;
        this.label_percent.Visible = progress != null;
        this.label_proposed.Visible = progress != null;
        this.timer_elapsed.Start();
        this.cancellationRegistration = ct.Register(this.Close);
        this.UpdateTimeLabels(0.0);
    }

    public void Report(double progress)
    {
        if (this.IsDisposed)
        {
            return;
        }

        progress = Math.Clamp(progress, 0.0, 1.0);
        if (this.InvokeRequired)
        {
            this.BeginInvoke(() => this.Report(progress));
            return;
        }

        this.progressBar.Value = (int) Math.Round(progress * 100.0);
        this.label_percent.Text = $"{progress:P0}";
        this.UpdateTimeLabels(progress);
    }

    public void Complete()
    {
        this.operationCompleted = true;
        this.Report(1.0);
        this.Close();
    }

    public new void Close()
    {
        if (this.IsDisposed || this.closeRequested)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(this.Close);
            return;
        }

        this.closeRequested = true;
        if (!this.operationCompleted)
        {
            this.cancellationSource?.Cancel();
        }
        if (this.windowCloseDelay <= 0.0d)
        {
            base.Close();
            return;
        }

        this.timer_close.Interval = Math.Max(1, (int) Math.Min(int.MaxValue, this.windowCloseDelay * 1000.0d));
        this.timer_close.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!this.operationCompleted)
        {
            this.cancellationSource?.Cancel();
        }

        base.OnFormClosing(e);
    }

    private void UpdateTimeLabels(double progress)
    {
        TimeSpan elapsed = this.stopwatch.Elapsed;
        this.label_elapsed.Text = $"Elapsed: {this.FormatTime(elapsed)}";
        if (this.progress == null)
        {
            return;
        }

        TimeSpan proposed = progress > 0.0001
            ? TimeSpan.FromSeconds(elapsed.TotalSeconds / progress)
            : TimeSpan.Zero;
        this.label_proposed.Text = proposed > TimeSpan.Zero
            ? $"Proposed time: {this.FormatTime(proposed)}"
            : "Proposed time: --:--.---";
    }

    private string FormatTime(TimeSpan value)
    {
        try
        {
            return value.ToString(this.timeFormat);
        }
        catch (FormatException)
        {
            return value.ToString(@"mm\:ss\.fff");
        }
    }

    private void timer_elapsed_Tick(object? sender, EventArgs e)
    {
        this.UpdateTimeLabels(this.progressBar.Value / 100.0d);
    }

    private void timer_close_Tick(object? sender, EventArgs e)
    {
        this.timer_close.Stop();
        base.Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.timer_elapsed.Stop();
        this.timer_close.Stop();
        this.cancellationRegistration.Dispose();
        base.OnFormClosed(e);
    }
}
