using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Text;

namespace ModularAudience.Audio
{
    public class SampleData : ISampleProvider
    {
        private readonly float[] data;
        private long position; // in Samples (floats)
        private readonly WaveFormat waveFormat;

        public SampleData(float[] data, int sampleRate, int channels)
        {
            // Erstellt eine Kopie des Daten-Arrays für den Mixer, um Race-Conditions zu vermeiden, 
            // falls das Original-Array während des Spielens verändert wird.
            // *Hinweis: Für sehr große Samples wäre dies ineffizient, aber für kurze Drum-Hits (Samples) ist es der beste Weg.*
            this.data = data.ToArray();
            this.waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            this.position = 0;
        }

        public WaveFormat WaveFormat => this.waveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (this.position >= this.data.Length)
            {
                return 0;
            }

            // wieviel Samples sind im Source noch verfügbar
            int availableInSource = (int) Math.Min((long) count, this.data.Length - this.position);

            // wieviel Platz ist im Zielpuffer ab 'offset'
            int availableInBuffer = buffer.Length - offset;

            // tatsächlich zu kopierende Samples (>= 0)
            int samplesToRead = Math.Max(0, Math.Min(availableInSource, availableInBuffer));

            if (samplesToRead == 0)
            {
                return 0;
            }

            Array.Copy(this.data, (int) this.position, buffer, offset, samplesToRead);
            this.position += samplesToRead;
            return samplesToRead;
        }
    }
}
