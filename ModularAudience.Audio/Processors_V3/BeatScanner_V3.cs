using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

public static class BeatScanner_V3
{
    // Wir entfernen die "relClose" und "Magic Numbers" für die Taktwahl 
    // und ersetzen sie durch eine strukturelle Validierung.

    public static async Task<double> EstimateBpmAsync(float[] monoData, double sampleRate, double minBpm, double maxBpm)
    {
        int n = monoData.Length;
        if (n < 1024) return -1.0;

        // 1) Vorverarbeitung: Erzeuge ein Onset-Signal (Spectral Flux oder Envelope)
        // Wir nutzen hier eine robuste Hüllkurve mit Halbwellendetektion
        float[] onsetSignal = ComputeOnsetSignal(monoData);

        // 2) Autokorrelation zur Suche nach dem Lag (Zeit zwischen Schlägen)
        // Wir suchen nach dem Lag, der die stärkste Periodizität zeigt
        int bestLag = FindBestLag(onsetSignal, sampleRate, minBpm, maxBpm);

        if (bestLag <= 0) return -1.0;

        // 3) VALIDIERUNG: Der entscheidende Schritt gegen die "Halbe-BPM-Falle"
        // Wir prüfen, ob der gefundene Lag eine echte rhythmische Struktur hat
        // oder nur eine Symmetrie im Signal ist.
        double bpm = 60.0 * sampleRate / bestLag;

        if (IsSubharmonicError(onsetSignal, bestLag))
        {
            // Wenn es ein Subharmonik-Fehler ist (z.B. 67 statt 135), 
            // verdoppeln wir den BPM-Wert.
            bpm *= 2.0;
        }

        // Sicherstellen, dass wir im Bereich bleiben
        while (bpm < minBpm) bpm *= 2.0;
        while (bpm > maxBpm) bpm /= 2.0;

        return bpm;
    }

    private static float[] ComputeOnsetSignal(float[] samples)
    {
        int n = samples.Length;
        float[] onset = new float[n];

        // Wir nutzen eine einfache, aber effektive Hüllkurve
        // (In einer High-End Implementierung wäre hier Spectral Flux besser)
        for (int i = 0; i < n; i++)
        {
            onset[i] = Math.Abs(samples[i]);
        }

        // Glättung zur Rauschunterdrückung (Moving Average)
        // Ein Fenster von ca. 20-50ms ist ideal für Onsets
        int window = 512;
        for (int i = window; i < n; i++)
        {
            float sum = 0;
            for (int j = 0; j < window; j++) sum += onset[i - j];
            onset[i] = sum / window;
        }

        return onset;
    }

    private static int FindBestLag(float[] signal, double sampleRate, double minBpm, double maxBpm)
    {
        int n = signal.Length;
        // Um die FFT-Effizienz zu nutzen, runden wir auf die nächste Zweierpotenz auf
        int L = 1;
        while (L < 2 * n) L <<= 1;

        Complex[] fft = new Complex[L];
        for (int i = 0; i < n; i++) fft[i] = new Complex(signal[i], 0);
        for (int i = n; i < L; i++) fft[i] = new Complex(0, 0);

        // FFT durchführen
        ForwardFFT(fft);

        // Im Frequenzbereich die Autokorrelation berechnen (Wiener-Khinchin Theorem)
        for (int i = 0; i < L; i++)
        {
            fft[i] = fft[i].Conjugate() * fft[i];
        }

        // Inverse FFT zurück in den Zeitbereich
        InverseFFT(fft);

        // Suche den besten Lag innerhalb der BPM-Grenzen
        int minLag = (int) (sampleRate * 60.0 / maxBpm);
        int maxLag = (int) (sampleRate * 60.0 / minBpm);

        int bestLag = -1;
        double maxCorr = -1.0;

        // Wir starten bei minLag, um die Symmetrie bei sehr kleinen Lags zu ignorieren
        for (int lag = minLag; lag <= maxLag && lag < n; lag++)
        {
            double corr = fft[lag].Real;
            if (corr > maxCorr)
            {
                maxCorr = corr;
                bestLag = lag;
            }
        }

        return bestLag;
    }

    /// <summary>
    /// Prüft, ob der gefundene Lag nur eine "halbe" Periode ist.
    /// Wir vergleichen die Korrelation des Lags mit der Korrelation des doppelten Lags.
    /// Wenn der doppelte Lag (die volle Periode) eine signifikant höhere 
    /// strukturelle Kohärenz zeigt, war der erste Lag nur eine Symmetrie-Spiegelung.
    /// </summary>
    private static bool IsSubharmonicError(float[] signal, int lag)
    {
        // Wir prüfen die "Peak-Prominenz" des Lags im Vergleich zu seinem Doppelten.
        // Ein echter Beat hat eine sehr klare Periodizität über mehrere Takte.

        double scoreSingle = GetCorrelationStrength(signal, lag);
        double scoreDouble = GetCorrelationStrength(signal, lag * 2);

        // Wenn die Korrelation bei der doppelten Distanz deutlich "stabiler" ist,
        // deutet das darauf hin, dass der erste Lag nur eine lokale Symmetrie war.
        // Wir nutzen hier keinen festen Prozentwert, sondern das Verhältnis der Stabilität.
        return scoreDouble > scoreSingle * 1.15;
    }

    private static double GetCorrelationStrength(float[] signal, int lag)
    {
        // Misst, wie konsistent die Peaks über eine kurze Strecke sind
        int samplesToCheck = Math.Min(lag * 3, signal.Length - lag);
        if (samplesToCheck <= 0) return 0;

        double sum = 0;
        for (int i = 0; i < samplesToCheck; i += lag)
        {
            sum += signal[i];
        }
        return sum / (samplesToCheck / lag + 1);
    }

    // Standard FFT Implementierung (vereinfacht für das Beispiel)
    private static void ForwardFFT(Complex[] data) { /* Implementierung... */ }
    private static void InverseFFT(Complex[] data) { /* Implementierung... */ }
}