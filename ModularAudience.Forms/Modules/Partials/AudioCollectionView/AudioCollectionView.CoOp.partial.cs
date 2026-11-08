using ModularAudience.Audio;
using ModularAudience.CoOp.Synchronization;
using ModularAudience.Forms.Helpers;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView
    {
        private ToolStripMenuItem? coOpShareMenuItem;
        private Color coOpRestingBorderColor = Color.White;
        private bool coOpShareBusy;

        private partial void InitializeCoOpMenu()
        {
            this.coOpRestingBorderColor = this.FormBorderColor;

            this.coOpShareMenuItem = new ToolStripMenuItem("Share Collection")
            {
                CheckOnClick = false
            };
            this.coOpShareMenuItem.Click += this.CoOpShareMenuItem_Click;

            // Keep the co-op action at the very top so it is reachable even when
            // no track is selected.
            this.contextMenuStrip_audios.Items.Insert(0, new ToolStripSeparator());
            this.contextMenuStrip_audios.Items.Insert(0, this.coOpShareMenuItem);

            WindowMain.CoOpVisualsChanged += this.WindowMain_CoOpVisualsChanged;
            this.RefreshCoOpVisuals();
        }

        private partial void UpdateCoOpMenuState()
        {
            if (this.coOpShareMenuItem == null)
            {
                return;
            }

            CoOpUiController? controller = WindowMain.CoOpController;
            bool sessionActive = controller?.IsSessionActive == true;
            CoOpRemoteCollection? remote = controller?.FindRemoteCollection(this.AudioC);
            bool isShared = controller?.FindLocalShare(this.AudioC) != null;

            this.coOpShareMenuItem.Enabled = sessionActive && remote == null && !this.coOpShareBusy;
            this.coOpShareMenuItem.Checked = isShared || remote?.IsUnshared == false;
        }

        private partial bool CanShowCoOpMenu()
        {
            return WindowMain.CoOpController?.IsSessionActive == true;
        }

        private partial void AdjustCoOpDrawItem(
            AudioObj audio,
            DrawItemEventArgs e,
            ref string nameText,
            ref Color textColor,
            ref int nameLeft)
        {
            CoOpUiController? controller = WindowMain.CoOpController;
            if (controller == null)
            {
                return;
            }

            bool isDeleted = controller.IsAudioDeleted(this.AudioC, audio.Id);
            bool isPending = controller.IsAudioPending(this.AudioC, audio.Id);
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            if (isDeleted)
            {
                string owner = controller.FindRemoteCollection(this.AudioC)?.OwnerDisplayName ?? "owner";
                nameText = $"{nameText} [deleted by {owner}]";
                textColor = isSelected ? Color.OrangeRed : Color.Red;
            }

            if (isPending)
            {
                if (!isDeleted)
                {
                    textColor = isSelected ? Color.Silver : Color.Gray;
                }

                DrawCoOpClock(e.Graphics, nameLeft, e.Bounds, textColor);
                nameLeft += 16;
            }
        }

        private partial void NotifyCoOpClosing()
        {
            WindowMain.CoOpVisualsChanged -= this.WindowMain_CoOpVisualsChanged;

            CoOpUiController? controller = WindowMain.CoOpController;
            CoOpLocalShare? share = controller?.FindLocalShare(this.AudioC);
            if (controller != null && share != null)
            {
                _ = UnshareOnCloseAsync(controller, share.CollectionId);
            }
        }

        private async void CoOpShareMenuItem_Click(object? sender, EventArgs e)
        {
            CoOpUiController? controller = WindowMain.CoOpController;
            if (controller == null || !controller.IsSessionActive || this.coOpShareBusy)
            {
                return;
            }

            if (controller.FindRemoteCollection(this.AudioC) != null)
            {
                // Mirrored collections are shared by their owner. They cannot be
                // offered a second time by this peer.
                return;
            }

            this.coOpShareBusy = true;
            this.UpdateCoOpMenuState();
            try
            {
                await controller.ToggleShareAsync(this.AudioC, this.Text);
            }
            catch (Exception ex)
            {
                LogManager.Log($"Sharing the collection failed: {ex}");
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Co-op sharing failed", ex);
            }
            finally
            {
                this.coOpShareBusy = false;
                if (!this.IsDisposed && !this.Disposing)
                {
                    this.UpdateCoOpMenuState();
                    this.RefreshCoOpVisuals();
                }
            }
        }

        private void WindowMain_CoOpVisualsChanged(object? sender, EventArgs e)
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke((Action)this.RefreshCoOpVisuals);
                }
                catch (InvalidOperationException)
                {
                    // The handle is gone; the view is closing.
                }

                return;
            }

            this.RefreshCoOpVisuals();
        }

        private void RefreshCoOpVisuals()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.ApplyCoOpBorder();
            this.UpdateCoOpMenuState();
            this.listBox_audios.Invalidate();
        }

        private void ApplyCoOpBorder()
        {
            CoOpUiController? controller = WindowMain.CoOpController;
            Color borderColor = this.coOpRestingBorderColor;

            CoOpRemoteCollection? remote = controller?.FindRemoteCollection(this.AudioC);
            if (remote != null)
            {
                borderColor = remote.IsUnshared ? Color.Red : Color.Green;
            }
            else if (controller?.FindLocalShare(this.AudioC) != null)
            {
                borderColor = Color.Green;
            }

            if (this.FormBorderColor != borderColor)
            {
                this.FormBorderColor = borderColor;
            }
        }

        private static void DrawCoOpClock(Graphics graphics, int left, Rectangle bounds, Color color)
        {
            int size = Math.Clamp(bounds.Height - 6, 8, 12);
            int top = bounds.Top + (bounds.Height - size) / 2;
            var clockBounds = new Rectangle(left, top, size, size);
            Point center = new(left + size / 2, top + size / 2);

            SmoothingMode previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using var pen = new Pen(color, 1.4f);
                graphics.DrawEllipse(pen, clockBounds);
                graphics.DrawLine(pen, center, new Point(center.X, top + 2));
                graphics.DrawLine(pen, center, new Point(left + size - 2, center.Y));
            }
            finally
            {
                graphics.SmoothingMode = previousMode;
            }
        }

        private static async Task UnshareOnCloseAsync(CoOpUiController controller, Guid collectionId)
        {
            try
            {
                await controller.UnshareCollectionAsync(collectionId);
            }
            catch (Exception ex)
            {
                LogManager.Log($"Unsharing the collection while closing failed: {ex}");
            }
        }
    }
}
