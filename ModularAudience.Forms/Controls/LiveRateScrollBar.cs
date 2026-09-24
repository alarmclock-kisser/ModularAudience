using ModularAudience.Audio.Processing;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace ModularAudience.Forms.Controls
{
    internal sealed class LiveRateScrollBar : HScrollBar
    {
        private const int LeftButtonDown = 0x0201;
        private const int LeftButtonUp = 0x0202;
        private const int LeftButtonDoubleClick = 0x0203;
        private const int MouseMoveMessage = 0x0200;
        private const int CancelMode = 0x001F;
        private const int RepeatInterval = 50;
        private readonly Timer trackRepeatTimer;
        private bool followingTrack;
        private bool controlThumbGesture;
        private Point controlThumbDownPoint;
        private bool controlThumbMoved;

        internal bool IsCtrlResetGesture { get; private set; }

        public LiveRateScrollBar()
        {
            this.trackRepeatTimer = new Timer();
            this.trackRepeatTimer.Tick += this.TrackRepeatTimer_Tick;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == LeftButtonDown || message.Msg == LeftButtonDoubleClick)
            {
                Point point = new((short)message.LParam.ToInt64(), (short)(message.LParam.ToInt64() >> 16));
                if (this.HandleLeftButtonDown(point))
                {
                    return;
                }
            }
            if (message.Msg == MouseMoveMessage && this.controlThumbGesture)
            {
                Point point = new((short)message.LParam.ToInt64(), (short)(message.LParam.ToInt64() >> 16));
                int threshold = Math.Max(1, SystemInformation.DragSize.Width / 2);
                if (Math.Abs(point.X - this.controlThumbDownPoint.X) >= threshold)
                {
                    this.controlThumbMoved = true;
                }
            }
            if (message.Msg == LeftButtonUp && this.controlThumbGesture)
            {
                bool reset = !this.controlThumbMoved;
                this.controlThumbGesture = false;
                base.WndProc(ref message);
                if (reset && !this.IsDisposed)
                {
                    this.ResetFromControlClick();
                }
                return;
            }
            if (message.Msg == LeftButtonUp && this.followingTrack)
            {
                this.EndTrackFollowing();
                Point point = this.PointToClient(MousePosition);
                this.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
                return;
            }
            if (message.Msg == CancelMode)
            {
                this.EndTrackFollowing();
                this.controlThumbGesture = false;
            }
            base.WndProc(ref message);
        }

        private bool HandleLeftButtonDown(Point point)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                this.EndTrackFollowing();
                if (this.TryGetGeometry(out TrackGeometry thumbGeometry)
                    && point.X >= thumbGeometry.ThumbLeft && point.X < thumbGeometry.ThumbRight)
                {
                    this.controlThumbGesture = true;
                    this.controlThumbDownPoint = point;
                    this.controlThumbMoved = false;
                    return false;
                }

                this.ResetFromControlClick();
                return true;
            }
            if (!this.Enabled || !this.ClientRectangle.Contains(point)
                || !this.TryGetGeometry(out TrackGeometry geometry) || !geometry.IsTrackClick(point.X))
            {
                return false;
            }
            this.Focus();
            this.followingTrack = true;
            this.Capture = true;
            this.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
            this.StepTowardMouse(point.X);
            if (this.followingTrack)
            {
                this.trackRepeatTimer.Interval = 250 * (SystemInformation.KeyboardDelay + 1);
                this.trackRepeatTimer.Start();
            }
            return true;
        }

        private void ResetFromControlClick()
        {
            this.IsCtrlResetGesture = true;
            try
            {
                this.Value = 0;
                this.OnScroll(new ScrollEventArgs(ScrollEventType.ThumbPosition, 0));
            }
            finally
            {
                this.IsCtrlResetGesture = false;
            }
        }

        private void TrackRepeatTimer_Tick(object? sender, EventArgs e)
        {
            if (!this.followingTrack || !this.Capture || (MouseButtons & MouseButtons.Left) == 0)
            {
                this.EndTrackFollowing();
                return;
            }
            this.trackRepeatTimer.Interval = RepeatInterval;
            this.StepTowardMouse(this.PointToClient(MousePosition).X);
        }

        private void StepTowardMouse(int mouseX)
        {
            if (!this.TryGetGeometry(out TrackGeometry geometry))
            {
                this.EndTrackFollowing();
                return;
            }
            int maximum = Math.Max(this.Minimum, this.Maximum - Math.Max(0, this.LargeChange - 1));
            int next = GetNextValue(this.Value, this.Minimum, maximum, mouseX, geometry);
            if (next == this.Value)
            {
                return;
            }
            ScrollEventType type = next < this.Value ? ScrollEventType.LargeDecrement : ScrollEventType.LargeIncrement;
            ScrollEventArgs args = new(type, this.Value, next, ScrollOrientation.HorizontalScroll);
            this.OnScroll(args);
            if (!this.IsDisposed)
            {
                this.Value = Math.Clamp(args.NewValue, this.Minimum, maximum);
            }
        }

        internal static int GetNextValue(int current, int minimum, int maximum, int mouseX, TrackGeometry geometry)
        {
            int travel = geometry.Right - geometry.Left - (geometry.ThumbRight - geometry.ThumbLeft);
            if (travel <= 0 || minimum >= maximum || (mouseX >= geometry.ThumbLeft && mouseX < geometry.ThumbRight))
            {
                return current;
            }
            int direction = mouseX < geometry.ThumbLeft ? -1 : 1;
            double distance = direction < 0 ? (double)geometry.ThumbLeft - mouseX : (double)mouseX - geometry.ThumbRight + 1;
            double rateStep = 0.001 + 0.014 * Math.Clamp(distance / (travel / 2.0), 0.0, 1.0);
            double rate = Math.Clamp(PlaybackRateMapping.MapFactor(current) + direction * rateStep, 0.5, 2.0);
            double position = 500.0 * Math.Log2(rate);
            int next = direction > 0 ? (int)Math.Floor(position) : (int)Math.Ceiling(position);
            next = direction > 0 ? Math.Max(current + 1, next) : Math.Min(current - 1, next);
            double thumbHalfWidth = (geometry.ThumbRight - geometry.ThumbLeft) / 2.0;
            double fraction = Math.Clamp((mouseX - geometry.Left - thumbHalfWidth) / travel, 0.0, 1.0);
            int target = minimum + (int)Math.Round((maximum - minimum) * fraction);
            return Math.Clamp(next, Math.Min(current, target), Math.Max(current, target));
        }

        private void EndTrackFollowing()
        {
            if (!this.followingTrack)
            {
                return;
            }
            this.followingTrack = false;
            this.trackRepeatTimer.Stop();
            this.Capture = false;
            this.OnScroll(new ScrollEventArgs(ScrollEventType.EndScroll, this.Value));
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!this.Capture)
            {
                this.EndTrackFollowing();
            }
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!this.Enabled)
            {
                this.EndTrackFollowing();
                this.controlThumbGesture = false;
            }
            base.OnEnabledChanged(e);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            this.EndTrackFollowing();
            this.controlThumbGesture = false;
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.followingTrack = false;
                this.controlThumbGesture = false;
                this.trackRepeatTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool TryGetGeometry(out TrackGeometry geometry)
        {
            ScrollBarInfo info = new() { Size = Marshal.SizeOf<ScrollBarInfo>() };
            geometry = default;
            if (!GetScrollBarInfo(this.Handle, -4, ref info) || info.ThumbBottom <= info.ThumbTop)
            {
                return false;
            }
            int left = this.PointToClient(new Point(info.Bounds.Left, info.Bounds.Top)).X;
            geometry = new TrackGeometry(left + info.LineButton, left + info.Bounds.Right - info.Bounds.Left - info.LineButton,
                left + info.ThumbTop, left + info.ThumbBottom);
            return true;
        }

        internal readonly record struct TrackGeometry(int Left, int Right, int ThumbLeft, int ThumbRight)
        {
            internal bool IsTrackClick(int mouseX) => mouseX >= this.Left && mouseX < this.Right
                && (mouseX < this.ThumbLeft || mouseX >= this.ThumbRight);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetScrollBarInfo(IntPtr handle, int objectId, ref ScrollBarInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollBarInfo
        {
            public int Size;
            public NativeRect Bounds;
            public int LineButton;
            public int ThumbTop;
            public int ThumbBottom;
            public int Reserved;
            public uint State;
            public uint StartArrowState;
            public uint StartTrackState;
            public uint ThumbState;
            public uint EndTrackState;
            public uint EndArrowState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
