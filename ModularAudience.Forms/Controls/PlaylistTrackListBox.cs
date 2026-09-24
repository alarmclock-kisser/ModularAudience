using System.Windows.Forms.VisualStyles;

namespace ModularAudience.Forms.Controls
{
    internal sealed class PlaylistTrackListBox : CheckedListBox
    {
        private const int LeftButtonDown = 0x0201;
        private const int LeftButtonUp = 0x0202;
        private const int LeftButtonDoubleClick = 0x0203;
        private const int MouseMoveMessage = 0x0200;
        private const int CancelMode = 0x001F;
        private int interactionRowIndex = -1;
        private int mouseDownX;
        private Rectangle interactionTextBounds;
        private bool rateDragAllowed;
        private bool draggingRate;
        private bool controlRateGesture;
        private bool shiftRateGesture;
        private int lastMouseX;
        private int dragPixels;
        private int lastMappedPosition;

        public event EventHandler<PlaylistTrackRateChangedEventArgs>? RatePositionChanged;
        public event EventHandler? RateInteractionEnded;

        public bool IsInteracting { get; private set; }

        protected override void WndProc(ref Message message)
        {
            if ((message.Msg == LeftButtonDown || message.Msg == LeftButtonDoubleClick)
                && this.HandleLeftButtonDown(GetMessagePoint(message), message.Msg == LeftButtonDoubleClick ? 2 : 1))
            {
                return;
            }
            if (this.IsInteracting && (message.Msg == MouseMoveMessage || message.Msg == LeftButtonUp))
            {
                Point point = GetMessagePoint(message);
                MouseEventArgs args = new(MouseButtons.Left, message.Msg == LeftButtonUp ? 1 : 0, point.X, point.Y, 0);
                if (message.Msg == LeftButtonUp)
                {
                    this.OnMouseUp(args);
                }
                else
                {
                    this.OnMouseMove(args);
                }
                return;
            }
            if (message.Msg == CancelMode)
            {
                this.EndInteraction();
            }
            base.WndProc(ref message);
        }

        private static Point GetMessagePoint(Message message) =>
            new((short)message.LParam.ToInt64(), (short)(message.LParam.ToInt64() >> 16));

        private bool HandleLeftButtonDown(Point point, int clicks)
        {
            if (!this.Enabled || !this.ClientRectangle.Contains(point))
            {
                return false;
            }
            if (this.IsInteracting)
            {
                return true;
            }
            int rowIndex = this.IndexFromPoint(point);
            if (rowIndex >= 0 && !this.GetItemRectangle(rowIndex).Contains(point))
            {
                rowIndex = -1;
            }
            this.BeginInteraction(point, rowIndex);
            if (this.IsInteracting)
            {
                this.Focus();
                this.SelectInteractionRow();
            }
            if (this.IsInteracting)
            {
                this.OnMouseDown(new MouseEventArgs(MouseButtons.Left, clicks, point.X, point.Y, 0));
            }
            return true;
        }

        private void BeginInteraction(Point point, int rowIndex)
        {
            this.interactionRowIndex = rowIndex;
            this.interactionTextBounds = rowIndex >= 0 ? this.GetTextBounds(rowIndex) : Rectangle.Empty;
            this.mouseDownX = point.X;
            this.rateDragAllowed = this.interactionTextBounds.Contains(point);
            this.draggingRate = false;
            this.controlRateGesture = (ModifierKeys & Keys.Control) != 0;
            this.shiftRateGesture = (ModifierKeys & Keys.Shift) != 0;
            this.lastMouseX = point.X;
            this.dragPixels = 0;
            this.lastMappedPosition = 0;
            this.IsInteracting = true;
            this.Capture = true;
            if (!this.Capture)
            {
                this.EndInteraction();
            }
        }

        private void SelectInteractionRow()
        {
            if (!this.IsInteracting || this.interactionRowIndex < 0 || this.interactionRowIndex >= this.Items.Count)
            {
                return;
            }
            this.SelectedIndex = this.interactionRowIndex;
            if (!this.IsInteracting)
            {
                return;
            }
            if (!this.rateDragAllowed)
            {
                this.ToggleItem(this.interactionRowIndex);
            }
        }

