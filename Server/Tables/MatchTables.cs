using SpacetimeDB;

[SpacetimeDB.Table(Accessor = "Match", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "match_by_status", Columns = new[] { nameof(Status) })]
public partial struct Match
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong MatchId;

    public Identity CreatedBy;
    public byte Status;
    public int CurrentRound;

    public uint TickRateTps;
    public uint SnapshotEveryNTicks;
    public int SnapshotRetention;
    public int MaxBattleTicks;
    public int UnitsPerPlayer;
    public int StartingHealth;
    public int RoundDamage;
    public int ShopBaseDurationSeconds;
    public int ShopDurationIncreaseSeconds;
    public int ShopGoldPerRound;
    public int ShopRerollCost;
    public int ShopOffersPerRound;
    public int ShopMaxLevel;
    public int ShopBaseUpgradeCost;
    public int ShopUpgradeCostPerLevel;
    public string StaticContentHash;
    public string MapFlowFieldHash;

    public Timestamp CreatedAt;
    public Timestamp UpdatedAt;
    public bool HasWinner;
    public Identity Winner;
}

[SpacetimeDB.Table(Accessor = "MatchPlayer", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "match_player_by_match_id", Columns = new[] { nameof(MatchId) })]
[SpacetimeDB.Index.BTree(Accessor = "match_player_by_player_identity", Columns = new[] { nameof(PlayerIdentity) })]
public partial struct MatchPlayer
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong MatchPlayerId;

    public ulong MatchId;
    public Identity PlayerIdentity;
    public int SeatIndex;
    public int Health;
    public bool Eliminated;
    public Timestamp JoinedAt;
}

[SpacetimeDB.Table(Accessor = "MatchRound", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "match_round_by_match_id", Columns = new[] { nameof(MatchId) })]
public partial struct MatchRound
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong RoundId;

    public ulong MatchId;
    public int RoundNumber;
    public byte Status;
    public int BattleCount;
    public Timestamp StartedAt;
    public Timestamp EndedAt;
    public bool HasEndedAt;
}

[SpacetimeDB.Table(Accessor = "ShopRoundTimer", Scheduled = nameof(Module.TickShopRoundTimer))]
public partial struct ShopRoundTimer
{
    [SpacetimeDB.PrimaryKey]
    public ulong MatchId;

    public ulong RoundId;
    public int RoundNumber;
    public ScheduleAt ScheduledAt;
}
