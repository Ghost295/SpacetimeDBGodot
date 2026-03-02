using SpacetimeDB;

[SpacetimeDB.Table(Accessor = "Battle", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "battle_by_match_id", Columns = new[] { nameof(MatchId) })]
[SpacetimeDB.Index.BTree(Accessor = "battle_by_round_id", Columns = new[] { nameof(RoundId) })]
public partial struct Battle
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong BattleId;

    public ulong MatchId;
    public ulong RoundId;
    public Identity PlayerA;
    public Identity PlayerB;

    public byte Status;
    public uint TickRateTps;
    public uint SnapshotEveryNTicks;
    public int SnapshotRetention;
    public int MaxTicks;
    public int CurrentTick;
    public int UnitCount;
    public string StaticContentHash;

    public Fix64 WorldMinX;
    public Fix64 WorldMaxX;
    public Fix64 WorldMinY;
    public Fix64 WorldMaxY;

    public byte WinnerTeam;
    public bool HasWinnerPlayer;
    public Identity WinnerPlayer;

    public ulong LastDigest;
    public Timestamp CreatedAt;
    public Timestamp CompletedAt;
    public bool HasCompletedAt;
}

[SpacetimeDB.Table(Accessor = "BattleState")]
public partial struct BattleState
{
    [SpacetimeDB.PrimaryKey]
    public ulong BattleId;
    public BattleStateBlob State;
}

[SpacetimeDB.Table(Accessor = "BattleTickTimer", Scheduled = nameof(Module.TickBattle))]
public partial struct BattleTickTimer
{
    [SpacetimeDB.PrimaryKey]
    public ulong BattleId;
    public ScheduleAt ScheduledAt;
}

[SpacetimeDB.Table(Accessor = "BattleSnapshot", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "battle_snapshot_by_battle_id", Columns = new[] { nameof(BattleId) })]
public partial struct BattleSnapshot
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong SnapshotId;

    public ulong BattleId;
    public ulong MatchId;
    public ulong RoundId;
    public int Tick;
    public ulong Digest;
    public BattleSnapshotBlob Snapshot;
    public Timestamp CreatedAt;
}
