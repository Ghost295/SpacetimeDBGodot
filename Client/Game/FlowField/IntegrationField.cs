using System;
using static SpacetimeDB.Game.FlowField.FlowFlags;

namespace SpacetimeDB.Game.FlowField;

public sealed class IntegrationField
{
    public IntegrationField(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Costs = new float[width * height];
        Flags = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Costs { get; }

    public byte[] Flags { get; }

    public float GetCost(int x, int y)
    {
        EnsureInBounds(x, y);
        return Costs[y * Width + x];
    }

    public IntegrationFlags GetFlags(int x, int y)
    {
        EnsureInBounds(x, y);
        return (IntegrationFlags)Flags[y * Width + x];
    }

    public void Set(int x, int y, float cost, IntegrationFlags flags)
    {
        EnsureInBounds(x, y);
        int idx = y * Width + x;
        Costs[idx] = cost;
        Flags[idx] = (byte)flags;
    }

    public int IndexOf(int x, int y)
    {
        EnsureInBounds(x, y);
        return y * Width + x;
    }

    private void EnsureInBounds(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException($"Coordinates ({x},{y}) are outside the integration field bounds.");
        }
    }
}

