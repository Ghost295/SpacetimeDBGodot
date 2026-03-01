using Godot;
using SpacetimeDB.Game.VAT;

namespace SpacetimeDB.Game;

public partial class GameMain : Node3D
{
    [Export]
    public VATModel Model { get; private set; }
    
    public override void _Ready()
    {
        GameCore.SpacetimeSync.Model = Model;
        
        // var transform = new Transform3D(Basis.Identity, new Vector3(0, 2, 0));
        // GameCore.VATModelManager.SpawnInstance(Model, transform);
    }
}
