namespace ModularAudience.Audio.Effects.AutoTune;

internal static class AutoTunePitchMap
{
    internal static int AllowedMask(AutoTuneSettings settings)
    {
        if (settings.Scale == AutoTuneScale.Custom) return settings.CustomNotesMask;
        int[] intervals = settings.Scale switch
        {
            AutoTuneScale.Chromatic => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
            AutoTuneScale.Major => [0, 2, 4, 5, 7, 9, 11],
            AutoTuneScale.NaturalMinor => [0, 2, 3, 5, 7, 8, 10],
            AutoTuneScale.HarmonicMinor => [0, 2, 3, 5, 7, 8, 11],
            AutoTuneScale.MelodicMinor => [0, 2, 3, 5, 7, 9, 11],
            AutoTuneScale.MajorPentatonic => [0, 2, 4, 7, 9],
            AutoTuneScale.MinorPentatonic => [0, 3, 5, 7, 10],
            AutoTuneScale.Blues => [0, 3, 5, 6, 7, 10],
            _ => throw new ArgumentException("Unknown scale.")
        };
        int mask = 0;
        foreach (int interval in intervals) mask |= 1 << ((settings.RootNote + interval) % 12);
        return mask;
    }

    internal static double Nearest(double midiNote, int mask)
    {
        if (!double.IsFinite(midiNote) || mask is <= 0 or > 4095) throw new ArgumentException("Invalid note or pitch-class mask.");
        int center = (int)Math.Floor(midiNote);
        int best = center;
        double distance = double.PositiveInfinity;
        for (int candidate = center - 12; candidate <= center + 12; candidate++)
        {
            int pitchClass = (candidate % 12 + 12) % 12;
            double next = Math.Abs(midiNote - candidate);
            if ((mask & (1 << pitchClass)) != 0 && next < distance)
            {
                best = candidate;
                distance = next;
            }
        }
        return best;
    }

    internal static double Nearest(double midiNote, AutoTuneSettings settings)
    {
        if (!double.IsFinite(midiNote)) throw new ArgumentException("Invalid note.");
        if (settings.PitchGridSemitones == 1) return Nearest(midiNote, AllowedMask(settings));

        double gridPosition = (midiNote - settings.RootNote) / settings.PitchGridSemitones;
        double lower = Math.Floor(gridPosition);
        double nearest = gridPosition - lower <= 0.5 ? lower : lower + 1;
        return settings.RootNote + nearest * settings.PitchGridSemitones;
    }
}
