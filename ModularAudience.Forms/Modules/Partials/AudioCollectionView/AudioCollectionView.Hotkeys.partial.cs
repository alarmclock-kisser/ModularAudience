using ModularAudience.Audio;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView
    {
        private void InitializeHotkeyFocusTracking()
        {
            void RememberFocus(object? sender, EventArgs e) => WindowMain.FocusCollectionCommands(this);

            this.Activated += RememberFocus;
            Attach(this);

            void Attach(Control control)
            {
                control.Enter += RememberFocus;
                control.MouseDown += RememberFocus;
                foreach (Control child in control.Controls)
                {
                    Attach(child);
                }
            }
        }

        internal bool TryOpenTrackItem(int index)
        {
            if (index < 0 || index >= this.listBox_audios.Items.Count
                || this.listBox_audios.Items[index] is not AudioObj)
            {
                return false;
            }

            _ = this.OpenTrackItemAsync(index);
            return true;
        }

        private async Task OpenTrackItemAsync(int index)
        {
            this.listBox_audios.SelectedIndex = index;
            if (this.listBox_audios.Items[index] is AudioObj audio)
            {
                _ = new TrackView(audio, this.AudioC);
            }

            await this.AudioC.StopAllAsync().ConfigureAwait(false);
        }
    }
}
