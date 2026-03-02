using System;
using Godot;

namespace SpacetimeDB.Game.FlowField;

public sealed class CostField
{
    public CostField(int width, int height, byte[] costs)
    {
        GD.Print($"[CostField] Creating cost field -> size={width}x{height} cells={costs.Length}");
        // if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        // if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        // ArgumentNullException.ThrowIfNull(costs);
        // if (costs.Length != width * height)
        // {
        //     throw new ArgumentException("Cost array length does not match width * height.", nameof(costs));
        // }

        Width = width;
        Height = height;
        Costs = costs;
        PathableMask = new bool[costs.Length];
        GD.Print("[CostField] PathableMask: " + PathableMask.Length);

        for (int i = 0; i < costs.Length; i++)
        {
            PathableMask[i] = costs[i] != byte.MaxValue;
        }
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Costs { get; }

    public bool[] PathableMask { get; }

    public byte GetCost(int x, int y)
    {
        EnsureInBounds(x, y);
        return Costs[y * Width + x];
    }

    public bool IsPathable(int x, int y)
    {
        EnsureInBounds(x, y);
        return PathableMask[y * Width + x];
    }

    public int IndexOf(int x, int y)
    {
        EnsureInBounds(x, y);
        return y * Width + x;
    }

    public void SetCost(int x, int y, byte value)
    {
        EnsureInBounds(x, y);
        int idx = y * Width + x;
        Costs[idx] = value;
        PathableMask[idx] = value != byte.MaxValue;
    }

    private void EnsureInBounds(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            GD.PushError($"[CostField] Coordinates ({x},{y}) are outside the cost field bounds.");
        }
    }
}

