using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    /// <summary>
    /// Removes every control from the tab order of all windows. Without this, the tab key
    /// jumps between the buttons of a window and the arrow keys move the focus to the next
    /// control, for example to the varispeed scroll bar of a track view, which then changes
    /// the tempo while the track is playing.
    /// </summary>
    internal static class TabNavigationDisabler
    {
        private static readonly HashSet<Form> KnownForms = [];
        private static readonly MethodInfo? SetStyleMethod = typeof(Control).GetMethod(
            "SetStyle",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static System.Windows.Forms.Timer? _timer;

        public static void Install()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new System.Windows.Forms.Timer { Interval = 250 };
            _timer.Tick += (sender, e) => ApplyToOpenForms();
            _timer.Start();
        }

        private static void ApplyToOpenForms()
        {
            KnownForms.RemoveWhere(form => form.IsDisposed || form.Disposing);

            foreach (Form form in Application.OpenForms)
            {
                if (!KnownForms.Add(form))
                {
                    continue;
                }

                form.ControlAdded += Form_ControlAdded;
                Apply(form);
            }
        }

        private static void Form_ControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control != null)
            {
                Apply(e.Control);
            }
        }

        private static void Apply(Control control)
        {
            control.TabStop = false;

            if (control is ScrollBar)
            {
                // A scroll bar must never get the focus, otherwise the arrow keys would
                // scroll it and change the volume or the playback rate.
                MakeUnselectable(control);
            }

            foreach (Control child in control.Controls)
            {
                Apply(child);
            }
        }

        private static void MakeUnselectable(Control control)
        {
            try
            {
                SetStyleMethod?.Invoke(
                    control,
                    [ControlStyles.Selectable, false]);
            }
            catch
            {
            }
        }
    }
}
