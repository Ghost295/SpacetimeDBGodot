using System.Collections.Generic;
using Godot;
using SpacetimeDB.Game.FlowField.FIM;

namespace SpacetimeDB.Game.FlowField.Tile;

public partial class FlowRegion
{
    private Rect2I _bounds;
    private FimConfig _config;
    private Node _terrain3D;
    
    public GodotObject Region;

    private static readonly StringName Terrain3DData = "data";
    private static readonly StringName Terrain3DGetRegion= "get_region";
    private static readonly StringName Terrain3DGetActiveRegions= "get_regions_active";
    
    public FlowRegion(Rect2I bounds, FimConfig config, Node terrain3D, GodotObject region)
    {
        _bounds = bounds;
        _config = config;
        _terrain3D = terrain3D;
        
        Region = region;
    }
    
    private static bool CheckTerrain3D(Node terrain3D)
    {
        return terrain3D.IsClass("Terrain3D");
    }

    // public record struct NavTileInfo(Vector2I Index, float GlobalX, float GlobalY);

    public IEnumerable<FlowNavTile> GetNavTiles()
    {
        var size = _config.FieldSize.X / _config.NavTileSize;
        
        for (int localY = 0; localY < size; localY++)
        {
            for (int localX = 0; localX < size; localX++)
            {
                int globalX = localX * _config.NavTileSize;
                int globalY = localY * _config.NavTileSize;
                
                yield return new FlowNavTile(new Vector2I(localX, localY), new Rect2I(globalX, globalY, _config.NavTileSize, _config.NavTileSize), _config);
                // yield return new NavTileInfo(new Vector2I(localX, localY), globalX, globalY);
            }
        }
    }

    public static FlowRegion GetRegion(Vector3 position, FimConfig config, Node terrain3D)
    {
        var regionIndex = WorldToRegionIndex(position, config);
        return GetRegion(regionIndex, config, terrain3D);
    }

    public static FlowRegion GetRegion(Vector2I regionIndex, FimConfig config, Node terrain3D)
    {
        var region = terrain3D.Get(Terrain3DData).AsGodotObject().Call(Terrain3DGetRegion, regionIndex).AsGodotObject();
        Vector2I regionOrigin = new Vector2I(regionIndex.X * config.RegionSize, regionIndex.Y * config.RegionSize);
        return new FlowRegion(new Rect2I(regionOrigin, new Vector2I(config.RegionSize, config.RegionSize)), config, terrain3D, region);
    }
    
    public static Vector2I WorldToRegionIndex(Vector3 position, FimConfig config)
    {
        float tileX = position.X / config.RegionSize;
        float tileY = position.Z / config.RegionSize;
        return new Vector2I(Mathf.FloorToInt(tileX), Mathf.FloorToInt(tileY));
    }

    public static Resource[] GetActiveRegions(Node terrain3D)
    {
        var regions = terrain3D.Get(Terrain3DData).AsGodotObject().Call(Terrain3DGetActiveRegions).AsGodotObjectArray<Resource>();
        return regions;
    }
    
}
