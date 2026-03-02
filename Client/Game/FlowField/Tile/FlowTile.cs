using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Game.FlowField.FIM;

namespace SpacetimeDB.Game.FlowField.Tile;

public sealed class FlowTile
{
    public Vector2I Index;
    
    private Rect2I _tileBounds;
    private FimConfig _config;

    public FlowTile(Vector2I index, Rect2I tileBounds, FimConfig config)
    {
        Index = index;
        _tileBounds = tileBounds;
        _config = config;
    }
    
    public IEnumerable<FlowCell.CellInfo> GetCells()
    {
        var width = _config.FieldSize.X / _config.CellSize;
        int originX = _config.WorldOrigin.X;
        int originZ = _config.WorldOrigin.Z;
    
        for (int localY = 0; localY < _tileBounds.Size.Y; localY++)
        {
            int globalIndexY = _tileBounds.Position.Y + localY;
            int rowOffset = globalIndexY * width;
    
            for (int localX = 0; localX < _tileBounds.Size.X; localX++)
            {
                int globalIndexX = _tileBounds.Position.X + localX;
                int index = rowOffset + globalIndexX;
                
                int globalX = originX + globalIndexX * _config.CellSize;
                int globalY = originZ + globalIndexY * _config.CellSize;
                
                yield return new FlowCell.CellInfo(index, new Vector2I(index % width, index / width), localX, localY, globalX, globalY);
            }
        }
    }

    public static FlowTile GetTile(Vector3 position, FimConfig config)
    {
        var tileIndex = WorldToTileIndex(position, config);
        return GetTile(tileIndex, config);
    }

    public static FlowTile GetTile(Vector2I tileIndex, FimConfig config)
    {
        Vector2I tileOrigin = new Vector2I(tileIndex.X * config.TileSize, tileIndex.Y * config.TileSize);
        Rect2I bounds = new Rect2I(tileOrigin, new Vector2I(config.TileSize, config.TileSize));
        return new FlowTile(tileIndex, bounds, config);
    }
    
    public static Vector2I WorldToTileIndex(Vector3 position, FimConfig config)
    {
        Vector2I cell = FlowCell.WorldToCell(position, config);
        return CellToTileIndex(cell, config);
    }

    public static Vector2I CellToTileIndex(Vector2I cellIndex, FimConfig config)
    {
        int tileX = Mathf.FloorToInt(cellIndex.X / (float)config.TileSize);
        int tileY = Mathf.FloorToInt(cellIndex.Y / (float)config.TileSize);
        return new Vector2I(tileX, tileY);
    }
    
    public static int TileIndexToFlatIndex(Vector2I tileIndex, FimConfig config)
    {
        var width = config.FieldSize.X / (config.TileSize * config.CellSize);
        return tileIndex.X + tileIndex.Y * width;
    }

}
