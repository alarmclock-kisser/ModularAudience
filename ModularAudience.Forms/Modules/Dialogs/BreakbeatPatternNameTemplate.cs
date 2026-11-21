using System.Globalization;
using System.Text;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class BreakbeatPatternNameTemplate
{
    private static readonly (decimal Value, string Symbol)[] FractionSymbols =
    [
        (1m / 2m, "½"),
        (1m / 3m, "⅓"),
        (2m / 3m, "⅔"),
        (1m / 4m, "¼"),
        (3m / 4m, "¾"),
        (1m / 5m, "⅕"),
        (2m / 5m, "⅖"),
        (3m / 5m, "⅗"),
        (4m / 5m, "⅘"),
        (1m / 6m, "⅙"),
        (5m / 6m, "⅚"),
        (1m / 8m, "⅛"),
        (3m / 8m, "⅜"),
        (5m / 8m, "⅝"),
        (7m / 8m, "⅞"),
        (1m / 7m, "⅐"),
        (1m / 9m, "⅑"),
        (1m / 10m, "⅒")
    ];

    private readonly TemplateSegment[] segments;

    private BreakbeatPatternNameTemplate(TemplateSegment[] segments)
    {
        this.segments = segments;
    }

    public static bool TryParse(
        string? text,
        out BreakbeatPatternNameTemplate? template,
        out string error)
    {
        template = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a template or leave the field empty.";
            return false;
        }

        List<TemplateSegment> segments = [];
        int noteGroupCount = 0;
        int position = 0;
        while (position < text.Length)
        {
            int openingBrace = text.IndexOf('{', position);
            int closingBrace = text.IndexOf('}', position);
            if (closingBrace >= 0 && (openingBrace < 0 || closingBrace < openingBrace))
            {
                error = "A closing brace has no matching opening brace.";
                return false;
            }

            if (openingBrace < 0)
            {
                AddLiteralSegment(segments, text[position..]);
                break;
            }

            if (openingBrace > position)
            {
                AddLiteralSegment(segments, text[position..openingBrace]);
            }

            int closingIndex = text.IndexOf('}', openingBrace + 1);
            int nestedOpeningIndex = text.IndexOf('{', openingBrace + 1);
            if (closingIndex < 0)
            {
                error = "An opening brace has no matching closing brace.";
                return false;
            }

            if (nestedOpeningIndex >= 0 && nestedOpeningIndex < closingIndex)
            {
                error = "Nested braces are not supported.";
                return false;
            }

            string body = text[(openingBrace + 1)..closingIndex];
            if (!TryParseGroup(body, out TemplatePart[] parts, out bool hasNoteVariables, out error))
            {
                return false;
            }

            if (hasNoteVariables && ++noteGroupCount > 1)
            {
                error = "Use one per-note template group.";
                return false;
            }

            segments.Add(new TemplateSegment(hasNoteVariables, parts));
            position = closingIndex + 1;
        }

        template = new BreakbeatPatternNameTemplate(segments.ToArray());
        return true;
    }

    public string Format(
        IReadOnlyList<BreakbeatPatternNote> notes,
        IReadOnlyList<string> sampleNames,
        int ticksPerCell,
        decimal bpm)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(sampleNames);
        if (ticksPerCell <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerCell));
        }

        int noteGroupIndex = Array.FindIndex(this.segments, segment => segment.IsNoteGroup);
        StringBuilder result = new();
        if (noteGroupIndex < 0)
        {
            foreach (TemplateSegment segment in this.segments)
            {
                AppendParts(result, segment.Parts, null, sampleNames, ticksPerCell, bpm);
            }
            return result.ToString().Trim();
        }

        for (int index = 0; index < noteGroupIndex; index++)
        {
            AppendParts(result, this.segments[index].Parts, null, sampleNames, ticksPerCell, bpm);
        }

        TemplatePart[] noteParts = this.segments[noteGroupIndex].Parts;
        foreach (BreakbeatPatternNote note in notes
            .OrderBy(note => note.TrackIndex)
            .ThenBy(note => note.StartTick)
            .ThenBy(note => note.PitchSemitones)
            .ThenBy(note => note.DurationTicks))
        {
            if (note.TrackIndex < 0 || note.TrackIndex >= sampleNames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(notes), "A note refers to a missing sample track.");
            }

            AppendParts(result, noteParts, note, sampleNames, ticksPerCell, bpm);
        }

        for (int index = noteGroupIndex + 1; index < this.segments.Length; index++)
        {
            AppendParts(result, this.segments[index].Parts, null, sampleNames, ticksPerCell, bpm);
        }

        return result.ToString().Trim();
    }

    private static bool TryParseGroup(
        string body,
        out TemplatePart[] parts,
        out bool hasNoteVariables,
        out string error)
    {
        List<TemplatePart> parsedParts = [];
        StringBuilder literal = new();
        hasNoteVariables = false;
        bool hasBpmVariables = false;
        error = string.Empty;

        for (int index = 0; index < body.Length;)
        {
            char current = body[index];
            if (current is not ('n' or 'p' or 'd' or 'b'))
            {
                if (char.IsLetter(current))
                {
                    error = $"Unknown variable '{current}'. Use lowercase n, p, d, or b.";
                    parts = [];
                    return false;
                }

                literal.Append(current);
                index++;
                continue;
            }

            AddLiteralPart(parsedParts, literal);
            int runLength = 1;
            while (index + runLength < body.Length && body[index + runLength] == current)
            {
                runLength++;
            }

            TemplatePartKind kind = current switch
            {
                'n' => TemplatePartKind.SampleName,
                'p' => TemplatePartKind.Pitch,
                'd' => TemplatePartKind.Duration,
                'b' => TemplatePartKind.Bpm,
                _ => throw new InvalidOperationException("Unexpected template variable.")
            };

            if (kind == TemplatePartKind.Pitch && runLength > 3)
            {
                error = "Pitch variables are limited to p, pp, or ppp.";
                parts = [];
                return false;
            }

            if (kind == TemplatePartKind.Duration && runLength != 1)
            {
                error = "Use one d for note duration.";
                parts = [];
                return false;
            }

            hasNoteVariables |= kind is TemplatePartKind.SampleName or TemplatePartKind.Pitch or TemplatePartKind.Duration;
            hasBpmVariables |= kind == TemplatePartKind.Bpm;
            parsedParts.Add(new TemplatePart(kind, string.Empty, runLength));
            index += runLength;
        }

        AddLiteralPart(parsedParts, literal);
        if (parsedParts.Count == 0)
        {
            error = "Each brace group must contain a supported variable.";
            parts = [];
            return false;
        }

        if (hasNoteVariables && hasBpmVariables)
        {
            error = "Keep BPM variables outside the per-note template group.";
            parts = [];
            return false;
        }

        parts = parsedParts.ToArray();
        return true;
    }

    private static void AppendParts(
        StringBuilder output,
        IReadOnlyList<TemplatePart> parts,
        BreakbeatPatternNote? note,
        IReadOnlyList<string> sampleNames,
        int ticksPerCell,
        decimal bpm)
    {
        foreach (TemplatePart part in parts)
        {
            switch (part.Kind)
            {
                case TemplatePartKind.Literal:
                    output.Append(part.Value);
                    break;
                case TemplatePartKind.SampleName:
                    BreakbeatPatternNote nameNote = note
                        ?? throw new InvalidOperationException("A sample-name variable requires a note.");
                    string sampleName = sampleNames[nameNote.TrackIndex] ?? string.Empty;
                    StringInfo nameInfo = new(sampleName);
                    output.Append(nameInfo.SubstringByTextElements(0, Math.Min(part.Count, nameInfo.LengthInTextElements)));
                    break;
                case TemplatePartKind.Pitch:
                    BreakbeatPatternNote pitchNote = note
                        ?? throw new InvalidOperationException("A pitch variable requires a note.");
                    output.Append(FormatPitch(pitchNote.PitchSemitones, part.Count));
                    break;
                case TemplatePartKind.Duration:
                    BreakbeatPatternNote durationNote = note
                        ?? throw new InvalidOperationException("A duration variable requires a note.");
                    output.Append(FormatDuration(durationNote.DurationTicks, ticksPerCell));
                    break;
                case TemplatePartKind.Bpm:
                    output.Append(FormatBpmPrefix(bpm, part.Count));
                    break;
                default:
                    throw new InvalidOperationException("Unexpected template part.");
            }
        }
    }

    private static string FormatPitch(float pitch, int precision)
    {
        if (!float.IsFinite(pitch))
        {
            throw new ArgumentOutOfRangeException(nameof(pitch), "Pitch must be finite to format a sample name.");
        }

        if (pitch == 0f)
        {
            return "0";
        }

        string sign = pitch < 0f ? "-" : "+";
        if (precision == 1)
        {
            return sign;
        }

        decimal absolutePitch = decimal.Round(
            decimal.Abs((decimal)pitch),
            6,
            MidpointRounding.AwayFromZero);
        decimal wholePitch = decimal.Truncate(absolutePitch);
        string wholeText = wholePitch.ToString("0", CultureInfo.InvariantCulture);
        if (precision == 2)
        {
            return sign + wholeText;
        }

        decimal fraction = absolutePitch - wholePitch;
        if (fraction == 0m)
        {
            return sign + wholeText;
        }

        string fractionText = FormatFraction(fraction);
        if (fractionText.Length == 1)
        {
            return sign + (wholePitch == 0m ? string.Empty : wholeText) + fractionText;
        }

        return sign + (wholePitch == 0m ? string.Empty : wholeText + " ") + fractionText;
    }

    private static string FormatFraction(decimal fraction)
    {
        foreach ((decimal value, string symbol) in FractionSymbols)
        {
            if (decimal.Abs(fraction - value) <= 0.000001m)
            {
                return symbol;
            }
        }

        (long numerator, long denominator) = ApproximateFraction(fraction);
        return $"{numerator}/{denominator}";
    }

    private static (long Numerator, long Denominator) ApproximateFraction(decimal fraction)
    {
        long bestNumerator = 0;
        long bestDenominator = 1;
        decimal bestError = decimal.MaxValue;
        for (int denominator = 2; denominator <= 1000; denominator++)
        {
            long numerator = (long)decimal.Round(
                fraction * denominator,
                0,
                MidpointRounding.AwayFromZero);
            if (numerator <= 0 || numerator >= denominator)
            {
                continue;
            }

            decimal error = decimal.Abs(fraction - (decimal)numerator / denominator);
            if (error < bestError)
            {
                bestError = error;
                bestNumerator = numerator;
                bestDenominator = denominator;
            }

            if (error <= 0.0000005m)
            {
                return ReduceFraction(numerator, denominator);
            }
        }

        if (bestError <= 0.0000005m)
        {
            return ReduceFraction(bestNumerator, bestDenominator);
        }

        string decimalText = fraction.ToString("0.############################", CultureInfo.InvariantCulture);
        int decimalPoint = decimalText.IndexOf('.');
        if (decimalPoint < 0)
        {
            return (long.Parse(decimalText, CultureInfo.InvariantCulture), 1);
        }

        long exactNumerator = long.Parse(decimalText.Remove(decimalPoint, 1), CultureInfo.InvariantCulture);
        long exactDenominator = 1;
        for (int index = decimalPoint + 1; index < decimalText.Length; index++)
        {
            exactDenominator *= 10;
        }

        return ReduceFraction(exactNumerator, exactDenominator);
    }

    private static (long Numerator, long Denominator) ReduceFraction(long numerator, long denominator)
    {
        long left = numerator;
        long right = denominator;
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        long divisor = Math.Max(1, left);
        return (numerator / divisor, denominator / divisor);
    }

    private static string FormatDuration(int durationTicks, int ticksPerCell) =>
        ((decimal)durationTicks / ticksPerCell).ToString("0.################", CultureInfo.InvariantCulture);

    private static string FormatBpmPrefix(decimal bpm, int digitCount)
    {
        string formatted = bpm.ToString("0.################", CultureInfo.InvariantCulture);
        bool negative = formatted.StartsWith("-", StringComparison.Ordinal);
        string unsigned = negative ? formatted[1..] : formatted;
        int decimalPoint = unsigned.IndexOf('.');
        string wholeDigits = decimalPoint < 0 ? unsigned : unsigned[..decimalPoint];
        string fractionDigits = decimalPoint < 0 ? string.Empty : unsigned[(decimalPoint + 1)..];
        string allDigits = wholeDigits + fractionDigits;
        int includedDigits = Math.Min(digitCount, allDigits.Length);
        string prefix = includedDigits <= wholeDigits.Length
            ? allDigits[..includedDigits]
            : wholeDigits + "." + fractionDigits[..(includedDigits - wholeDigits.Length)];
        return negative ? "-" + prefix : prefix;
    }

    private static void AddLiteralSegment(List<TemplateSegment> segments, string value)
    {
        if (value.Length > 0)
        {
            segments.Add(new TemplateSegment(false, [new TemplatePart(TemplatePartKind.Literal, value, 0)]));
        }
    }

    private static void AddLiteralPart(List<TemplatePart> parts, StringBuilder literal)
    {
        if (literal.Length > 0)
        {
            parts.Add(new TemplatePart(TemplatePartKind.Literal, literal.ToString(), 0));
            literal.Clear();
        }
    }

    private enum TemplatePartKind
    {
        Literal,
        SampleName,
        Pitch,
        Duration,
        Bpm
    }

    private readonly record struct TemplatePart(TemplatePartKind Kind, string Value, int Count);

    private sealed record TemplateSegment(bool IsNoteGroup, TemplatePart[] Parts);
}
