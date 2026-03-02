using SpacetimeDB;

[SpacetimeDB.Table(Accessor = "BattleCommand", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "battle_command_by_battle_id", Columns = new[] { nameof(BattleId) })]
public partial struct BattleCommand
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong CommandId;

    public ulong BattleId;
    public Identity Issuer;
    public int UnitIndex;
    public int TargetUnitIndex;
    public int TickIssued;
    public bool Consumed;
}

[SpacetimeDB.Table(Accessor = "BattleStatusCommand", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "battle_status_command_by_battle_id", Columns = new[] { nameof(BattleId) })]
public partial struct BattleStatusCommand
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong CommandId;

    public ulong BattleId;
    public Identity Issuer;
    public int UnitIndex;
    public byte StatusKind;
    public bool IsPermanent;
    public int DurationTicks;
    public int TickIssued;
    public bool Consumed;
}
