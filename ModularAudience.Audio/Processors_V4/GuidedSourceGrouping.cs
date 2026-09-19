using System;
using System.Collections.Generic;
using System.Linq;

namespace ModularAudience.Audio.Processors_V4
{
    internal static class GuidedSourceGrouping
    {
        /// <summary>
        /// Assigns weighted component membership using instrument profile priors.
        /// ProfilesOnly emits requested profile targets plus Residual (unmatched components stay at weight 0 in all groups).
        /// ProfilesAndAutomatic adds extra stable automatic groups for unmatched material without double-counting.
        /// The Automatic path is untouched; this method is only called for guided ensemble modes.
        /// </summary>
        internal static DeterministicSourceGroup[] Create(DeterministicComponentFeatures[] components,
            IReadOnlyList<InstrumentProfile> profiles, InstrumentEnsembleMode mode, CancellationToken token)
        {
            double total = components.Sum(component => component.Energy);
            if (total <= 0 || profiles.Count == 0) return [];

            DeterministicComponentFeatures[] retained = components.Where(component => component.Energy > total * 1e-5)
                .OrderByDescending(component => component.Energy).ThenBy(component => component.Index).ToArray();

            // Score each retained component against every profile using pitch, brightness, HPR and noise evidence.
            double[,] scores = new double[retained.Length, profiles.Count];
            for (int c = 0; c < retained.Length; c++)
            {
                token.ThrowIfCancellationRequested();
                DeterministicComponentFeatures component = retained[c];
                for (int p = 0; p < profiles.Count; p++)
                    scores[c, p] = ProfileCompatibility(component, profiles[p]);
            }

            // Per-component weights: normalize the profile score vector so memberships sum to at most 1.
            double[][] weights = new double[profiles.Count][];
            for (int p = 0; p < profiles.Count; p++) weights[p] = new double[retained.Length];
            for (int c = 0; c < retained.Length; c++)
            {
                double maxScore = scores[c, 0];
                for (int p = 1; p < profiles.Count; p++) maxScore = Math.Max(maxScore, scores[c, p]);
                if (maxScore <= 0.05) continue; // below the ambiguity threshold: leave for Residual / automatic groups
                double sum = 0;
                for (int p = 0; p < profiles.Count; p++) sum += Math.Max(0, scores[c, p] - 0.02);
                if (sum <= 0) continue;
                for (int p = 0; p < profiles.Count; p++)
                    weights[p][c] = Math.Max(0, scores[c, p] - 0.02) / sum;
            }

            // Build one group per requested profile with its weighted members.
            List<DeterministicSourceGroup> result = new();
            HashSet<int> assigned = [];
            for (int p = 0; p < profiles.Count; p++)
            {
                token.ThrowIfCancellationRequested();
                InstrumentProfile profile = profiles[p];
                List<int> memberIndices = [];
                double energy = 0, h = 0, perc = 0, pan = 0;
                for (int c = 0; c < retained.Length; c++)
                {
                    if (weights[p][c] <= 1e-4) continue;
                    memberIndices.Add(retained[c].Index);
                    double w = weights[p][c];
                    energy += w * retained[c].Energy;
                    h += w * retained[c].Harmonic;
                    perc += w * retained[c].Percussive;
                    pan += w * retained[c].Pan;
                }

                if (memberIndices.Count == 0) continue; // profile matched nothing: skip, material stays in Residual
                memberIndices.Sort();
                double fittedEnergy = energy > 0 ? energy : 1e-300;
                double confidence = Math.Clamp(energy / total * 0.6 + 0.25, 0, 0.85);
                string character = DescribeCharacter(profile, h / fittedEnergy, perc / fittedEnergy);
                DeterministicSourceDescriptor descriptor = new(p + 1, profile.Name,
                    $"{character}; guided profile, heuristic confidence", confidence, energy / total,
                    representativePitch(retained, memberIndices, weights[p]), Math.Clamp(pan / fittedEnergy, -1, 1));
                result.Add(new(memberIndices.ToArray(), h / fittedEnergy, perc / fittedEnergy, pan / fittedEnergy,
                    descriptor, WeightsForProfile(weights[p], retained)));
                foreach (int index in memberIndices) assigned.Add(index);
            }

            // ProfilesAndAutomatic: add stable automatic groups for components not meaningfully claimed by any profile.
            if (mode == InstrumentEnsembleMode.ProfilesAndAutomatic)
            {
                List<DeterministicComponentFeatures> unmatched = retained
                    .Where(component => !assigned.Contains(component.Index))
                    .OrderByDescending(component => component.Energy).ThenBy(component => component.Index)
                    .ToList();
                if (unmatched.Count > 0)
                {
                    DeterministicSourceGroup[] automatic = DeterministicSourceGrouping.Create(unmatched.ToArray(), token);
                    int nextId = result.Count;
                    foreach (DeterministicSourceGroup group in automatic)
                        result.Add(group with { Descriptor = group.Descriptor with { Id = ++nextId } });
                }
            }

            return result.OrderByDescending(group => group.Descriptor.EnergyFraction)
                .ThenBy(group => group.Descriptor.Id).ToArray();
        }

