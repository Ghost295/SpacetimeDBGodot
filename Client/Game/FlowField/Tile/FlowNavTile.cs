using System;
using System.Collections.Generic;
using Godot;
using SpacetimeDB.Game.FlowField.FIM;

namespace SpacetimeDB.Game.FlowField.Tile;

public class FlowNavTile
{
    private Vector2I _index;
    private Rect2I _navTileBounds;
    private FimConfig _config;

    public FlowNavTile(Vector2I index, Rect2I navTileBounds, FimConfig config)
    {
        _index = index;
        _navTileBounds = navTileBounds;
        _config = config;
    }
    
    public record struct TileInfo(Vector2I Index, float GlobalX, float GlobalY);

    public IEnumerable<TileInfo> GetTiles(bool[] mask = null)
    {
        int tileSize = _config.TileSize * _config.CellSize;
        int navTileWorldSize = Math.Max(1, _config.NavTileSize);
        int size = Math.Max(1, (int)MathF.Floor(navTileWorldSize / (float)tileSize));
        int originX = _config.WorldOrigin.X;
        int originZ = _config.WorldOrigin.Z;
        
        // Calculate global tile coordinates offset for this nav tile
        int navTileGlobalTileX = _navTileBounds.Position.X / tileSize;
        int navTileGlobalTileY = _navTileBounds.Position.Y / tileSize;
        
        // Calculate total tiles in X direction for mask indexing
        int tilesX = (_config.FieldSize.X + tileSize - 1) / tileSize;
        
        for (int localY = 0; localY < size; localY++)
        {
            for (int localX = 0; localX < size; localX++)
            {
                // Calculate global tile indices
                int globalTileX = navTileGlobalTileX + localX;
                int globalTileY = navTileGlobalTileY + localY;
                
                // Calculate global tile index for mask lookup
                int globalTileIndex = globalTileY * tilesX + globalTileX;
                
                if (mask != null && !mask[globalTileIndex])
                    continue;
                
                int globalX = originX + _navTileBounds.Position.X + localX * tileSize;
                int globalY = originZ + _navTileBounds.Position.Y + localY * tileSize;
                
                // yield return new FlowTile(new Vector2I(localX, localY), new Rect2I(globalX, globalY, _config.TileSize * _config.CellSize, _config.TileSize * _config.CellSize), _config);
                yield return new TileInfo(new Vector2I(globalTileX, globalTileY), globalX, globalY);
            }
        }
    }

    public static FlowNavTile GetNavTile(Vector3 position, FimConfig config)
    {
        var navTileIndex = WorldToNavTileIndex(position, config);
        return GetNavTile(navTileIndex, config);
    }

    public static FlowNavTile GetNavTile(Vector2I navTileIndex, FimConfig config)
    {
        Vector2I tileOrigin = new Vector2I(navTileIndex.X * config.NavTileSize, navTileIndex.Y * config.NavTileSize);
        Rect2I bounds = new Rect2I(tileOrigin, new Vector2I(config.NavTileSize, config.NavTileSize));
        return new FlowNavTile(navTileIndex, bounds, config);
    }
    
    public static Vector2I WorldToNavTileIndex(Vector3 position, FimConfig config)
    {
        float navTileWorldSize = MathF.Max(1f, config.NavTileSize);
        float tileX = (position.X - config.WorldOrigin.X) / navTileWorldSize;
        float tileY = (position.Z - config.WorldOrigin.Z) / navTileWorldSize;
        return new Vector2I(Mathf.FloorToInt(tileX), Mathf.FloorToInt(tileY));
    }
}
