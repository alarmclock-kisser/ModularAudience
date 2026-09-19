namespace ModularAudience.Audio.Processors_V4
{
    public enum InstrumentEnsembleMode { Automatic, ProfilesOnly, ProfilesAndAutomatic }

    public enum InstrumentProfileId
    {
        SynthBass, BassGuitar, Drums, Kick, Snare, HiHatCymbals, Vocals, SynthLead,
        SynthPad, Piano, Guitar, Strings, Brass, Woodwinds, Organ, Mallets
    }

    public sealed record InstrumentProfile(InstrumentProfileId Id, string Name,
        double MinimumPitchHz, double MaximumPitchHz, double BrightnessHz,
        double Harmonic, double Percussive, double Noisiness)
    {
        public override string ToString() => this.Name;

        internal double SpectralWeight(double frequency)
        {
            if (frequency <= 0) return 0.02;
            double distance = Math.Log2(frequency / this.BrightnessHz);
            return 0.02 + 0.98 * Math.Exp(-0.5 * distance * distance / 2.25);
        }
    }

    public static class InstrumentProfileCatalog
    {
        public static IReadOnlyList<InstrumentProfile> All { get; } = Array.AsReadOnly<InstrumentProfile>(
        [
            new(InstrumentProfileId.SynthBass, "Synth Bass", 25, 300, 180, 0.90, 0.10, 0.10),
            new(InstrumentProfileId.BassGuitar, "Bass Guitar", 30, 350, 300, 0.80, 0.30, 0.15),
            new(InstrumentProfileId.Drums, "Drums", 30, 300, 1800, 0.15, 0.95, 0.75),
            new(InstrumentProfileId.Kick, "Kick", 25, 180, 90, 0.25, 0.95, 0.25),
            new(InstrumentProfileId.Snare, "Snare", 100, 450, 2300, 0.10, 0.95, 0.90),
            new(InstrumentProfileId.HiHatCymbals, "Hi-hat / Cymbals", 1000, 12000, 7500, 0.05, 0.85, 0.95),
            new(InstrumentProfileId.Vocals, "Vocals", 65, 1200, 1400, 0.70, 0.20, 0.35),
            new(InstrumentProfileId.SynthLead, "Synth Lead", 100, 3000, 2000, 0.85, 0.20, 0.15),
            new(InstrumentProfileId.SynthPad, "Synth Pad", 60, 2000, 1000, 0.95, 0.05, 0.25),
            new(InstrumentProfileId.Piano, "Piano", 27.5, 4200, 1600, 0.80, 0.65, 0.15),
            new(InstrumentProfileId.Guitar, "Guitar", 65, 1400, 1200, 0.80, 0.55, 0.25),
            new(InstrumentProfileId.Strings, "Strings", 32, 3500, 1800, 0.95, 0.10, 0.30),
            new(InstrumentProfileId.Brass, "Brass", 45, 2000, 2200, 0.85, 0.25, 0.20),
            new(InstrumentProfileId.Woodwinds, "Woodwinds", 55, 2500, 1000, 0.85, 0.15, 0.30),
            new(InstrumentProfileId.Organ, "Organ", 25, 4000, 850, 0.98, 0.05, 0.05),
            new(InstrumentProfileId.Mallets, "Mallets / Bells", 100, 4000, 3000, 0.65, 0.75, 0.25)
        ]);

        public static InstrumentProfile Get(InstrumentProfileId id)
            => All.First(profile => profile.Id == id);
    }
}
