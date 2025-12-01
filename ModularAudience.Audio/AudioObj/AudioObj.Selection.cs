using System;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public partial class AudioObj
    {
        public async Task<AudioObj?> CloneFromSelectionAsync()
        {
            if (this.Data == null || this.Data.LongLength <= 0 || this.SelectionEnd < 0 || this.SelectionStart < 0 || this.SelectionStart == this.SelectionEnd)
            {
                return null;
            }

            if (this.SelectionEnd < this.SelectionStart)
            {
                (this.SelectionStart, this.SelectionEnd) = (this.SelectionEnd, this.SelectionStart);
            }

            int channels = Math.Max(1, this.Channels);
            long totalSamples = this.Data.LongLength;
            long selStartSample = Math.Clamp(this.SelectionStart, 0, totalSamples);
            long selEndSample = Math.Clamp(this.SelectionEnd, 0, totalSamples);
            long selSampleCount = selEndSample - selStartSample;

            if (selSampleCount <= 0)
            {
                return null;
            }

            AudioObj clone = new()
            {
                Name = this.Name + "_selection",
                Data = new float[selSampleCount],
                SampleRate = this.SampleRate,
                Channels = this.Channels,
                BitDepth = this.BitDepth,
                Bpm = this.Bpm,
                Timing = this.Timing,
                Volume = this.Volume,
                Length = selSampleCount,
                Duration = TimeSpan.FromSeconds((double) selSampleCount / (this.SampleRate * channels))
            };

            Buffer.BlockCopy(
                src: this.Data,
                srcOffset: (int) (selStartSample * sizeof(float)),
                dst: clone.Data,
                dstOffset: 0,
                count: (int) (selSampleCount * sizeof(float)));

            await Task.CompletedTask;
            return clone;
        }

        public async Task EraseSelectionAsync(bool inverted = false)
        {
            if (this.Data == null || this.Data.Length == 0 || this.SelectionEnd < 0 || this.SelectionStart < 0 || this.SelectionStart == this.SelectionEnd)
            {
                return;
            }

            if (this.SelectionEnd < this.SelectionStart)
            {
                (this.SelectionStart, this.SelectionEnd) = (this.SelectionEnd, this.SelectionStart);
            }

            long totalSamples = this.Data.LongLength;
            long selStart = Math.Clamp(this.SelectionStart, 0, totalSamples);
            long selEnd = Math.Clamp(this.SelectionEnd, 0, totalSamples);
            long selCount = selEnd - selStart;
            if (selCount <= 0)
            {
                return;
            }

            if (!inverted)
            {
                // Standardverhalten: Auswahl löschen, Rest behalten
                float[] newData = new float[this.Data.Length - selCount];
                await Task.Run(() =>
                {
                    int bytesBefore = checked((int) (selStart * sizeof(float)));
                    int srcAfterOffset = checked((int) (selEnd * sizeof(float)));
                    int bytesAfter = checked((int) ((totalSamples - selEnd) * sizeof(float)));
                    int dstAfterOffset = bytesBefore;

                    if (bytesBefore > 0)
                    {
                        Buffer.BlockCopy(this.Data, 0, newData, 0, bytesBefore);
                    }

                    if (bytesAfter > 0)
                    {
                        Buffer.BlockCopy(this.Data, srcAfterOffset, newData, dstAfterOffset, bytesAfter);
                    }
                }).ConfigureAwait(false);

                this.Data = newData;
            }
            else
            {
                // Inverted: alles außer Auswahl löschen -> behalten nur die Auswahl
                float[] newData = new float[selCount];
                await Task.Run(() =>
                {
                    int srcOffset = checked((int) (selStart * sizeof(float)));
                    int copyBytes = checked((int) (selCount * sizeof(float)));
                    if (copyBytes > 0)
                    {
                        Buffer.BlockCopy(this.Data, srcOffset, newData, 0, copyBytes);
                    }
                }).ConfigureAwait(false);

                this.Data = newData;
            }

            int channels = Math.Max(1, this.Channels);
            this.Length = this.Data.Length;
            this.Duration = TimeSpan.FromSeconds((double) this.Data.Length / (this.SampleRate * channels));
            // Auswahl zurücksetzen - TrackView/Callees erwarten meist Selection cleared after erase
            this.SelectionStart = -1;
            this.SelectionEnd = -1;
        }
    }
}
