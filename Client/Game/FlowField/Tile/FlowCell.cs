using Godot;
using SpacetimeDB.Game.FlowField.FIM;
using System.Collections.Generic;

namespace SpacetimeDB.Game.FlowField.Tile;

public class FlowCell
{
    public record struct CellInfo(int Index, Vector2I CellIndex, int LocalX, int LocalY, int GlobalX, int GlobalY);
    
    public static IEnumerable<CellInfo> GetCells(FimConfig config, int[] mask = null)
    {
        if (mask is not null)
        {
            int width = config.FieldSize.X / config.CellSize;
            int height = config.FieldSize.Y / config.CellSize;
            int originX = config.WorldOrigin.X;
            int originZ = config.WorldOrigin.Z;

            int tilesX = (width + config.TileSize - 1) / config.TileSize;
            
            foreach (var tileIndex in mask)
            {
                int tileX = tileIndex % tilesX;
                int tileY = tileIndex / tilesX;
                int startX = tileX * config.TileSize;
                int startY = tileY * config.TileSize;

                int tileWidth = Mathf.Min(config.TileSize, width - startX);
                int tileHeight = Mathf.Min(config.TileSize, height - startY);
                
                for (int localY = 0; localY < tileHeight; localY++)
                {
                    int globalIndexY = startY + localY;
                    int rowOffset = globalIndexY * width;

                    for (int localX = 0; localX < tileWidth; localX++)
                    {
                        int globalIndexX = startX + localX;
                        int index = rowOffset + globalIndexX;
                    
                        int globalX = originX + globalIndexX * config.CellSize;
                        int globalY = originZ + globalIndexY * config.CellSize;
                        
                        yield return new CellInfo(index, new Vector2I(index % width, index / width), localX, localY, globalX, globalY);
                    }
                }
            }   
        }
        else
        {
            var width = config.FieldSize.X / config.CellSize;
            var height = config.FieldSize.Y / config.CellSize;
            int originX = config.WorldOrigin.X;
            int originZ = config.WorldOrigin.Z;
        
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    var index = localY * width + localX;
                    
                    var globalX = originX + localX * config.CellSize;
                    var globalY = originZ + localY * config.CellSize;
                
                    yield return new CellInfo(index, new Vector2I(index % width, index / width),  localX, localY, globalX, globalY);
                }
            }
        }
    }
    
    public static Vector3 CellToWorld(Vector2I cell, FimConfig config)
    {
        float worldX = config.WorldOrigin.X + (cell.X + 0.5f) * config.CellSize;
        float worldY = config.WorldOrigin.Y;
        float worldZ = config.WorldOrigin.Z + (cell.Y + 0.5f) * config.CellSize;
        return new Vector3(worldX, worldY, worldZ);
    }

    public static Vector2I WorldToCell(Vector3 position, FimConfig config)
    {
        float relativeX = position.X - config.WorldOrigin.X;
        float relativeZ = position.Z - config.WorldOrigin.Z;
        int cellX = Mathf.FloorToInt(relativeX / config.CellSize);
        int cellY = Mathf.FloorToInt(relativeZ / config.CellSize);
        return new Vector2I(cellX, cellY);
    }
}