        private Rectangle GetTextBounds(int rowIndex)
        {
            Rectangle bounds = this.GetItemRectangle(rowIndex);
            using Graphics graphics = this.CreateGraphics();
            int glyphWidth = Application.RenderWithVisualStyles
                ? CheckBoxRenderer.GetGlyphSize(graphics, CheckBoxState.UncheckedNormal).Width
                : 13;
            int checkboxWidth = glyphWidth + 2 * this.LogicalToDeviceUnits(1);
            if (this.RightToLeft != RightToLeft.Yes)
            {
                bounds.X += checkboxWidth;
            }
            bounds.Width = Math.Max(0, bounds.Width - checkboxWidth);
            return Rectangle.Intersect(bounds, this.ClientRectangle);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (this.IsInteracting && this.rateDragAllowed)
            {
                int threshold = Math.Max(1, SystemInformation.DragSize.Width / 2);
                if (this.draggingRate || Math.Abs((long)e.X - this.mouseDownX) >= threshold)
                {
                    this.draggingRate = true;

                    int totalWidth = Math.Max(1, this.interactionTextBounds.Width - 1);
                    this.dragPixels += e.X - this.lastMouseX;
                    this.lastMouseX = e.X;
                    int mappedPosition = MapRatePosition(this.dragPixels, totalWidth);
                    int relativePosition = mappedPosition - this.lastMappedPosition;
                    this.lastMappedPosition = mappedPosition;
                    if (relativePosition != 0)
                    {
                        this.RaiseRatePositionChanged(relativePosition);
                    }
                }
            }
            base.OnMouseMove(e);
        }

        // Dragging is relative. Five row widths to the right reach 1000%; two row
        // widths to the left reach approximately 1%.
        internal static int MapRatePosition(int dragPixels, int rowWidth)
        {
            if (rowWidth <= 0 || dragPixels == 0)
            {
                return 0;
            }

            double unitsPerRowWidth = dragPixels >= 0
                ? 500.0 * Math.Log2(10.0) / 5.0
                : 500.0 * Math.Log2(100.0) / 2.0;
            double mapped = unitsPerRowWidth * dragPixels / rowWidth;
            return (int)Math.Round(Math.Clamp(mapped,
                500.0 * Math.Log2(0.01),
                500.0 * Math.Log2(10.0)));
        }

        private void RaiseRatePositionChanged(int position)
        {
            if (!this.IsInteracting || this.interactionRowIndex < 0 || this.interactionRowIndex >= this.Items.Count
                || position == 0)
            {
                return;
            }
            this.RatePositionChanged?.Invoke(this,
                new PlaylistTrackRateChangedEventArgs(this.interactionRowIndex, position,
                    shiftAll: this.shiftRateGesture));
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == ' ')
            {
                e.Handled = true;
                if (!this.IsInteracting && this.SelectionMode != SelectionMode.None)
                {
                    this.ToggleItem(this.SelectedIndex);
                }
                return;
            }
            base.OnKeyPress(e);
        }

        private void ToggleItem(int rowIndex)
        {
            if (rowIndex >= 0 && rowIndex < this.Items.Count)
            {
                this.SetItemChecked(rowIndex, !this.GetItemChecked(rowIndex));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.EndInteraction();
            }
            base.OnMouseUp(e);
        }

        private void EndInteraction()
        {
            if (!this.IsInteracting)
            {
                return;
            }
            int endedRowIndex = this.interactionRowIndex;
            bool resetRate = this.rateDragAllowed && !this.draggingRate && this.controlRateGesture;
            this.IsInteracting = false;
            this.interactionRowIndex = -1;
            this.rateDragAllowed = false;
            this.draggingRate = false;
            this.controlRateGesture = false;
            this.shiftRateGesture = false;
            this.dragPixels = 0;
            this.lastMappedPosition = 0;
            if (resetRate)
            {
                this.RatePositionChanged?.Invoke(this,
                    new PlaylistTrackRateChangedEventArgs(endedRowIndex, 0));
            }
            this.RateInteractionEnded?.Invoke(this, EventArgs.Empty);
            this.Capture = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!this.Capture)
            {
                this.EndInteraction();
            }
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!this.Enabled)
            {
                this.EndInteraction();
            }
            base.OnEnabledChanged(e);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            this.EndInteraction();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.EndInteraction();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class PlaylistTrackRateChangedEventArgs : EventArgs
    {
        public PlaylistTrackRateChangedEventArgs(int rowIndex, int position, bool resetAll = false, bool shiftAll = false)
        {
            this.RowIndex = rowIndex;
            this.Position = position;
            this.ResetAll = resetAll;
            this.ShiftAll = shiftAll;
        }

        public int RowIndex { get; }
        public int Position { get; }
        public bool ResetAll { get; }
        public bool ShiftAll { get; }
    }
}
