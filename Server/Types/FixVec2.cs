using System;

[SpacetimeDB.Type]
public partial struct FixVec2
{
    public static readonly FixVec2 Zero = new(Fix64.Zero, Fix64.Zero);

    public Fix64 X;
    public Fix64 Y;

    public FixVec2(Fix64 x, Fix64 y)
    {
        X = x;
        Y = y;
    }

    public readonly Fix64 SqrMagnitude => (X * X) + (Y * Y);
    public readonly Fix64 Magnitude => Fix64.Sqrt(SqrMagnitude);

    public readonly FixVec2 Normalized()
    {
        var mag = Magnitude;
        if (mag <= Fix64.Epsilon)
        {
            return Zero;
        }

        return this / mag;
    }

    public static Fix64 Dot(FixVec2 a, FixVec2 b)
    {
        return (a.X * b.X) + (a.Y * b.Y);
    }

    public static FixVec2 ClampMagnitude(FixVec2 value, Fix64 maxMagnitude)
    {
        if (maxMagnitude <= Fix64.Zero)
        {
            return Zero;
        }

        var sq = value.SqrMagnitude;
        var maxSq = maxMagnitude * maxMagnitude;
        if (sq <= maxSq)
        {
            return value;
        }

        if (sq <= Fix64.Epsilon)
        {
            return Zero;
        }

        var scale = maxMagnitude / Fix64.Sqrt(sq);
        return value * scale;
    }

    public static FixVec2 Lerp(FixVec2 a, FixVec2 b, Fix64 t)
    {
        return a + ((b - a) * Fix64.Clamp(t, Fix64.Zero, Fix64.One));
    }

    public static FixVec2 operator +(FixVec2 a, FixVec2 b)
    {
        return new FixVec2(a.X + b.X, a.Y + b.Y);
    }

    public static FixVec2 operator -(FixVec2 a, FixVec2 b)
    {
        return new FixVec2(a.X - b.X, a.Y - b.Y);
    }

    public static FixVec2 operator *(FixVec2 value, Fix64 scalar)
    {
        return new FixVec2(value.X * scalar, value.Y * scalar);
    }

    public static FixVec2 operator /(FixVec2 value, Fix64 scalar)
    {
        return new FixVec2(value.X / scalar, value.Y / scalar);
    }

}
