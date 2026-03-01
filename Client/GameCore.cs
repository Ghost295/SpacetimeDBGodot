using Framework;
using SpacetimeDB.Game;
using SpacetimeDB.Game.VAT;

namespace SpacetimeDB;

// Anything added here will need to be added in Autoloads.cs as well
public partial class GameCore : GameFramework
{
    // For example:
    // public static WorldManager World => Autoloads.Instance.WorldManager;
    
    public static SpacetimeSync SpacetimeSync => Autoloads.Instance.SpacetimeSync;
    
    public static VATModelManager VATModelManager => Autoloads.Instance.VATModelManager;
}
