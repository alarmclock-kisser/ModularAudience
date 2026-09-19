using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>Rows contain w^H, not w. Matrices and vectors are always complex.</summary>
    internal readonly record struct IlrmaMatrix(Complex M00, Complex M01, Complex M10, Complex M11)
    {
        internal static IlrmaMatrix Identity => new(1, 0, 0, 1);

        internal Complex Apply(int row, Complex left, Complex right)
            => row == 0 ? this.M00 * left + this.M01 * right : this.M10 * left + this.M11 * right;

        internal Complex Element(int row, int column)
            => row == 0 ? (column == 0 ? this.M00 : this.M01) : (column == 0 ? this.M10 : this.M11);

        internal IlrmaMatrix WithRow(int row, Complex first, Complex second)
            => row == 0 ? new(first, second, this.M10, this.M11) : new(this.M00, this.M01, first, second);

        internal IlrmaMatrix ScaleRow(int row, double factor)
            => this.WithRow(row, this.Element(row, 0) * factor, this.Element(row, 1) * factor);

        internal IlrmaMatrix Inverse()
        {
            double scale = this.Scale();
            IlrmaMatrix unit = this.Divide(scale);
            Complex determinant = unit.M00 * unit.M11 - unit.M01 * unit.M10;
            double rows = Math.Sqrt((IlrmaNumerics.Power(unit.M00) + IlrmaNumerics.Power(unit.M01))
                * (IlrmaNumerics.Power(unit.M10) + IlrmaNumerics.Power(unit.M11)));
            if (!IlrmaNumerics.IsFinite(determinant) || determinant.Magnitude <= 1e-12 * rows || rows == 0)
                throw new ArithmeticException("ILRMA encountered a singular or poorly conditioned 2x2 matrix; no source image was fabricated.");
            IlrmaMatrix inverse = new(unit.M11 / determinant / scale, -unit.M01 / determinant / scale,
                -unit.M10 / determinant / scale, unit.M00 / determinant / scale);
            inverse.Scale();
            return inverse;
        }

        internal double LogAbsDeterminant()
        {
            double scale = this.Scale();
            IlrmaMatrix unit = this.Divide(scale);
            double determinant = (unit.M00 * unit.M11 - unit.M01 * unit.M10).Magnitude;
            if (!(determinant > 0) || !double.IsFinite(determinant))
                throw new ArithmeticException("ILRMA has a non-finite or zero demixing determinant.");
            return 2 * Math.Log(scale) + Math.Log(determinant);
        }

        private double Scale()
        {
            if (!IlrmaNumerics.IsFinite(this.M00) || !IlrmaNumerics.IsFinite(this.M01)
                || !IlrmaNumerics.IsFinite(this.M10) || !IlrmaNumerics.IsFinite(this.M11))
                throw new ArithmeticException("ILRMA has a non-finite 2x2 matrix.");
            double scale = Math.Max(Math.Max(this.M00.Magnitude, this.M01.Magnitude),
                Math.Max(this.M10.Magnitude, this.M11.Magnitude));
            if (!(scale > 0)) throw new ArithmeticException("ILRMA has a zero 2x2 matrix.");
            return scale;
        }

        private IlrmaMatrix Divide(double scale)
            => new(this.M00 / scale, this.M01 / scale, this.M10 / scale, this.M11 / scale);
    }
}
