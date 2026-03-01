using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void Add(ReducerContext ctx, int x, int y)
    {
        ctx.Db.Entity.Insert(new Entity() { position = new DBVector2(x, y) });
    }

    [SpacetimeDB.Reducer]
    public static void SayHello(ReducerContext ctx)
    {
        Log.Info("Hello, World!");
    }
    
    [Table(Accessor = "Entity", Public = true)]
    public partial struct Entity
    {
        [PrimaryKey, AutoInc]
        public int entity_id;
        public DBVector2 position;
    }
}
