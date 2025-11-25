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
			if (this.position >= this.data.Length)
			{
				return 0; // Ende des Samples erreicht
			}

			int samplesToRead = (int) Math.Min(count, this.data.Length - this.position);

			// Stelle sicher, dass wir nicht über den Puffer schreiben
			samplesToRead = Math.Min(samplesToRead, count - offset);

			Array.Copy(this.data, this.position, buffer, offset, samplesToRead);
			this.position += samplesToRead;
			return samplesToRead;
		}
	}
}
