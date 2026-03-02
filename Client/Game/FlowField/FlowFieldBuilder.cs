using System;
using System.Linq;
using static SpacetimeDB.Game.FlowField.FlowFlags;
using Godot;
using SpacetimeDB.Game.FlowField.FIM;
using SpacetimeDB.Game.FlowField.Tile;

namespace SpacetimeDB.Game.FlowField;

public static class FlowFieldBuilder
{
    private static readonly (int dx, int dy, int dir)[] NeighborDirections =
    {
        (1, 0, 0),   // E
        (1, -1, 1),  // NE
        (0, -1, 2),  // N
        (-1, -1, 3), // NW
        (-1, 0, 4),  // W
        (-1, 1, 5),  // SW
        (0, 1, 6),   // S
        (1, 1, 7)    // SE
    };

    private const float NeighborEpsilon = 1e-4f;
    private const float GradientThreshold = 1e-6f;
    
    // Nudges directions away from adjacent unpathable cells.
    // This is intentionally small; it's just enough to prevent "slightly through wall" arrows near boundaries.
    private const float WallBiasStrength = 0.35f;

    public static FlowField BuildFlowField(IntegrationField integration, CostField costField, FimConfig config, int[] allowedTiles = null)
    {
        ArgumentNullException.ThrowIfNull(integration);
        ArgumentNullException.ThrowIfNull(costField);
        ArgumentNullException.ThrowIfNull(config);

        if (integration.Width != costField.Width || integration.Height != costField.Height)
        {
            throw new ArgumentException("Integration field and cost field dimensions must match.");
        }

        int width = costField.Width;
        int height = costField.Height;
        FlowField flowField = new(width, height);

        float[] costs = integration.Costs;
        byte[] flagBytes = integration.Flags;
        
        int tileSize = Math.Max(1, config.TileSize);
        int tilesX = (width + tileSize - 1) / tileSize;

        if (allowedTiles is not null)
        {
            var cells = FlowCell.GetCells(config, allowedTiles);
            foreach (var cell in cells)
            {
                // Calculate cell coordinates from the pre-calculated index
                int cellX = cell.Index % width;
                int cellY = cell.Index / width;
                
                // Use the pre-calculated index for array access
                int index = cell.Index;
                float currentCost = costs[index];
                IntegrationFlags integrationFlags = (IntegrationFlags)flagBytes[index];
            
                bool pathable = (integrationFlags & IntegrationFlags.Pathable) != 0;
                bool los = (integrationFlags & IntegrationFlags.LineOfSight) != 0;
            
                FlowFieldFlags flowFlags = FlowFieldFlags.None;
                if (pathable)
                {
                    flowFlags |= FlowFieldFlags.Pathable;
                }
                if (pathable && los)
                {
                    flowFlags |= FlowFieldFlags.HasLineOfSight;
                }
            
                int directionIndex = 0;
            
                if (pathable && currentCost < config.InfinityValue)
                {
                    Vector2 direction = ComputeGradientDirection(cellX, cellY, width, height, costs, config.InfinityValue);
                    direction = ApplyWallBias(cellX, cellY, width, height, flagBytes, direction);
            
                    if (direction.LengthSquared() >= GradientThreshold)
                    {
                        directionIndex = QuantizeToDirectionIndexConstrained(direction, cellX, cellY, width, height, flagBytes);
                    }
                    else
                    {
                        directionIndex = QuantizeFallbackDirection(cellX, cellY, width, height, costs, flagBytes, currentCost);
                    }
                }
            
                flowField.Set(cellX, cellY, directionIndex, flowFlags);
            }
            
            return flowField;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                float currentCost = costs[idx];
                IntegrationFlags integrationFlags = (IntegrationFlags)flagBytes[idx];

                bool pathable = (integrationFlags & IntegrationFlags.Pathable) != 0;
                bool los = (integrationFlags & IntegrationFlags.LineOfSight) != 0;

                FlowFieldFlags flowFlags = FlowFieldFlags.None;
                if (pathable)
                {
                    flowFlags |= FlowFieldFlags.Pathable;
                }
                if (pathable && los)
                {
                    flowFlags |= FlowFieldFlags.HasLineOfSight;
                }

                int directionIndex = 0;

                if (pathable && currentCost < config.InfinityValue)
                {
                    Vector2 direction = ComputeGradientDirection(x, y, width, height, costs, config.InfinityValue);
                    direction = ApplyWallBias(x, y, width, height, flagBytes, direction);

                    if (direction.LengthSquared() >= GradientThreshold)
                    {
                        directionIndex = QuantizeToDirectionIndexConstrained(direction, x, y, width, height, flagBytes);
                    }
                    else
                    {
                        directionIndex = QuantizeFallbackDirection(x, y, width, height, costs, flagBytes, currentCost);
                    }
                }

                flowField.Set(x, y, directionIndex, flowFlags);
            }
        }

