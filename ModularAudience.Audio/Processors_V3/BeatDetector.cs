using System;
using System.Linq;
using System.Threading.Tasks;
using ModularAudience.Audio;

public static class BeatDetector
{
    public static async Task<double> ScanBpmAsync(AudioObj audio)
    {
        if (audio == null || audio.Data == null || audio.Data.Length == 0)
        {
            throw new ArgumentException("Invalid audio data.");
        }

        // Bestimme die Hüllkurve (Envelope) und deren Samplingrate
        var envelope = await CalculateEnvelopeAsync(audio);
        var bpm = EstimateBpmFromEnvelope(envelope, audio.SampleRate);

        return bpm;
    }

    public static async Task<bool[]> GenerateBeatGrid(AudioObj audio, int granularity = 4)
    {
        if (audio == null || audio.Data == null || audio.Data.Length == 0)
        {
            throw new ArgumentException("Invalid audio data.");
        }

        // Schritt 1: BPM bestimmen
        double bpm = await ScanBpmAsync(audio);
        if (bpm == -1.0)
        {
            throw new Exception("Failed to detect BPM.");
        }

        // Schritt 2: Hüllkurve des Tracks berechnen
        var envelope = await CalculateEnvelopeAsync(audio);
        int sampleRate = audio.SampleRate;
        int totalFrames = audio.Data.Length / audio.Channels;

        // Schritt 3: Schätzungen der Intervalllänge (Frames pro Beat)
        int intervalFrames = (int) (sampleRate * 60.0 / bpm);  // Frames pro Schlag
        intervalFrames = Math.Max(1, (intervalFrames / granularity) * granularity);  // Granularität berücksichtigen

        // Schritt 4: BeatGrid erzeugen
        bool[] beatGrid = new bool[totalFrames];
        FillBeatGrid(beatGrid, intervalFrames, totalFrames);

        // Schritt 5: Snappe Beats auf Hüllkurven Peaks
        SnapBeatsToEnvelopePeaks(beatGrid, envelope);

        return beatGrid;
    }

    private static async Task<float[]> CalculateEnvelopeAsync(AudioObj audio)
    {
        // Hüllkurve berechnen: Durchschnittliche Amplitude des Audiosignals (Mono)
        int totalFrames = audio.Data.Length / audio.Channels;
        var envelope = new float[totalFrames];

        await Task.Run(() =>
        {
            for (int i = 0; i < totalFrames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < audio.Channels; c++)
                {
                    sum += Math.Abs(audio.Data[i * audio.Channels + c]);
                }
                envelope[i] = sum / audio.Channels;
            }
        });

        return envelope;
    }

    private static double EstimateBpmFromEnvelope(float[] envelope, int sampleRate)
    {
        // Dynamische Bestimmung von windowSize und peakThreshold
        int windowSize = CalculateWindowSize(envelope.Length, sampleRate);
        int peakThreshold = CalculatePeakThreshold(envelope.Length);

        // Peaks der Envelope bestimmen
        var peaks = new bool[envelope.Length];
        for (int i = windowSize; i < envelope.Length - windowSize; i++)
        {
            if (envelope[i] > envelope[i - windowSize] && envelope[i] > envelope[i + windowSize])
            {
                peaks[i] = true;
            }
        }

        // BPM durch Zählen der Perioden zwischen den Peaks berechnen
        int peakCount = peaks.Count(p => p);
        if (peakCount == 0)
        {
            return -1.0;  // Keine Beats gefunden
        }

        int intervalSum = 0;
        int lastPeak = -1;

        for (int i = 0; i < envelope.Length; i++)
        {
            if (peaks[i])
            {
                if (lastPeak != -1)
                {
                    intervalSum += i - lastPeak;
                }
                lastPeak = i;
            }
        }

        // Durchschnittliches Intervall zwischen den Peaks
        double avgInterval = (double) intervalSum / peakCount;
        double bpm = 60.0 * sampleRate / avgInterval;

        return bpm;
    }



    private static void FillBeatGrid(bool[] beatGrid, int intervalFrames, int totalFrames)
    {
        int currentPosition = 0;
        while (currentPosition < totalFrames)
        {
            beatGrid[currentPosition] = true;
            currentPosition += intervalFrames;
        }
    }

    private static void SnapBeatsToEnvelopePeaks(bool[] beatGrid, float[] envelope)
    {
        int maxShiftFrames = envelope.Length / 10;  // Maximale Verschiebung der Beats
        for (int i = 0; i < beatGrid.Length; i++)
        {
            if (beatGrid[i])
            {
                int peakIdx = FindPeakInEnvelope(envelope, i, maxShiftFrames);
                if (peakIdx != -1)
                {
                    beatGrid[i] = false;
                    beatGrid[peakIdx] = true;
                }
            }
        }
    }

    private static int FindPeakInEnvelope(float[] envelope, int currentIdx, int maxShiftFrames)
    {
        float maxPeak = 0;
        int peakIdx = -1;

        for (int i = currentIdx - maxShiftFrames; i < currentIdx + maxShiftFrames; i++)
        {
            if (i >= 0 && i < envelope.Length && envelope[i] > maxPeak)
            {
                maxPeak = envelope[i];
                peakIdx = i;
            }
        }
        return peakIdx;
    }



    private static int CalculateWindowSize(int envelopeLength, int sampleRate)
    {
        // Dynamische Berechnung von windowSize basierend auf der Audio-Länge und Sample-Rate
        // Größeres windowSize für längere Audiodaten oder niedrigere Samplingraten
        int dynamicWindowSize = Math.Max(512, envelopeLength / (sampleRate / 1000));  // Dynamisch skalierbar, z.B. 1ms Proben
        return Math.Min(dynamicWindowSize, envelopeLength / 2);  // Sicherstellen, dass windowSize nicht zu groß wird
    }

    private static int CalculatePeakThreshold(int envelopeLength)
    {
        // Berechnung eines dynamischen peakThreshold basierend auf der Hüllkurve
        // Hier verwenden wir eine einfache Heuristik basierend auf den Extremen der Envelope
        int threshold = (int) (envelopeLength * 0.05);  // Einfacher Wert (5% der gesamten Länge)
        return Math.Max(threshold, 2);  // Minimalwert, damit es keine zu niedrige Schwelle gibt
    }







}