        private static double[] WeightsForProfile(double[] profileWeights, DeterministicComponentFeatures[] retained)
        {
            // The mask builder indexes weights by NMF component index; build a full-length array.
            int rank = retained.Max(component => component.Index) + 1;
            double[] full = new double[rank];
            for (int c = 0; c < retained.Length; c++) full[retained[c].Index] = profileWeights[c];
            return full;
        }

        private static double representativePitch(DeterministicComponentFeatures[] retained, List<int> memberIndices, double[] weights)
        {
            double best = 0;
            foreach (int component in memberIndices)
            {
                DeterministicComponentFeatures feature = retained.First(f => f.Index == component);
                if (feature.FundamentalHz > 0 && feature.PitchScore > best)
                    best = feature.FundamentalHz;
            }

            return best;
        }

        private static string DescribeCharacter(InstrumentProfile profile, double harmonic, double percussive)
        {
            if (harmonic >= 0.45 && harmonic > 1.4 * percussive)
                return $"{profile.Name}: sustained/harmonic, pitch {profile.MinimumPitchHz:F0}–{profile.MaximumPitchHz:F0} Hz";
            if (percussive >= 0.45 && percussive > 1.4 * harmonic)
                return $"{profile.Name}: transient/percussion-like, brightness near {profile.BrightnessHz:F0} Hz";
            return $"{profile.Name}: mixed character, pitch {profile.MinimumPitchHz:F0}–{profile.MaximumPitchHz:F0} Hz";
        }

        /// <summary>
        /// Scores how well a component matches an instrument profile using pitch bounds,
        /// spectral brightness (log-normal kernel), HPR character, noise evidence,
        /// CQT energy (constant-Q spectral correlation) and pYIN pitch evidence.
        /// </summary>
        private static double ProfileCompatibility(DeterministicComponentFeatures component, InstrumentProfile profile)
        {
            // Pitch: the component fundamental must fall within (or near) the profile's pitch range.
            // Use pYIN pitch if available and more reliable than the QIFFT estimate.
            double pitchHz = component.FundamentalHz;
            if (component.PyinPitchHz > 0 && component.PyinPitchHz != component.FundamentalHz)
                pitchHz = component.PyinPitchHz;
            double pitchScore = 0;
            if (pitchHz > 0)
            {
                double margin = Math.Max(1, (profile.MaximumPitchHz - profile.MinimumPitchHz) * 0.25);
                double distance = pitchHz < profile.MinimumPitchHz
                    ? profile.MinimumPitchHz - pitchHz
                    : pitchHz > profile.MaximumPitchHz
                        ? pitchHz - profile.MaximumPitchHz : 0;
                pitchScore = Math.Clamp(1 - distance / margin, 0, 1) * component.PitchScore;
            }

            // Brightness: compare the component's spectral centroid to the profile's brightness center.
            // Boost with CQT energy correlation if available.
            double brightnessScore = 0;
            if (component.CentroidHz > 0 && profile.BrightnessHz > 0)
            {
                double logDistance = Math.Log2(component.CentroidHz / profile.BrightnessHz);
                brightnessScore = Math.Exp(-0.5 * logDistance * logDistance / 2.25);
            }
            // CQT energy provides constant-Q spectral correlation: high CqtEnergy means the component
            // spectrum aligns well with specific CQT bands (harmonic structure).
            if (component.CqtEnergy > 0)
            {
                double cqtBrightness = profile.SpectralWeight(component.CentroidHz);
                brightnessScore = Math.Max(brightnessScore, cqtBrightness * 0.9);
            }

            // HPR character: cosine-like similarity of the harmonic/percussive pair.
            double dot = component.Harmonic * profile.Harmonic + component.Percussive * profile.Percussive;
            double magnitude = Math.Sqrt(component.Harmonic * component.Harmonic + component.Percussive * component.Percussive)
                * Math.Sqrt(profile.Harmonic * profile.Harmonic + profile.Percussive * profile.Percussive);
            double characterScore = magnitude > 1e-9 ? dot / magnitude : 0;

            // Noise evidence: a component that is neither harmonic nor percussive matches noisier profiles better.
            double noiseEvidence = Math.Clamp(1 - component.Harmonic - component.Percussive, 0, 1);
            double noiseScore = profile.Noisiness > 0.4 ? noiseEvidence : 1 - noiseEvidence;

            // pYIN voiced evidence: if the source is mostly voiced, boost harmonic profiles.
            double voicedBoost = 0;
            if (component.PyinVoiced > 0.1)
            {
                voicedBoost = component.PyinVoiced * (profile.Harmonic > 0.5 ? 0.1 : -0.05);
            }

            return 0.30 * pitchScore + 0.25 * brightnessScore + 0.30 * characterScore + 0.10 * noiseScore + voicedBoost;
        }
    }
}
