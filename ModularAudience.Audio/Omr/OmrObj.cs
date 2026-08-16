using System;
using System.Collections.Generic;
using System.Text;

namespace ModularAudience.Audio.Omr
{
    public sealed class OmrObj
    {
        public int Version { get; init; } = 1;
        public string? SourceFilePath { get; init; }
        public List<OmrPage> Pages { get; init; } = new();
    }

    public sealed class OmrPage
    {
        public int PageIndex { get; init; }
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }
        public List<OmrStaffSystem> StaffSystems { get; init; } = new();
        public List<OmrRegion> UnclassifiedRegions { get; init; } = new();
    }

    public sealed class OmrStaffSystem
    {
        public OmrRect Bounds { get; init; }
        public float Confidence { get; init; }
        public List<OmrStaff> Staves { get; init; } = new();
        public List<OmrMeasure> Measures { get; init; } = new();
    }

    public sealed class OmrStaff
    {
        public OmrRect Bounds { get; init; }
        public int LineCount { get; init; } = 5;
        public List<int> LineYPositions { get; init; } = new();
        public string? Clef { get; set; }
        public float ClefConfidence { get; set; }
    }

    public sealed class OmrMeasure
    {
        public OmrRect Bounds { get; init; }
        public int? Number { get; init; }
        public string? TimeSignature { get; init; }
        public string? KeySignature { get; init; }
        public List<OmrNoteEvent> Events { get; init; } = new();
    }

    public sealed class OmrNoteEvent
    {
        public OmrRect Bounds { get; init; }
        public bool IsRest { get; init; }
        public string? Step { get; init; }
        public int? Octave { get; init; }
        public int? Alter { get; init; }
        public string? Duration { get; init; }
        public bool IsDotted { get; init; }
        public int? Voice { get; init; }
        public string? Articulation { get; init; }
        public string? SourceSymbol { get; init; }
        public int? StaffIndex { get; init; }
        public int? StaffPosition { get; init; }
        public bool IsCandidate { get; init; }
        public float Confidence { get; init; }
    }

    public sealed class OmrRegion
    {
        public OmrRect Bounds { get; init; }
        public string? Kind { get; init; }
        public float Confidence { get; init; }
        public string? SourceSymbol { get; init; }
    }

    public readonly record struct OmrPoint(int X, int Y);

    public readonly record struct OmrRect(int X, int Y, int Width, int Height)
    {
        public int Right => this.X + this.Width;
        public int Bottom => this.Y + this.Height;
    }
}
