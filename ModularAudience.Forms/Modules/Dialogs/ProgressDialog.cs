using System.Diagnostics;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class ProgressDialog : Form
{
    private const double MinimumRateSampleIntervalSeconds = 1.0;
    private const double MaximumRateMultipleOfAverage = 2.0;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly IProgress<double>? progress;
    private readonly string timeFormat;
    private readonly double windowCloseDelay;
    private readonly CancellationTokenRegistration cancellationRegistration;
    private readonly CancellationTokenSource? cancellationSource;
    private bool closeRequested;
    private bool operationCompleted;
    private bool cancellationRequested;
    private readonly CancellationToken cancellationToken;
    private double lastRateSampleFraction;
    private double lastRateSampleSeconds;
    private double latestProgressFraction;
    private double smoothedProgressRate;
    private int progressRateSampleCount;
    private TimeSpan? smoothedCompletionTime;

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
        this.cancellationToken = ct;
        this.InitializeComponent();
        this.Text = title;
        this.label_operation.Text = title;
        this.progressBar.Visible = progress != null;
        this.label_percent.Visible = progress != null;
        this.label_proposed.Visible = progress != null;
        this.button_cancel.Visible = this.cancellationSource != null;
        this.button_cancel.Enabled = this.cancellationSource != null;
        this.timer_elapsed.Start();
        // Token callbacks can run on a worker thread (or synchronously inside Cancel).
        // Never close/dispose the native dialog from within that callback.
        this.cancellationRegistration = ct.Register(this.QueueClose);
        this.UpdateTimeLabels(0.0);
    }

    public void Report(double progress)
    {
        if (this.IsDisposed || this.Disposing || this.closeRequested)
        {
            return;
        }

        progress = Math.Clamp(progress, 0.0, 1.0);
        if (this.InvokeRequired)
        {
            this.PostToUi(() => this.Report(progress));
            return;
        }

        this.latestProgressFraction = progress;
        this.progressBar.Value = (int)Math.Round(progress * 100.0);
        this.label_percent.Text = $"{progress:P0}";
        this.UpdateTimeLabels(progress);
    }

    public void SetStatus(string status)
    {
        if (this.IsDisposed || this.Disposing || this.closeRequested || string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.PostToUi(() => this.SetStatus(status));
            return;
        }

        this.label_operation.Text = status;
    }

    public void Complete()
    {
        if (this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired) { this.PostToUi(this.Complete); return; }
        this.operationCompleted = true;
        this.timer_elapsed.Stop();
        this.stopwatch.Stop();
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
            this.PostToUi(this.Close);
            return;
        }

        this.closeRequested = true;
        if (!this.operationCompleted)
        {
            this.RequestCancellation();
        }
        if (this.windowCloseDelay <= 0.0d)
        {
            base.Close();
            return;
        }

        this.timer_close.Interval = Math.Max(1, (int)Math.Min(int.MaxValue, this.windowCloseDelay * 1000.0d));
        this.timer_close.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!this.operationCompleted)
        {
            this.RequestCancellation();
        }

        base.OnFormClosing(e);
    }

    private void RequestCancellation()
    {
        if (this.cancellationRequested || this.operationCompleted) return;
        this.cancellationRequested = true;
        try { this.cancellationSource?.Cancel(); }
        catch (ObjectDisposedException)
        {
            // The caller owns the source and may already have finished its cleanup.
            // A delayed window-close message must still be safe in that case.
        }
    }

    private void button_cancel_Click(object? sender, EventArgs e)
    {
        this.button_cancel.Enabled = false;
        this.label_operation.Text = "Cancelling after the current safe stage...";
        this.RequestCancellation();
    }

    private void PostToUi(Action action)
    {
        if (this.IsDisposed || this.Disposing || !this.IsHandleCreated) return;
        try { this.BeginInvoke((Action)(() => { if (!this.IsDisposed && !this.Disposing) action(); })); }
        catch (InvalidOperationException) when (this.IsDisposed || this.Disposing || !this.IsHandleCreated) { }
    }

    private void QueueClose()
    {
        if (this.IsHandleCreated) this.PostToUi(this.Close);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (this.cancellationToken.IsCancellationRequested) this.PostToUi(this.Close);
    }

    private void UpdateTimeLabels(double progress, bool sampleProgress = false)
    {
        TimeSpan elapsed = this.stopwatch.Elapsed;
        this.label_elapsed.Text = $"Elapsed: {this.FormatTime(elapsed)}";
        if (this.progress == null)
        {
            return;
        }

        double elapsedSeconds = elapsed.TotalSeconds;
        double fractionDelta = progress - this.lastRateSampleFraction;
        double elapsedDelta = elapsedSeconds - this.lastRateSampleSeconds;
        if (sampleProgress
            && fractionDelta > 0.0001
            && elapsedDelta >= MinimumRateSampleIntervalSeconds)
        {
            double instantRate = fractionDelta / elapsedDelta;
            double maximumRate = GetMaximumPlausibleRate(progress, elapsedSeconds);
            instantRate = Math.Min(instantRate, maximumRate);
            this.smoothedProgressRate = this.smoothedProgressRate <= 0
                ? instantRate
                : (this.smoothedProgressRate * 0.72) + (instantRate * 0.28);
            this.smoothedProgressRate = Math.Min(this.smoothedProgressRate, maximumRate);
            this.lastRateSampleFraction = progress;
            this.lastRateSampleSeconds = elapsedSeconds;
            this.progressRateSampleCount++;
        }

        TimeSpan proposed = TimeSpan.Zero;
        double estimatedRate = Math.Min(
            this.smoothedProgressRate,
            GetMaximumPlausibleRate(progress, elapsedSeconds));
        if (this.progressRateSampleCount >= 2
            && elapsedSeconds >= 5
            && estimatedRate > 0.000001
            && progress < 1.0)
        {
            double totalSeconds = elapsedSeconds + ((1.0 - progress) / estimatedRate);
            var candidate = TimeSpan.FromSeconds(Math.Clamp(totalSeconds, 0, TimeSpan.MaxValue.TotalSeconds));
            if (this.smoothedCompletionTime is null)
            {
                this.smoothedCompletionTime = candidate;
            }
            else
            {
                double mixedSeconds = (this.smoothedCompletionTime.Value.TotalSeconds * 0.80)
                    + (candidate.TotalSeconds * 0.20);
                this.smoothedCompletionTime = TimeSpan.FromSeconds(
                    Math.Clamp(mixedSeconds, elapsedSeconds, TimeSpan.MaxValue.TotalSeconds));
            }

            proposed = this.smoothedCompletionTime.Value;
        }
        else if (progress >= 1.0)
        {
            proposed = elapsed;
        }

        this.label_proposed.Text = proposed > TimeSpan.Zero && progress > 0.0001
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

    private static double GetMaximumPlausibleRate(double progress, double elapsedSeconds)
    {
        return progress > 0 && elapsedSeconds > 0
            ? (progress / elapsedSeconds) * MaximumRateMultipleOfAverage
            : double.PositiveInfinity;
    }

    private void timer_elapsed_Tick(object? sender, EventArgs e)
    {
        // The progress bar is rounded for display; only this timer samples exact reported progress.
        this.UpdateTimeLabels(this.latestProgressFraction, sampleProgress: true);
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
