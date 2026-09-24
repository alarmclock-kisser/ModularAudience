using System;
using System.Collections.Generic;

namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Procedural pattern grammar for BeatClicker Classic. The 256 logical patterns are
    /// derived from a 4x4x4x4 combination of spatial, rhythm, transform, and texture families.
    /// Each pattern is a parameterized generator, not a fixed coordinate template.
    /// </summary>
    public class BeatClickerPatternLibrary
    {
        public enum SpatialFamily
        {
            Arc = 0,
            PartialEllipse = 1,
            Wave = 2,
            Curl = 3
        }

        public enum RhythmFamily
        {
            EvenPulse = 0,
            Acceleration = 1,
            Syncopated = 2,
            PhraseEcho = 3
        }

        public enum TransformFamily
        {
            Mirror = 0,
            Rotate = 1,
            ScaleAndSkew = 2,
            PhaseRemix = 3
        }

        public enum TextureFamily
        {
            Smooth = 0,
            Jagged = 1,
            Damped = 2,
            Resonant = 3
        }

        public struct PatternId
        {
            public SpatialFamily Spatial;
            public RhythmFamily Rhythm;
            public TransformFamily Transform;
            public TextureFamily Texture;

            public int Index => (int)Spatial * 64 + (int)Rhythm * 16 + (int)Transform * 4 + (int)Texture;

            public static PatternId FromIndex(int index)
            {
                index = Math.Clamp(index, 0, 255);
                return new PatternId
                {
                    Spatial = (SpatialFamily)(index / 64),
                    Rhythm = (RhythmFamily)((index / 16) % 4),
                    Transform = (TransformFamily)((index / 4) % 4),
                    Texture = (TextureFamily)(index % 4)
                };
            }

            public override string ToString() => $"{Spatial}-{Rhythm}-{Transform}-{Texture}";
        }

        public struct PatternParameters
        {
            public float Heading;
            public float LengthFactor;
            public float DurationBeats;
            public float Amplitude;
            public float Phase;
            public float Frequency;
            public float Easing;
            public float LoopRadius;
            public float TurnFraction;
            public float Skew;
            public float TextureJitter;
            public float TextureDamping;
            public float TextureResonance;
            public int PointCount;
            public bool Mirror;
            public bool Reverse;
        }

        public struct ClickPattern
        {
            public PatternId Id;
            public PatternParameters Parameters;
            public float[] Times;
            public float[] X;
            public float[] Y;
        }

        public struct SliderPath
        {
            public PatternId Id;
            public PatternParameters Parameters;
            public float[] X;
            public float[] Y;
            public float[] CumulativeLength;
            public float TotalLength;
        }

        private struct Fingerprint
        {
            public int PatternIndex;
            public int LengthBucket;
            public int AmplitudeBucket;
            public int PhaseBucket;
            public int DirectionBucket;
            public int RhythmBucket;
            public int DurationBucket;
        }

        private readonly List<Fingerprint> _recentFingerprints = [];
        private const int RecentFingerprintLimit = 12;

        public PatternId SelectPattern(Random rng, int difficultyIndex, float energy, bool slider)
        {
            int spatial = SelectSpatial(rng, difficultyIndex, energy, slider);
            int rhythm = SelectRhythm(rng, difficultyIndex, energy);
            int transform = rng.Next(4);
            int texture = rng.Next(4);
            var id = new PatternId
            {
                Spatial = (SpatialFamily)spatial,
                Rhythm = (RhythmFamily)rhythm,
                Transform = (TransformFamily)transform,
                Texture = (TextureFamily)texture
            };

            // Suppress exact recent fingerprints, but allow remixes of the same family.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var fp = MakeFingerprint(id, 0.5f, 0.5f, 0f, 0f, 0f, 0f);
                if (!_recentFingerprints.Contains(fp))
                {
                    return id;
                }

                // Remix: change one axis or the transform.
                int axis = rng.Next(4);
                switch (axis)
                {
                    case 0:
                        id.Transform = (TransformFamily)rng.Next(4);
                        break;
                    case 1:
                        id.Rhythm = (RhythmFamily)rng.Next(4);
                        break;
                    case 2:
                        id.Texture = (TextureFamily)rng.Next(4);
                        break;
                    default:
                        id.Spatial = (SpatialFamily)rng.Next(4);
                        break;
                }
            }

            return id;
        }

        public void RememberFingerprint(
            PatternId id,
            float lengthFactor,
            float amplitude,
            float phase,
            float heading,
            float durationBeats,
            float rhythmSeed)
        {
            var fp = MakeFingerprint(id, lengthFactor, amplitude, phase, heading, durationBeats, rhythmSeed);
            _recentFingerprints.Add(fp);
            if (_recentFingerprints.Count > RecentFingerprintLimit)
            {
                _recentFingerprints.RemoveAt(0);
            }
        }

        public PatternParameters CreateParameters(
            PatternId id,
            Random rng,
            float heading,
            float minSliderLength,
            float maxSliderLength,
            int difficultyIndex)
        {
            float lengthFactor = 0.2f + (float)rng.NextDouble() * 0.8f;
            float amplitude = 0.15f + (float)rng.NextDouble() * 0.55f;
            float phase = (float)(rng.NextDouble() * Math.PI * 2);
            float frequency = 1f + (float)rng.NextDouble() * 3f;
            float easing = 0.8f + (float)rng.NextDouble() * 0.6f;
            float loopRadius = 0.25f + (float)rng.NextDouble() * 0.45f;
            float turnFraction = 0.35f + (float)rng.NextDouble() * 0.45f;
            float skew = (float)((rng.NextDouble() - 0.5) * 0.6);
            int pointCount = 16 + rng.Next(17);

            float textureJitter = id.Texture == TextureFamily.Jagged
                ? 0.08f + (float)rng.NextDouble() * 0.12f
                : 0f;
            float textureDamping = id.Texture == TextureFamily.Damped
                ? 0.5f + (float)rng.NextDouble() * 0.5f
                : 0f;
            float textureResonance = id.Texture == TextureFamily.Resonant
                ? 1.5f + (float)rng.NextDouble() * 1.5f
                : 0f;

            float durationBeats;
            if (id.Rhythm == RhythmFamily.Acceleration || id.Rhythm == RhythmFamily.PhraseEcho)
            {
                int minBeats = difficultyIndex >= 3 ? 3 : 2;
                int maxBeats = difficultyIndex >= 5 ? 6 : difficultyIndex >= 3 ? 5 : 4;
                durationBeats = rng.Next(minBeats, maxBeats + 1);
            }
            else
            {
                int maxBeats = difficultyIndex >= 5 ? 2 : 3;
                durationBeats = rng.Next(1, maxBeats + 1);
            }

            bool mirror = id.Transform == TransformFamily.Mirror || rng.NextDouble() < 0.25f;
            bool reverse = id.Transform == TransformFamily.PhaseRemix && rng.NextDouble() < 0.5f;

            return new PatternParameters
            {
                Heading = heading + (float)((rng.NextDouble() - 0.5) * 1.2),
                LengthFactor = lengthFactor,
                DurationBeats = durationBeats,
                Amplitude = amplitude,
                Phase = phase,
                Frequency = frequency,
                Easing = easing,
                LoopRadius = loopRadius,
                TurnFraction = turnFraction,
                Skew = skew,
                TextureJitter = textureJitter,
                TextureDamping = textureDamping,
                TextureResonance = textureResonance,
                PointCount = pointCount,
                Mirror = mirror,
                Reverse = reverse
            };
        }

        public SliderPath BuildSliderPath(
            PatternId id,
            PatternParameters p,
            float startX,
            float startY,
            int margin,
            int screenW,
            int screenH,
            float minSliderLength,
            float maxSliderLength,
            Random? rng = null)
        {
            float length = minSliderLength + (maxSliderLength - minSliderLength) * p.LengthFactor;
            float dx = (float)Math.Cos(p.Heading);
            float dy = (float)Math.Sin(p.Heading);

            // Clamp the straight endpoint to the playable rectangle.
            float maxForward = GetMaxForwardDistance(startX, startY, dx, dy, margin, screenW, screenH);
            if (maxForward < minSliderLength)
            {
                float towardCenterX = screenW / 2f - startX;
                float towardCenterY = screenH / 2f - startY;
                float towardCenterLength = (float)Math.Sqrt(towardCenterX * towardCenterX + towardCenterY * towardCenterY);
                if (towardCenterLength > 0.001f)
                {
                    dx = towardCenterX / towardCenterLength;
                    dy = towardCenterY / towardCenterLength;
                    maxForward = GetMaxForwardDistance(startX, startY, dx, dy, margin, screenW, screenH);
                }
            }
            if (maxForward < minSliderLength)
            {
                float left = Math.Max(0f, startX - margin);
                float right = Math.Max(0f, screenW - margin - startX);
                float top = Math.Max(0f, startY - margin);
                float bottom = Math.Max(0f, screenH - margin - startY);
                if (Math.Max(left, right) >= Math.Max(top, bottom))
                {
                    dx = right >= left ? 1f : -1f;
                    dy = 0f;
                }
                else
                {
                    dx = 0f;
                    dy = bottom >= top ? 1f : -1f;
                }
                maxForward = GetMaxForwardDistance(startX, startY, dx, dy, margin, screenW, screenH);
            }
            length = Math.Clamp(length, minSliderLength, Math.Min(maxSliderLength, maxForward));

            float endX = startX + dx * length;
            float endY = startY + dy * length;

            // Build local path samples in a normalized [0,1] x [0,1] space, then map to screen.
            var localX = new float[p.PointCount];
            var localY = new float[p.PointCount];
            var localRng = rng ?? new Random();
            for (int i = 0; i < p.PointCount; i++)
            {
                float t = i / (float)(p.PointCount - 1);
                float eased = (float)Math.Pow(t, p.Easing);
                localX[i] = eased;
                localY[i] = BuildLocalY(id.Spatial, id.Texture, p, t, eased, localRng);
            }

            // Apply transform.
            if (p.Mirror)
            {
                for (int i = 0; i < p.PointCount; i++)
                {
                    localY[i] = -localY[i];
                }
            }
            if (p.Reverse)
            {
                for (int i = 0; i < p.PointCount / 2; i++)
                {
                    (localX[i], localX[p.PointCount - 1 - i]) = (localX[p.PointCount - 1 - i], localX[i]);
                    (localY[i], localY[p.PointCount - 1 - i]) = (localY[p.PointCount - 1 - i], localY[i]);
                }
            }

            // Map local to screen: x along the heading, y perpendicular.
            float perpX = -dy;
            float perpY = dx;
            float amplitudePx = length * p.Amplitude;
            var pathX = new float[p.PointCount];
            var pathY = new float[p.PointCount];
            for (int i = 0; i < p.PointCount; i++)
            {
                float along = localX[i] * length;
                float across = localY[i] * amplitudePx;
                pathX[i] = startX + dx * along + perpX * across;
                pathY[i] = startY + dy * along + perpY * across;
            }

            // Force exact endpoints.
            pathX[0] = startX;
            pathY[0] = startY;
            pathX[p.PointCount - 1] = endX;
            pathY[p.PointCount - 1] = endY;

            // Clamp to playable rectangle.
            for (int i = 0; i < p.PointCount; i++)
            {
                pathX[i] = Math.Clamp(pathX[i], margin, screenW - margin);
                pathY[i] = Math.Clamp(pathY[i], margin, screenH - margin);
            }

            // Recompute cumulative arc length.
            var cumulative = new float[p.PointCount];
            float total = 0f;
            for (int i = 1; i < p.PointCount; i++)
            {
                total += (float)Math.Sqrt(
                    Math.Pow(pathX[i] - pathX[i - 1], 2) + Math.Pow(pathY[i] - pathY[i - 1], 2));
                cumulative[i] = total;
            }

            return new SliderPath
            {
                Id = id,
                Parameters = p,
                X = pathX,
                Y = pathY,
                CumulativeLength = cumulative,
                TotalLength = total
            };
        }

        private static float GetMaxForwardDistance(
            float startX,
            float startY,
            float dx,
            float dy,
            int margin,
            int screenW,
            int screenH)
        {
            float maxForward = float.PositiveInfinity;
            if (dx > 0.001f) maxForward = Math.Min(maxForward, (screenW - margin - startX) / dx);
            else if (dx < -0.001f) maxForward = Math.Min(maxForward, (margin - startX) / dx);
            if (dy > 0.001f) maxForward = Math.Min(maxForward, (screenH - margin - startY) / dy);
            else if (dy < -0.001f) maxForward = Math.Min(maxForward, (margin - startY) / dy);
            return maxForward;
        }

        public ClickPattern BuildClickPattern(
            PatternId id,
            PatternParameters p,
            float startTime,
            float beatInterval,
            int count,
            float centerX,
            float centerY,
            int margin,
            int screenW,
            int screenH,
            float circleRadius,
            float minTimeGap)
        {
            count = Math.Clamp(count, 2, 8);
            float radius = Math.Clamp(circleRadius * (1.6f + p.LengthFactor * 0.8f), 60f, 160f);
            centerX = Math.Clamp(centerX, margin + radius, screenW - margin - radius);
            centerY = Math.Clamp(centerY, margin + radius, screenH - margin - radius);

            float rotation = p.Heading;
            if (p.Mirror)
            {
                rotation = -rotation;
            }

            var times = new float[count];
            var x = new float[count];
            var y = new float[count];

            float step = beatInterval * 0.5f;
            if (id.Rhythm == RhythmFamily.Acceleration)
            {
                step = beatInterval * (0.35f + p.LengthFactor * 0.3f);
            }
            else if (id.Rhythm == RhythmFamily.Syncopated)
            {
                step = beatInterval * (0.4f + (float)(p.Phase / (Math.PI * 2)) * 0.2f);
            }
            else if (id.Rhythm == RhythmFamily.PhraseEcho)
            {
                step = beatInterval * (0.45f + p.LengthFactor * 0.25f);
            }

            // Ensure the pattern fits within the minimum time gap.
            if (step < minTimeGap)
            {
                step = minTimeGap;
            }

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float angle = rotation + t * p.TurnFraction * (float)(Math.PI * 2);
                float r = radius * (0.6f + t * 0.4f);
                x[i] = centerX + (float)Math.Cos(angle) * r;
                y[i] = centerY + (float)Math.Sin(angle) * r;
                times[i] = startTime + step * i;
            }

            // Clamp to playable rectangle.
            for (int i = 0; i < count; i++)
            {
                x[i] = Math.Clamp(x[i], margin, screenW - margin);
                y[i] = Math.Clamp(y[i], margin, screenH - margin);
            }

            return new ClickPattern
            {
                Id = id,
                Parameters = p,
                Times = times,
                X = x,
                Y = y
            };
        }

        public static (float x, float y) GetPointAtProgress(SliderPath path, float progress)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            float target = progress * path.TotalLength;
            int i = 1;
            while (i < path.CumulativeLength.Length && path.CumulativeLength[i] < target)
            {
                i++;
            }
            if (i == 0)
            {
                return (path.X[0], path.Y[0]);
            }
            float prev = path.CumulativeLength[i - 1];
            float curr = path.CumulativeLength[i];
            float t = curr - prev > 0.001f ? (target - prev) / (curr - prev) : 0f;
            return (
                path.X[i - 1] + (path.X[i] - path.X[i - 1]) * t,
                path.Y[i - 1] + (path.Y[i] - path.Y[i - 1]) * t);
        }

        public static (float progress, float distance) GetClosestPoint(SliderPath path, float mouseX, float mouseY)
        {
            float bestDist = float.MaxValue;
            float bestProgress = 0f;
            for (int i = 1; i < path.X.Length; i++)
            {
                float x1 = path.X[i - 1];
                float y1 = path.Y[i - 1];
                float x2 = path.X[i];
                float y2 = path.Y[i];
                float dx = x2 - x1;
                float dy = y2 - y1;
                float lenSq = dx * dx + dy * dy;
                float t = lenSq > 0.001f
                    ? Math.Clamp(((mouseX - x1) * dx + (mouseY - y1) * dy) / lenSq, 0f, 1f)
                    : 0f;
                float cx = x1 + t * dx;
                float cy = y1 + t * dy;
                float dist = (float)Math.Sqrt(Math.Pow(mouseX - cx, 2) + Math.Pow(mouseY - cy, 2));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    float segLen = (float)Math.Sqrt(lenSq);
                    float segProgress = path.CumulativeLength[i - 1] + t * segLen;
                    bestProgress = path.TotalLength > 0.001f ? segProgress / path.TotalLength : 0f;
                }
            }
            return (bestProgress, bestDist);
        }

        private static float BuildLocalY(
            SpatialFamily spatial,
            TextureFamily texture,
            PatternParameters p,
            float t,
            float eased,
            Random rng)
        {
            float baseY;
            switch (spatial)
            {
                case SpatialFamily.Arc:
                    // A monotonic curve: a single bend.
                    baseY = (float)Math.Sin(t * (float)Math.PI) * p.Amplitude * 0.8f;
                    break;

                case SpatialFamily.PartialEllipse:
                    // An elliptical bow: a partial ellipse segment.
                    float angle = p.Phase + t * p.TurnFraction * (float)(Math.PI * 2);
                    baseY = (float)Math.Sin(angle) * p.Amplitude;
                    break;

                case SpatialFamily.Wave:
                    // A sinusoidal wave with randomized frequency and phase.
                    baseY = (float)Math.Sin(p.Phase + t * p.Frequency * (float)(Math.PI * 2)) * p.Amplitude;
                    break;

                case SpatialFamily.Curl:
                    // A looping or curling path: a partial turn.
                    float curlAngle = p.Phase + t * p.TurnFraction * (float)(Math.PI * 2);
                    baseY = (float)Math.Sin(curlAngle) * p.Amplitude * (0.5f + p.LoopRadius * 0.5f);
                    break;

                default:
                    baseY = 0f;
                    break;
            }

            // Apply texture: jitter, damping, or resonance.
            if (texture == TextureFamily.Jagged)
            {
                baseY += (float)((rng.NextDouble() - 0.5) * p.TextureJitter * 2);
            }
            else if (texture == TextureFamily.Damped)
            {
                baseY *= (float)Math.Exp(-t * p.TextureDamping);
            }
            else if (texture == TextureFamily.Resonant)
            {
                baseY += (float)Math.Sin(t * p.TextureResonance * (float)(Math.PI * 4)) * p.Amplitude * 0.15f;
            }

            return baseY;
        }

        private static int SelectSpatial(Random rng, int difficultyIndex, float energy, bool slider)
        {
            // Curl is rare, especially on lower difficulties.
            float curlChance = slider
                ? (difficultyIndex >= 5 ? 0.12f : difficultyIndex >= 3 ? 0.06f : 0.02f)
                : (difficultyIndex >= 5 ? 0.08f : 0.03f);
            float roll = (float)rng.NextDouble();
            if (roll < curlChance)
            {
                return (int)SpatialFamily.Curl;
            }

            // Weight the other families by energy.
            float arcWeight = 0.3f + energy * 0.2f;
            float ellipseWeight = 0.25f + energy * 0.15f;
            float waveWeight = 0.25f + (1f - energy) * 0.2f;
            float total = arcWeight + ellipseWeight + waveWeight;
            roll = (float)rng.NextDouble() * total;
            if (roll < arcWeight)
            {
                return (int)SpatialFamily.Arc;
            }
            if (roll < arcWeight + ellipseWeight)
            {
                return (int)SpatialFamily.PartialEllipse;
            }
            return (int)SpatialFamily.Wave;
        }

        private static int SelectRhythm(Random rng, int difficultyIndex, float energy)
        {
            // Higher difficulties and energy favor more complex rhythms.
            float evenWeight = 0.4f - difficultyIndex * 0.03f;
            float accelWeight = 0.2f + difficultyIndex * 0.02f;
            float syncWeight = 0.15f + energy * 0.15f;
            float echoWeight = 0.15f + difficultyIndex * 0.02f;
            float total = evenWeight + accelWeight + syncWeight + echoWeight;
            float roll = (float)rng.NextDouble() * total;
            if (roll < evenWeight)
            {
                return (int)RhythmFamily.EvenPulse;
            }
            if (roll < evenWeight + accelWeight)
            {
                return (int)RhythmFamily.Acceleration;
            }
            if (roll < evenWeight + accelWeight + syncWeight)
            {
                return (int)RhythmFamily.Syncopated;
            }
            return (int)RhythmFamily.PhraseEcho;
        }

        private static Fingerprint MakeFingerprint(
            PatternId id,
            float lengthFactor,
            float amplitude,
            float phase,
            float heading,
            float durationBeats,
            float rhythmSeed)
        {
            return new Fingerprint
            {
                PatternIndex = id.Index,
                LengthBucket = (int)(lengthFactor * 8),
                AmplitudeBucket = (int)(amplitude * 8),
                PhaseBucket = (int)(phase / (Math.PI * 2) * 8),
                DirectionBucket = (int)(heading / (Math.PI * 2) * 8),
                RhythmBucket = (int)rhythmSeed,
                DurationBucket = (int)durationBeats
            };
        }
    }
}
