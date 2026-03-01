using Framework;
using SpacetimeDB.Game;

namespace SpacetimeDB;

// Anything added here will need to be added in Autoloads.cs as well
public partial class GameCore : GameFramework
{
    // For example:
    // public static WorldManager World => Autoloads.Instance.WorldManager;
    
    public static SpacetimeSync SpacetimeSync => Autoloads.Instance.SpacetimeSync;
}
