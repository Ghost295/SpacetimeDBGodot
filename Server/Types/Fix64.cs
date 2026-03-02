using System;

[SpacetimeDB.Type]
public partial struct Fix64
{
    public const int FractionalBits = 32;
    public const long OneRaw = 1L << FractionalBits;

    public static readonly Fix64 Zero = new(0);
    public static readonly Fix64 One = new(OneRaw);
    public static readonly Fix64 Epsilon = new(1);

    public long Raw;

    public Fix64(long raw)
    {
        Raw = raw;
    }

    public static Fix64 FromRaw(long raw)
    {
        return new Fix64(raw);
    }

    public static Fix64 FromInt(int value)
    {
        return new Fix64((long)value << FractionalBits);
    }

    public static Fix64 FromLong(long value)
    {
        return new Fix64(value << FractionalBits);
    }

    public static Fix64 FromRatio(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("Fix64 denominator cannot be zero.");
        }

        Int128 shifted = (Int128)numerator << FractionalBits;
        return new Fix64((long)(shifted / denominator));
    }

    public static Fix64 FromDouble(double value)
    {
        return new Fix64((long)Math.Round(value * OneRaw));
    }

    public int FloorToInt()
    {
        if (Raw >= 0)
        {
            return (int)(Raw >> FractionalBits);
        }

        long integer = Raw >> FractionalBits;
        if ((Raw & (OneRaw - 1)) == 0)
        {
            return (int)integer;
        }

        return (int)(integer - 1);
    }

    public int CeilToInt()
    {
        if (Raw >= 0)
        {
            long integer = Raw >> FractionalBits;
            if ((Raw & (OneRaw - 1)) == 0)
            {
                return (int)integer;
            }

            return (int)(integer + 1);
        }

        return (int)(Raw >> FractionalBits);
    }

    public double ToDouble()
    {
        return Raw / (double)OneRaw;
    }

    public static Fix64 Abs(Fix64 value)
    {
        return value.Raw < 0 ? new Fix64(-value.Raw) : value;
    }

    public static Fix64 Min(Fix64 a, Fix64 b)
    {
        return a.Raw <= b.Raw ? a : b;
    }

    public static Fix64 Max(Fix64 a, Fix64 b)
    {
        return a.Raw >= b.Raw ? a : b;
    }

    public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
    {
        if (value.Raw < min.Raw)
        {
            return min;
        }

        if (value.Raw > max.Raw)
        {
            return max;
        }

        return value;
    }

    public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t)
    {
        return a + ((b - a) * Clamp(t, Zero, One));
    }

    public static Fix64 Sqrt(Fix64 value)
    {
        if (value.Raw <= 0)
        {
            return Zero;
        }

        UInt128 n = (UInt128)(ulong)value.Raw << FractionalBits;
        ulong root = IntegerSqrt(n);
        if (root > long.MaxValue)
        {
            root = (ulong)long.MaxValue;
        }

        return new Fix64((long)root);
    }

    private static ulong IntegerSqrt(UInt128 value)
    {
        UInt128 result = 0;
        UInt128 bit = (UInt128)1 << 126;

        while (bit > value)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            UInt128 candidate = result + bit;
            result >>= 1;
            if (value >= candidate)
            {
                value -= candidate;
                result += bit;
            }

            bit >>= 2;
        }

        return (ulong)result;
    }

    public static Fix64 operator +(Fix64 a, Fix64 b)
    {
        return new Fix64(a.Raw + b.Raw);
    }

    public static Fix64 operator -(Fix64 a, Fix64 b)
    {
        return new Fix64(a.Raw - b.Raw);
    }

    public static Fix64 operator -(Fix64 value)
    {
        return new Fix64(-value.Raw);
    }

    public static Fix64 operator *(Fix64 a, Fix64 b)
    {
        Int128 product = (Int128)a.Raw * b.Raw;
        return new Fix64((long)(product >> FractionalBits));
    }

    public static Fix64 operator /(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0)
        {
            throw new DivideByZeroException("Fix64 division by zero.");
        }

        Int128 numerator = (Int128)a.Raw << FractionalBits;
        return new Fix64((long)(numerator / b.Raw));
    }

    public static bool operator <(Fix64 a, Fix64 b)
    {
        return a.Raw < b.Raw;
    }

    public static bool operator >(Fix64 a, Fix64 b)
    {
        return a.Raw > b.Raw;
    }

    public static bool operator <=(Fix64 a, Fix64 b)
    {
        return a.Raw <= b.Raw;
    }

    public static bool operator >=(Fix64 a, Fix64 b)
    {
        return a.Raw >= b.Raw;
    }

    public static implicit operator Fix64(int value)
    {
        return FromInt(value);
    }

    public static implicit operator Fix64(long value)
    {
        return FromLong(value);
    }
}
