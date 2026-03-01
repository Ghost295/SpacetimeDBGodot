using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void Add(ReducerContext ctx)
    {
        ctx.Db.Entity.Insert(new Entity() {});
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
