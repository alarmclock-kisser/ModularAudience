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
        private int? lastPosition;
        private int? startRatePosition; // Rate position when dragging starts (for relative dragging)

        public event EventHandler<PlaylistTrackRateChangedEventArgs>? RatePositionChanged;

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
            new((short) message.LParam.ToInt64(), (short) (message.LParam.ToInt64() >> 16));

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
            this.lastPosition = null;
            this.startRatePosition = this.rateDragAllowed
                ? MapRatePosition(point.X, this.interactionTextBounds.Left, this.interactionTextBounds.Right - 1)
                : null;
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
            else if ((ModifierKeys & Keys.Control) != 0)
            {
                this.rateDragAllowed = false;
                this.RaiseRatePositionChanged(0);
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
                if (this.draggingRate || Math.Abs((long) e.X - this.mouseDownX) >= threshold)
                {
                    this.draggingRate = true;

                    int currentMapPos = MapRatePosition(e.X,
                        this.interactionTextBounds.Left, this.interactionTextBounds.Right - 1);
                    int relativePosition = currentMapPos - (this.startRatePosition ?? currentMapPos);
                    this.RaiseRatePositionChanged(Math.Clamp(relativePosition, -500, 500));
                }
            }
            base.OnMouseMove(e);
        }

        // The row width maps to 50%..200% (0.5x..2.0x). The midpoint is 100% (1.0x).
        // Dragging accumulates relative deltas, so the full 1%..1000% range is reachable.
        internal static int MapRatePosition(int mouseX, int textLeft, int textRight)
        {
            if (textRight <= textLeft)
            {
                return 0;
            }

            double totalWidth = textRight - textLeft;
            double midpoint = textLeft + totalWidth / 2;
            double positionFromMid = mouseX - midpoint;

            // One row width spans -500..+500. Positions outside the row remain usable
            // so a relative grab-drag can continue across repeated gestures.
            return (int)Math.Round(1000.0 * positionFromMid / totalWidth);
        }

        private void RaiseRatePositionChanged(int position)
        {
            if (!this.IsInteracting || this.interactionRowIndex < 0 || this.interactionRowIndex >= this.Items.Count
                || this.lastPosition == position)
            {
                return;
            }
            this.lastPosition = position;
            this.RatePositionChanged?.Invoke(this, new PlaylistTrackRateChangedEventArgs(this.interactionRowIndex, position));
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
            this.IsInteracting = false;
            this.interactionRowIndex = -1;
            this.rateDragAllowed = false;
            this.draggingRate = false;
            this.lastPosition = null;
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
        public PlaylistTrackRateChangedEventArgs(int rowIndex, int position)
        {
            this.RowIndex = rowIndex;
            this.Position = position;
        }

        public int RowIndex { get; }
        public int Position { get; }
    }
}
