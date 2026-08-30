using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ModularAudience.Audio.Omr
{
    public static class ImageToOmrObjParser
    {
        public static Task<OmrObj> ParseAsync(
            ImageObj imageObj,
            string? sourceFilePath = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(imageObj);

            var result = new OmrObj
            {
                SourceFilePath = sourceFilePath
            };

            for (int pageIndex = 0; pageIndex < imageObj.FrameCount; pageIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var frame = imageObj[pageIndex];
                var horizontalLines = FindHorizontalLines(frame, ct);
                var staves = FindStaves(horizontalLines);
                var staffLinePositions = staves.SelectMany(staff => staff.Lines).ToHashSet();
                var page = new OmrPage
                {
                    PageIndex = pageIndex,
                    PixelWidth = frame.Width,
                    PixelHeight = frame.Height,
                    UnclassifiedRegions = horizontalLines
                        .Where(line => !staffLinePositions.Contains(line.Y))
                        .Select(line => new OmrRegion
                        {
                            Bounds = new OmrRect(0, line.Y, frame.Width, line.Thickness),
                            Kind = "horizontal-line",
                            Confidence = line.Coverage
                        })
                        .ToList()
                };
                page.StaffSystems.AddRange(BuildStaffSystems(staves, frame.Width));
                page.UnclassifiedRegions.AddRange(page.StaffSystems.Select(system => new OmrRegion
                {
                    Bounds = system.Bounds,
                    Kind = "staff-system-segment",
                    Confidence = system.Confidence
                }));
                AnalyzeMeasures(frame, page.StaffSystems, ct);
                result.Pages.Add(page);
            }

            return Task.FromResult(result);
        }

        private static List<DetectedLine> FindHorizontalLines(SixLabors.ImageSharp.Image<Rgba32> frame, CancellationToken ct)
        {
            const byte darkPixelThreshold = 160;
            const float minimumCoverage = 0.30f;
            var lines = new List<DetectedLine>();
            int runStart = -1;
            float coverageSum = 0;
            int runLength = 0;

            frame.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < frame.Height; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    int darkPixels = 0;

                    foreach (Rgba32 pixel in row)
                    {
                        int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                        if (pixel.A > 0 && luminance <= darkPixelThreshold)
                        {
                            darkPixels++;
                        }
                    }

                    float coverage = darkPixels / (float)frame.Width;
                    if (coverage >= minimumCoverage)
                    {
                        if (runStart < 0)
                        {
                            runStart = y;
                        }

                        coverageSum += coverage;
                        runLength++;
                        continue;
                    }

                    AddLineFromRun(lines, runStart, runLength, coverageSum);
                    runStart = -1;
                    coverageSum = 0;
                    runLength = 0;
                }
            });

            AddLineFromRun(lines, runStart, runLength, coverageSum);
            return lines;
        }

        private static void AddLineFromRun(List<DetectedLine> lines, int runStart, int runLength, float coverageSum)
        {
            if (runStart < 0 || runLength == 0)
            {
                return;
            }

            lines.Add(new DetectedLine(
                Y: runStart + runLength / 2,
                Thickness: runLength,
                Coverage: coverageSum / runLength));
        }

        private static List<DetectedStaff> FindStaves(IReadOnlyList<DetectedLine> lines)
        {
            var staves = new List<DetectedStaff>();

            for (int start = 0; start <= lines.Count - 5; start++)
            {
                var candidate = lines.Skip(start).Take(5).ToArray();
                int[] distances = candidate
                    .Zip(candidate.Skip(1), (first, second) => second.Y - first.Y)
                    .ToArray();
                float averageSpacing = distances.Sum() / (float)distances.Length;

                if (averageSpacing < 2 || distances.Any(distance => Math.Abs(distance - averageSpacing) > Math.Max(2, averageSpacing * 0.25f)))
                {
                    continue;
                }

                staves.Add(new DetectedStaff(
                    candidate.Select(line => line.Y).ToArray(),
                    (int)Math.Round(averageSpacing)));
                start += 4;
            }

            return staves;
        }

        private static List<OmrStaffSystem> BuildStaffSystems(IReadOnlyList<DetectedStaff> staves, int pageWidth)
        {
            var systems = new List<OmrStaffSystem>();

            foreach (DetectedStaff staff in staves)
            {
                OmrStaffSystem? currentSystem = systems.LastOrDefault();
                int staffTop = staff.Lines[0];
                int staffBottom = staff.Lines[^1];

                if (currentSystem is null || staffTop - currentSystem.Bounds.Bottom > staff.Spacing * 8)
                {
                    currentSystem = new OmrStaffSystem
                    {
                        Bounds = CreateBounds(pageWidth, staffTop, staffBottom, staff.Spacing)
                    };
                    systems.Add(currentSystem);
                }
                else
                {
                    currentSystem = new OmrStaffSystem
                    {
                        Bounds = CreateBounds(pageWidth, currentSystem.Bounds.Y, staffBottom, staff.Spacing),
                        Staves = currentSystem.Staves,
                        Measures = currentSystem.Measures
                    };
                    systems[^1] = currentSystem;
                }

                currentSystem.Staves.Add(new OmrStaff
                {
                    Bounds = CreateBounds(pageWidth, staffTop, staffBottom, staff.Spacing),
                    LineYPositions = [.. staff.Lines]
                });
            }

            return systems;
        }

        private static OmrRect CreateBounds(int pageWidth, int top, int bottom, int spacing)
        {
            int verticalMargin = Math.Max(1, spacing * 2);
            return new OmrRect(0, Math.Max(0, top - verticalMargin), pageWidth, bottom - top + verticalMargin * 2 + 1);
        }

        private static List<StaffSystemSegment> CropStaffSystems(Image<Rgba32> frame, IReadOnlyList<OmrStaffSystem> systems)
        {
            var segments = new List<StaffSystemSegment>(systems.Count);

            for (int systemIndex = 0; systemIndex < systems.Count; systemIndex++)
            {
                OmrRect bounds = systems[systemIndex].Bounds;
                int x = Math.Clamp(bounds.X, 0, frame.Width - 1);
                int y = Math.Clamp(bounds.Y, 0, frame.Height - 1);
                int width = Math.Min(bounds.Width, frame.Width - x);
                int height = Math.Min(bounds.Height, frame.Height - y);
                var crop = new Rectangle(x, y, width, height);
                segments.Add(new StaffSystemSegment(systemIndex, bounds, frame.Clone(context => context.Crop(crop))));
            }

            return segments;
        }

        private static void AnalyzeMeasures(Image<Rgba32> frame, IReadOnlyList<OmrStaffSystem> systems, CancellationToken ct)
        {
            var segments = CropStaffSystems(frame, systems);
            try
            {
                var results = new SegmentAnalysis[segments.Count];
                Parallel.For(0, segments.Count, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
                }, index =>
                {
                    StaffSystemSegment segment = segments[index];
                    results[index] = AnalyzeSegment(segment, systems[segment.SystemIndex], ct);
                });

                for (int index = 0; index < systems.Count; index++)
                {
                    SegmentAnalysis analysis = results[index];
                    systems[index].Measures.AddRange(analysis.Measures);
                    AssignTrebleClefCandidates(systems[index], analysis.NoteCandidates);

                    foreach (OmrNoteEvent candidate in analysis.NoteCandidates)
                    {
                        OmrNoteEvent interpretedCandidate = AssignPitch(candidate, systems[index]);
                        OmrMeasure? measure = systems[index].Measures
                            .FirstOrDefault(item => interpretedCandidate.Bounds.X >= item.Bounds.X && interpretedCandidate.Bounds.X < item.Bounds.Right);
                        measure?.Events.Add(interpretedCandidate);
                    }

                    foreach (OmrMeasure measure in systems[index].Measures)
                    {
                        measure.Events.Sort((left, right) => left.Bounds.X.CompareTo(right.Bounds.X));
                    }
                }
            }
            finally
            {
                foreach (StaffSystemSegment segment in segments)
                {
                    segment.Dispose();
                }
            }
        }

        private static SegmentAnalysis AnalyzeSegment(StaffSystemSegment segment, OmrStaffSystem system, CancellationToken ct)
        {
            int[] columnCoverage = new int[segment.Image.Width];
            int totalStaffHeight = 0;
            var staffRanges = system.Staves
                .Select(staff => new
                {
                    Top = Math.Max(0, staff.LineYPositions.Min() - segment.Bounds.Y),
                    Bottom = Math.Min(segment.Image.Height - 1, staff.LineYPositions.Max() - segment.Bounds.Y)
                })
                .ToArray();

            totalStaffHeight = staffRanges.Sum(range => range.Bottom - range.Top + 1);
            if (totalStaffHeight <= 0)
            {
                return new SegmentAnalysis([], []);
            }

            segment.Image.ProcessPixelRows(accessor =>
            {
                foreach (var range in staffRanges)
                {
                    for (int y = range.Top; y <= range.Bottom; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            Rgba32 pixel = row[x];
                            int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                            if (pixel.A > 0 && luminance <= 160)
                            {
                                columnCoverage[x]++;
                            }
                        }
                    }
                }
            });

            int spacing = EstimateStaffSpacing(system);
            List<int> barLines = FindVerticalBars(columnCoverage, totalStaffHeight, spacing);
            var boundaries = new List<int> { 0 };
            boundaries.AddRange(barLines.Where(x => x > spacing && x < segment.Image.Width - spacing));
            boundaries.Add(segment.Image.Width);
            boundaries = boundaries.Distinct().Order().ToList();

            var measures = new List<OmrMeasure>();
            for (int index = 0; index < boundaries.Count - 1; index++)
            {
                int left = boundaries[index];
                int right = boundaries[index + 1];
                if (right - left < spacing * 3)
                {
                    continue;
                }

                measures.Add(new OmrMeasure
                {
                    Number = measures.Count + 1,
                    Bounds = new OmrRect(segment.Bounds.X + left, segment.Bounds.Y, right - left, segment.Bounds.Height)
                });
            }

            return new SegmentAnalysis(measures, FindNoteCandidates(segment, system, spacing, ct));
        }

        private static List<OmrNoteEvent> FindNoteCandidates(StaffSystemSegment segment, OmrStaffSystem system, int spacing, CancellationToken ct)
        {
            int width = segment.Image.Width;
            int height = segment.Image.Height;
            var ink = new bool[width * height];
            var staffLineRows = system.Staves
                .SelectMany(staff => staff.LineYPositions)
                .SelectMany(y => Enumerable.Range(y - segment.Bounds.Y - 1, 3))
                .Where(y => y >= 0 && y < height)
                .ToHashSet();

            segment.Image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    if (staffLineRows.Contains(y))
                    {
                        continue;
                    }

                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        Rgba32 pixel = row[x];
                        int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                        ink[y * width + x] = pixel.A > 0 && luminance <= 128;
                    }
                }
            });

            var candidates = new List<OmrNoteEvent>();
            var connectedPixels = new Stack<int>();
            int minimumPixels = Math.Max(4, spacing * spacing / 4);

            for (int seed = 0; seed < ink.Length; seed++)
            {
                ct.ThrowIfCancellationRequested();
                if (!ink[seed])
                {
                    continue;
                }

                int minX = width;
                int maxX = 0;
                int minY = height;
                int maxY = 0;
                int pixelCount = 0;
                long xSum = 0;
                long ySum = 0;
                ink[seed] = false;
                connectedPixels.Push(seed);

                while (connectedPixels.TryPop(out int current))
                {
                    int x = current % width;
                    int y = current / width;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    pixelCount++;
                    xSum += x;
                    ySum += y;

                    AddConnectedPixel(current - 1, x > 0);
                    AddConnectedPixel(current + 1, x < width - 1);
                    AddConnectedPixel(current - width, y > 0);
                    AddConnectedPixel(current + width, y < height - 1);
                }

                int componentWidth = maxX - minX + 1;
                int componentHeight = maxY - minY + 1;
                if (pixelCount < minimumPixels || componentWidth > spacing * 3 || componentHeight > spacing * 8 || componentWidth < Math.Max(2, spacing / 3))
                {
                    continue;
                }

                float fillRatio = pixelCount / (float)(componentWidth * componentHeight);
                if (fillRatio < 0.15f)
                {
                    continue;
                }

                int centerY = (int)(ySum / pixelCount) + segment.Bounds.Y;
                int staffIndex = FindNearestStaff(system.Staves, centerY);
                OmrStaff staff = system.Staves[staffIndex];
                int staffPosition = (int)Math.Round((staff.LineYPositions[^1] - centerY) / (spacing / 2.0));
                string duration = ClassifyDuration(componentWidth, componentHeight, fillRatio, spacing);
                candidates.Add(new OmrNoteEvent
                {
                    Bounds = new OmrRect(segment.Bounds.X + minX, segment.Bounds.Y + minY, componentWidth, componentHeight),
                    StaffIndex = staffIndex,
                    StaffPosition = staffPosition,
                    IsCandidate = true,
                    Duration = duration,
                    Confidence = Math.Clamp(fillRatio * 0.8f, 0.12f, 0.76f),
                    SourceSymbol = "notehead-duration-candidate"
                });

                void AddConnectedPixel(int index, bool isInBounds)
                {
                    if (isInBounds && ink[index])
                    {
                        ink[index] = false;
                        connectedPixels.Push(index);
                    }
                }
            }

            return candidates;
        }

        private static string ClassifyDuration(int width, int height, float fillRatio, int spacing)
        {
            bool hasStem = height >= spacing * 2;
            bool isCompact = height <= spacing * 2 && width <= spacing * 2;

            if (!hasStem && isCompact)
            {
                return fillRatio < 0.45f ? "whole" : "quarter";
            }

            return fillRatio < 0.30f ? "half" : "quarter";
        }

        private static int FindNearestStaff(IReadOnlyList<OmrStaff> staves, int y)
        {
            return Enumerable.Range(0, staves.Count)
                .MinBy(index => Math.Abs(staves[index].LineYPositions[2] - y));
        }

        private static List<int> FindVerticalBars(int[] coverage, int totalStaffHeight, int spacing)
        {
            var bars = new List<int>();
            int start = -1;
            int minimumDarkPixels = (int)Math.Ceiling(totalStaffHeight * 0.70);

            for (int x = 0; x < coverage.Length; x++)
            {
                if (coverage[x] >= minimumDarkPixels)
                {
                    start = start < 0 ? x : start;
                    continue;
                }

                AddVerticalBar(bars, start, x - 1, spacing);
                start = -1;
            }

            AddVerticalBar(bars, start, coverage.Length - 1, spacing);
            return bars;
        }

        private static void AddVerticalBar(List<int> bars, int start, int end, int spacing)
        {
            if (start < 0 || end - start + 1 > Math.Max(2, spacing / 2))
            {
                return;
            }

            bars.Add((start + end) / 2);
        }

        private static int EstimateStaffSpacing(OmrStaffSystem system)
        {
            return Math.Max(1, (int)Math.Round(system.Staves
                .SelectMany(staff => staff.LineYPositions.Zip(staff.LineYPositions.Skip(1), (top, bottom) => bottom - top))
                .Average()));
        }

        private static void AssignTrebleClefCandidates(OmrStaffSystem system, IReadOnlyList<OmrNoteEvent> candidates)
        {
            int spacing = EstimateStaffSpacing(system);

            for (int staffIndex = 0; staffIndex < system.Staves.Count; staffIndex++)
            {
                OmrNoteEvent? clefCandidate = candidates
                    .Where(candidate => candidate.StaffIndex == staffIndex)
                    .Where(candidate => candidate.Bounds.X - system.Bounds.X <= spacing * 5)
                    .Where(candidate => candidate.Bounds.Height >= spacing * 3 && candidate.Bounds.Height <= spacing * 8)
                    .Where(candidate => candidate.Bounds.Width <= spacing * 3)
                    .OrderByDescending(candidate => candidate.Bounds.Height * candidate.Confidence)
                    .FirstOrDefault();

                if (clefCandidate is null)
                {
                    continue;
                }

                OmrStaff staff = system.Staves[staffIndex];
                staff.Clef = "treble";
                staff.ClefConfidence = clefCandidate.Confidence;
            }
        }

        private static OmrNoteEvent AssignPitch(OmrNoteEvent candidate, OmrStaffSystem system)
        {
            if (candidate.StaffIndex is not int staffIndex || candidate.StaffPosition is not int staffPosition ||
                staffIndex < 0 || staffIndex >= system.Staves.Count || system.Staves[staffIndex].Clef != "treble")
            {
                return candidate;
            }

            const int trebleBottomLineE4 = 30;
            const string noteSteps = "CDEFGAB";
            int diatonicPitch = trebleBottomLineE4 + staffPosition;
            int octave = Math.DivRem(diatonicPitch, 7, out int stepIndex);

            return new OmrNoteEvent
            {
                Bounds = candidate.Bounds,
                IsRest = candidate.IsRest,
                Step = noteSteps[stepIndex].ToString(),
                Octave = octave,
                Alter = candidate.Alter,
                Duration = candidate.Duration,
                IsDotted = candidate.IsDotted,
                Voice = candidate.Voice,
                Articulation = candidate.Articulation,
                SourceSymbol = candidate.SourceSymbol,
                StaffIndex = staffIndex,
                StaffPosition = staffPosition,
                IsCandidate = true,
                Confidence = candidate.Confidence * system.Staves[staffIndex].ClefConfidence
            };
        }

        private readonly record struct DetectedLine(int Y, int Thickness, float Coverage);
        private readonly record struct DetectedStaff(int[] Lines, int Spacing);
        private sealed record SegmentAnalysis(List<OmrMeasure> Measures, List<OmrNoteEvent> NoteCandidates);
        private sealed record StaffSystemSegment(int SystemIndex, OmrRect Bounds, Image<Rgba32> Image) : IDisposable
        {
            public void Dispose() => this.Image.Dispose();
        }


    }
}
