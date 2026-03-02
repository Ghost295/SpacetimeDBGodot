using System;

internal struct DeterministicRng
{
    private const ulong DefaultSeed = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public DeterministicRng(ulong seed)
    {
        _state = seed == 0 ? DefaultSeed : seed;
    }

    public uint NextU32()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        _state *= 2685821657736338717UL;
        return (uint)(_state >> 32);
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            return 0;
        }

        return (int)(NextU32() % (uint)maxExclusive);
    }

    public static ulong Mix(ulong seed, ulong value)
    {
        seed ^= value + 0x9E3779B97F4A7C15UL + (seed << 6) + (seed >> 2);
        return seed;
    }

    public static ulong HashString(string value)
    {
        var hash = 1469598103934665603UL;
        var input = value ?? string.Empty;
        for (var i = 0; i < input.Length; i++)
        {
            hash ^= input[i];
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
