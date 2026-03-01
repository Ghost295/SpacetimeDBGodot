using SpacetimeDB;
using SpacetimeDB.Game;
using SpacetimeDB.Game.VAT;

namespace Framework;

public partial class Autoloads : AutoloadsFramework
{
    public static Autoloads Instance { get; private set; }

    // For example:
    // public WorldManager WorldManager { get; private set; }
    public SpacetimeSync SpacetimeSync { get; private set; }
    
    public VATModelManager VATModelManager { get; private set; }
    
    protected override void EnterTree()
    {
        Instance = this;
        // WorldManager = new WorldManager();
        SpacetimeSync = new SpacetimeSync();
        VATModelManager = new VATModelManager();
        
        AddChild(VATModelManager);
    }

    protected override void Ready()
    {
        // WorldManager.Initialize();
        SpacetimeSync.Start();
        
        VATModelManager.Initialize();
    }

    protected override void Process(double delta)
    {
        // WorldManager.Update(delta);
        SpacetimeSync.Update();
    }

    protected override void PhysicsProcess(double delta)
    {
        // WorldManager.PhysicsUpdate(delta);
    }

    // Uncomment if _Input is needed
    //public override void _Input(InputEvent @event)
    //{
    //    // WorldManager.Input(@event);
    //}

    protected override void Notification(int what)
    {
        // WorldManager.Notification(what);
    }

    protected override void ExitTree()
    {
        // WorldManager.Dispose();
        // SpacetimeSync.Dispose();
        
        Instance = null;
    }
}
