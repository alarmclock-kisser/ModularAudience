using System;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public static class ProgressAdapters
    {
        public static IProgress<double> ToProgressBar(
            ProgressBar bar,
            int max = 1000,
            bool growOnly = true)
        {
            if (bar == null)
            {
                throw new ArgumentNullException(nameof(bar));
            }

            bar.Minimum = 0;
            bar.Maximum = max;
            bar.Value = 0;

            return new Progress<double>(p =>
            {
                if (double.IsNaN(p) || double.IsInfinity(p))
                {
                    return;
                }

                p = Math.Clamp(p, 0.0, 1.0);
                int target = (int) Math.Round(p * max);

                if (bar.IsDisposed)
                {
                    return;
                }

                void Apply()
                {
                    if (bar.IsDisposed)
                    {
                        return;
                    }

                    int clamped = Math.Min(bar.Maximum, Math.Max(bar.Minimum, target));

                    // Monotonie erzwingen: nur erhöhen, niemals verringern
                    if (clamped > bar.Value && growOnly)
                    {
                        bar.Value = clamped;
                    }
                    else if (!growOnly)
                    {
                        bar.Value = clamped;
                    }
                }

                if (bar.InvokeRequired)
                {
                    bar.BeginInvoke(Apply);
                }
                else
                {
                    Apply();
                }
            });
        }
    }
}
