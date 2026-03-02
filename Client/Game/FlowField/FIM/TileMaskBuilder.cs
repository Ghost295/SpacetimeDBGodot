using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacetimeDB.Game.FlowField.Tile;

namespace SpacetimeDB.Game.FlowField.FIM;

public static class TileMaskBuilder
{
    public static bool[] Create(int tilesX, int tilesY, Func<int, int, bool> include)
    {
        ArgumentNullException.ThrowIfNull(include);

        bool[] mask = new bool[tilesX * tilesY];
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (include(tx, ty))
                {
                    mask[ty * tilesX + tx] = true;
                }
            }
        }

        return mask;
    }

    public static bool[]? FromRectangles(int tilesX, int tilesY, IEnumerable<Rect2I>? rects)
    {
        if (rects is null)
        {
            return null;
        }

        bool[] mask = new bool[tilesX * tilesY];
        bool any = false;

        foreach (Rect2I rect in rects)
        {
            if (rect.Size == Vector2I.Zero)
            {
                continue;
            }

            int minX = Math.Clamp(rect.Position.X, 0, tilesX);
            int minY = Math.Clamp(rect.Position.Y, 0, tilesY);
            int maxX = Math.Clamp(rect.Position.X + rect.Size.X, 0, tilesX);
            int maxY = Math.Clamp(rect.Position.Y + rect.Size.Y, 0, tilesY);

            for (int ty = minY; ty < maxY; ty++)
            {
                int rowOffset = ty * tilesX;
                for (int tx = minX; tx < maxX; tx++)
                {
                    mask[rowOffset + tx] = true;
                    any = true;
                }
            }
        }

        return any ? mask : null;
    }

    public static bool[]? FromTilesBool(int tilesX, int tilesY, IEnumerable<Vector2I>? tiles)
    {
        if (tiles is null)
        {
            return null;
        }

        bool[] mask = new bool[tilesX * tilesY];
        bool any = false;

        foreach (Vector2I tile in tiles)
        {
            if ((uint)tile.X >= (uint)tilesX || (uint)tile.Y >= (uint)tilesY)
            {
                continue;
            }

            mask[tile.Y * tilesX + tile.X] = true;
            any = true;
        }

        return any ? mask : null;
    }

    public static int[] FromTiles(Vector2I[] tiles, FimConfig config)
    {
        int[] mask = new int[tiles.Length];
    
        for (var i = 0; i < mask.Length; i++)
        {
            mask[i] = FlowTile.TileIndexToFlatIndex(tiles[i], config);
        }
    
        return mask;
    }
    
    public static bool[]? Merge(int tilesX, int tilesY, params bool[]?[] sources)
    {
        bool[]? result = null;

        foreach (bool[]? source in sources)
        {
            if (source is null)
            {
                continue;
            }

            if (result is null)
            {
                result = (bool[])source.Clone();
                continue;
            }

            for (int i = 0; i < result.Length; i++)
            {
                result[i] |= source[i];
            }
        }

        return result;
    }

    public static int Count(bool[] mask)
    {
        int count = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                count++;
            }
        }

        return count;
    }
}