        return flowField;
    }

    private static Vector2 ComputeGradientDirection(int x, int y, int width, int height, float[] costs, float infinityValue)
    {
        int idx = y * width + x;
        float current = costs[idx];

        float left = x > 0 ? costs[idx - 1] : current;
        float right = x + 1 < width ? costs[idx + 1] : current;
        float up = y > 0 ? costs[idx - width] : current;
        float down = y + 1 < height ? costs[idx + width] : current;

        Vector2 gradient = Vector2.Zero;

        bool leftValid = left < infinityValue;
        bool rightValid = right < infinityValue;
        bool upValid = up < infinityValue;
        bool downValid = down < infinityValue;

        if (leftValid && rightValid)
        {
            gradient.X = -(right - left) * 0.5f;
        }
        else if (leftValid)
        {
            gradient.X = -(current - left);
        }
        else if (rightValid)
        {
            gradient.X = -(right - current);
        }

        if (upValid && downValid)
        {
            gradient.Y = -(down - up) * 0.5f;
        }
        else if (upValid)
        {
            gradient.Y = -(current - up);
        }
        else if (downValid)
        {
            gradient.Y = -(down - current);
        }

        return gradient;
    }

    private static int QuantizeFallbackDirection(int x, int y, int width, int height, float[] costs, byte[] flagBytes, float currentCost)
    {
        int bestDir = -1;
        float bestCost = currentCost;

        foreach ((int dx, int dy, int dir) in NeighborDirections)
        {
            int nx = x + dx;
            int ny = y + dy;

            if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
            {
                continue;
            }

            int nIdx = ny * width + nx;
            if ((flagBytes[nIdx] & (byte)IntegrationFlags.Pathable) == 0)
            {
                continue;
            }
            
            // Prevent diagonal "corner cutting" into/through walls.
            // If we're considering a diagonal step, require both cardinal-adjacent cells to be pathable too.
            if (dx != 0 && dy != 0)
            {
                int ax = x + dx;
                int ay = y;
                int bx = x;
                int by = y + dy;
                if (!IsPathable(ax, ay, width, height, flagBytes) || !IsPathable(bx, by, width, height, flagBytes))
                {
                    continue;
                }
            }

            float neighborCost = costs[nIdx];
            if (neighborCost + NeighborEpsilon < bestCost)
            {
                bestCost = neighborCost;
                bestDir = dir;
            }
        }

        if (bestDir < 0)
        {
            return 0;
        }

        Vector2 fallbackDirection = DirectionLUT.Directions[Math.Min(bestDir, DirectionLUT.Directions.Length - 1)];
        return QuantizeToDirectionIndexConstrained(fallbackDirection, x, y, width, height, flagBytes);
    }

    private static int QuantizeToDirectionIndexConstrained(Vector2 direction, int x, int y, int width, int height, byte[] flagBytes)
    {
        if (direction == Vector2.Zero)
        {
            return 0;
        }

        direction = direction.Normalized();
        Vector2[] directions = DirectionLUT.Directions;
        int bestIndex = 0;
        float bestDot = float.NegativeInfinity;

        for (int i = 0; i < directions.Length; i++)
        {
            float dot = directions[i].Dot(direction);

            // Disallow directions that immediately head into an unpathable cell, or diagonally clip corners.
            if (!IsDirectionAllowed(x, y, width, height, flagBytes, directions[i]))
            {
                continue;
            }

            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = i;
            }
        }

        // If nothing was allowed (e.g. surrounded), keep direction zero.
        if (bestDot == float.NegativeInfinity)
        {
            return 0;
        }

        return bestIndex & 0x0F;
    }
    
    private static Vector2 ApplyWallBias(int x, int y, int width, int height, byte[] flagBytes, Vector2 direction)
    {
        // If the cell isn't adjacent to any wall, skip.
        Vector2 repel = Vector2.Zero;
        int blockedCount = 0;

        // 8-neighbor repulsion. We keep a fixed iteration order for determinism.
        for (int i = 0; i < NeighborDirections.Length; i++)
        {
            (int dx, int dy, _) = NeighborDirections[i];
            int nx = x + dx;
            int ny = y + dy;

            if (IsPathable(nx, ny, width, height, flagBytes))
            {
                continue;
            }

            // Repel away from the blocked neighbor.
            // Weight diagonals a bit less so boundaries don't feel too "sticky".
            float w = (dx != 0 && dy != 0) ? 0.7f : 1.0f;
            repel.X -= dx * w;
            repel.Y -= dy * w;
            blockedCount++;
        }

        if (blockedCount == 0)
        {
            return direction;
        }

        // Blend and return. We do not normalize here; Quantize will normalize.
        return direction + repel * WallBiasStrength;
    }

    private static bool IsPathable(int x, int y, int width, int height, byte[] flagBytes)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return false;
        }

        int idx = y * width + x;
        return (flagBytes[idx] & (byte)IntegrationFlags.Pathable) != 0;
    }
    
    private static bool IsDirectionAllowed(int x, int y, int width, int height, byte[] flagBytes, Vector2 dir)
    {
        // Determine the primary grid step for this direction.
        const float eps = 1e-6f;
        int dx = dir.X > eps ? 1 : (dir.X < -eps ? -1 : 0);
        int dy = dir.Y > eps ? 1 : (dir.Y < -eps ? -1 : 0);
        if (dx == 0 && dy == 0)
        {
            return false;
        }

        int nx = x + dx;
        int ny = y + dy;
        if (!IsPathable(nx, ny, width, height, flagBytes))
        {
            return false;
        }

        // Diagonal corner-cut prevention: require both adjacent cardinals open.
        if (dx != 0 && dy != 0)
        {
            if (!IsPathable(x + dx, y, width, height, flagBytes))
            {
                return false;
            }
            if (!IsPathable(x, y + dy, width, height, flagBytes))
            {
                return false;
            }
        }

        return true;
    }
}

