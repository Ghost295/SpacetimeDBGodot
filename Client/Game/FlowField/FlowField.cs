using SpacetimeDB.Game.FlowField;
using System;
using Godot;

namespace SpacetimeDB.Game.FlowField;

public sealed class FlowField
{
    public FlowField(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        DirectionsAndFlags = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] DirectionsAndFlags { get; }

    public byte GetRaw(int x, int y)
    {
        EnsureInBounds(x, y);
        return DirectionsAndFlags[y * Width + x];
    }

    public void SetRaw(int x, int y, byte value)
    {
        EnsureInBounds(x, y);
        DirectionsAndFlags[y * Width + x] = value;
    }

    public Vector2 GetDirection(int x, int y)
    {
        var flags = GetFlags(x, y);
        if ((flags & FlowFlags.FlowFieldFlags.Pathable) == 0)
        {
            return Vector2.Zero;
        }
        
        byte raw = GetRaw(x, y);
        int dirIdx = raw & 0x0F;
        return (uint)dirIdx >= DirectionLUT.Directions.Length ? Vector2.Zero : DirectionLUT.Directions[dirIdx];
    }

    public FlowFlags.FlowFieldFlags GetFlags(int x, int y)
    {
        byte raw = GetRaw(x, y);
        return (FlowFlags.FlowFieldFlags)((raw >> 4) & 0x0F);
    }

    public void Set(int x, int y, int directionIndex, FlowFlags.FlowFieldFlags flags)
    {
        if ((uint)directionIndex >= DirectionLUT.Directions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(directionIndex));
        }

        byte flagNibble = (byte)(((int)flags & 0x0F) << 4);
        SetRaw(x, y, (byte)((directionIndex & 0x0F) | flagNibble));
    }

    private void EnsureInBounds(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException($"Coordinates ({x},{y}) are outside the flow field bounds.");
        }
    }
}

