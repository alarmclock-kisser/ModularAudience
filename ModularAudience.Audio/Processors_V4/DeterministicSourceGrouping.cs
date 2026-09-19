namespace ModularAudience.Audio.Processors_V4
{
    internal sealed record DeterministicSourceGroup(int[] Components, double Harmonic, double Percussive,
        double Pan, DeterministicSourceDescriptor Descriptor);

    internal static class DeterministicSourceGrouping
    {
        internal static DeterministicSourceGroup[] Create(DeterministicComponentFeatures[] components, CancellationToken token)
        {
            double total = components.Sum(component => component.Energy);
            if (total <= 0) return [];
            // Energy pruning only, not ARD or a Bayesian relevance claim.
            DeterministicComponentFeatures[] retained = components.Where(component => component.Energy > total * 1e-5)
                .OrderByDescending(component => component.Energy).ThenBy(component => component.Index).ToArray();
            List<List<DeterministicComponentFeatures>> groups = [];
            foreach (DeterministicComponentFeatures component in retained)
            {
                token.ThrowIfCancellationRequested();
                // Complete-link grouping avoids chaining incompatible spectra through an intermediate component.
                List<DeterministicComponentFeatures>? match = groups.FirstOrDefault(group => group.All(other => Compatible(component, other, token)));
                if (match is null) groups.Add([component]);
                else match.Add(component);
            }
            List<DeterministicComponentFeatures>[] ordered = groups.OrderByDescending(group => group.Sum(component => component.Energy))
                .ThenBy(group => group.Min(component => component.Index)).ToArray();
            double fittedEnergy = retained.Sum(component => component.Energy);
            DeterministicSourceGroup[] result = new DeterministicSourceGroup[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                result[index] = Describe(ordered[index], index + 1, fittedEnergy);
            }
            return result;
        }

        private static bool Compatible(DeterministicComponentFeatures first, DeterministicComponentFeatures second, CancellationToken token)
        {
            if (Math.Abs(first.Pan - second.Pan) > 0.15) return false;
            if (Math.Abs(first.Harmonic - second.Harmonic) > 0.15 || Math.Abs(first.Percussive - second.Percussive) > 0.15) return false;
            if (!RelatedPitch(first.FundamentalHz, second.FundamentalHz)) return false;
            if (DeterministicSourceFeatures.EnvelopeSimilarity(first.Envelope, second.Envelope) < 0.975) return false;
            return DeterministicSourceFeatures.ActivationCorrelation(first.Activation, second.Activation, token) >= 0.90;
        }

        private static bool RelatedPitch(double first, double second)
        {
            if (first == 0 || second == 0) return first == second;
            double ratio = Math.Max(first, second) / Math.Min(first, second);
            double harmonic = Math.Round(ratio);
            if (harmonic < 1 || harmonic > 4) return false;
            return Math.Abs(1200 * Math.Log2(ratio / harmonic)) <= 35;
        }

        private static DeterministicSourceGroup Describe(List<DeterministicComponentFeatures> components, int id, double total)
        {
            double energy = components.Sum(component => component.Energy);
            double h = Weighted(components, component => component.Harmonic, energy);
            double p = Weighted(components, component => component.Percussive, energy);
            double pan = Weighted(components, component => component.Pan, energy);
            double centroid = Weighted(components, component => component.CentroidHz, energy);
            double pitchScore = Weighted(components, component => component.PitchScore, energy);
            // A representative template pitch, not a monophonic transcription of a whole group.
            double fundamental = components[0].FundamentalHz;
            (string name, string character) = Label(h, p, fundamental, centroid);
            // Confidence is an uncalibrated acoustic-purity heuristic, never a class probability.
            double confidence = Math.Clamp(0.1 + 0.4 * Math.Max(h, p) + 0.2 * Math.Abs(h - p) + 0.15 * pitchScore, 0, 0.85);
            DeterministicSourceDescriptor descriptor = new(id, $"{name} group {id}",
                $"{character}; uncertain, heuristic confidence", confidence, energy / total, fundamental, Math.Clamp(pan, -1, 1));
            return new(components.Select(component => component.Index).OrderBy(index => index).ToArray(), h, p, pan, descriptor);
        }

        private static double Weighted(List<DeterministicComponentFeatures> components,
            Func<DeterministicComponentFeatures, double> selector, double energy)
        {
            double sum = 0;
            foreach (DeterministicComponentFeatures component in components) sum += component.Energy / energy * selector(component);
            return sum;
        }

        private static (string Name, string Character) Label(double harmonic, double percussive, double fundamental, double centroid)
        {
            if (harmonic >= 0.45 && harmonic > 1.4 * percussive)
            {
                return (fundamental > 0 && fundamental < 250) || centroid < 250
                    ? ("Low tonal", "bass-like, sustained/harmonic") : ("Tonal", "sustained/harmonic");
            }
            return percussive >= 0.45 && percussive > 1.4 * harmonic
                ? ("Transient", "transient/percussion-like") : ("Mixed", "diffuse/mixed");
        }
    }
}
