using System.Drawing.Drawing2D;
using ModularAudience.Quantum.Models;

namespace ModularAudience.Forms.Quantum;

internal static class QuantumDiagramRenderer
{
    internal const int CircuitLeft = 64, CircuitTop = 34, WireHeight = 28;
    private static readonly Color Background = Color.FromArgb(19, 26, 38), Grid = Color.FromArgb(48, 62, 82), Ink = Color.FromArgb(220, 230, 242);

    internal static int GateWidth(int count) => Math.Clamp((8000 - CircuitLeft) / Math.Max(1, count + 1), 14, 48);

    internal static Bitmap Circuit(QuantumCircuitDefinition definition, int selectedGate = -1)
    {
        int spacing = GateWidth(definition.Gates.Count);
        var bitmap = new Bitmap(Math.Max(480, CircuitLeft + (definition.Gates.Count + 2) * spacing), CircuitTop + definition.QubitCount * WireHeight + 20);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Background);
        using var font = new Font("Segoe UI", 8);
        using var ink = new SolidBrush(Ink);
        using var wire = new Pen(Grid);
        for (int q = 0; q < definition.QubitCount; q++)
        {
            int y = CircuitTop + q * WireHeight;
            graphics.DrawString($"q{q} |0>", font, ink, 5, y - 9);
            graphics.DrawLine(wire, CircuitLeft - 8, y, bitmap.Width - 10, y);
        }
        for (int index = 0; index < definition.Gates.Count; index++)
        {
            var gate = definition.Gates[index];
            if (gate.Qubit < 0 || gate.Qubit >= definition.QubitCount) continue;
            float x = CircuitLeft + index * spacing, y = CircuitTop + gate.Qubit * WireHeight;
            Color color = !gate.Enabled ? Color.SlateGray : gate.Kind switch
            {
                QuantumGateKind.Hadamard => Color.FromArgb(65, 212, 193), QuantumGateKind.BitFlip => Color.FromArgb(180, 140, 250),
                QuantumGateKind.ControlledNot => Color.FromArgb(250, 188, 81), _ => Color.FromArgb(98, 177, 251)
            };
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, index == selectedGate ? 3 : 1.5f);
            int width = Math.Min(30, spacing - 2);
            if (gate.Kind == QuantumGateKind.ControlledNot && gate.Target >= 0 && gate.Target < definition.QubitCount)
            {
                float target = CircuitTop + gate.Target * WireHeight;
                graphics.DrawLine(pen, x + width / 2f, y, x + width / 2f, target);
                graphics.FillEllipse(brush, x + width / 2f - 3, y - 3, 6, 6);
                graphics.DrawEllipse(pen, x + width / 2f - 7, target - 7, 14, 14);
                graphics.DrawLine(pen, x + width / 2f, target - 7, x + width / 2f, target + 7);
                graphics.DrawLine(pen, x + width / 2f - 7, target, x + width / 2f + 7, target);
            }
            else
            {
                graphics.FillRectangle(brush, x, y - 10, width, 20);
                using var dark = new SolidBrush(Background);
                string text = gate.Kind switch { QuantumGateKind.Hadamard => "H", QuantumGateKind.BitFlip => "X", _ => "Ry" };
                graphics.DrawString(text, font, dark, x + 1, y - 8);
            }
            if (index == selectedGate) graphics.DrawRectangle(Pens.White, x - 2, y - 12, width + 4, 24);
        }
        graphics.DrawString("M → c (q0 is the rightmost bit)", font, ink, CircuitLeft, bitmap.Height - 18);
        return bitmap;
    }

    internal static Bitmap Sequence(QuantumBeatSequence? sequence, IReadOnlyList<string> sampleNames, int bars, int stepsPerBar)
    {
        int steps = Math.Clamp(bars * stepsPerBar, 4, 1024);
        int cell = Math.Clamp((8000 - 176) / steps, 6, 28), width = Math.Max(640, 176 + steps * cell);
        var bitmap = new Bitmap(width, Math.Max(160, 48 + sampleNames.Count * 32));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Background);
        using var font = new Font("Segoe UI", 8);
        using var brush = new SolidBrush(Ink);
        using var grid = new Pen(Grid);
        using var barPen = new Pen(Color.FromArgb(100, 133, 166), 2);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        for (int row = 0; row < sampleNames.Count; row++)
        {
            graphics.DrawString(sampleNames[row], font, brush, new RectangleF(5, 36 + row * 32, 164, 32), format);
            graphics.DrawLine(grid, 176, 36 + (row + 1) * 32, width, 36 + (row + 1) * 32);
        }
        for (int step = 0; step <= steps; step++)
        {
            int x = 176 + step * cell;
            graphics.DrawLine(step % stepsPerBar == 0 ? barPen : grid, x, 34, x, bitmap.Height - 8);
            if (step < steps && (cell >= 18 || step % stepsPerBar == 0))
                graphics.DrawString(step % stepsPerBar == 0 ? $"B{step / stepsPerBar + 1}" : (step % stepsPerBar + 1).ToString(), font, brush, x + 2, 12);
        }
        if (sequence is not null)
            foreach (var note in sequence.Notes)
            {
                if (note.TrackIndex < 0 || note.TrackIndex >= sampleNames.Count) continue;
                Color color = note.IsGhost ? Color.FromArgb(140, 158, 194) : note.Reverse ? Color.FromArgb(215, 132, 234)
                    : note.VarispeedFactor != 1 ? Color.FromArgb(247, 185, 87) : Color.FromArgb(59, 206, 184);
                using var fill = new SolidBrush(Color.FromArgb(Math.Clamp((int)(70 + note.Velocity * 185), 0, 255), color));
                float x = 176 + (float)note.StartStep * cell, y = 40 + note.TrackIndex * 32;
                float length = Math.Max(3, (float)Math.Min(note.DurationSteps, steps - note.StartStep) * cell - 2);
                graphics.FillRectangle(fill, x + 1, y, length, 23);
                if (cell >= 24) graphics.DrawString(note.Velocity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), font, Brushes.Black, x + 2, y + 3);
            }
        if (sampleNames.Count == 0) graphics.DrawString("Select samples in an audio collection to create instrument tracks.", font, brush, 12, 72);
        return bitmap;
    }
}
