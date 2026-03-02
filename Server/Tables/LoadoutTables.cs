using SpacetimeDB;
using System.Collections.Generic;

[SpacetimeDB.Table(Accessor = "MatchPlayerCard", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "match_player_card_by_match_id", Columns = new[] { nameof(MatchId) })]
[SpacetimeDB.Index.BTree(Accessor = "match_player_card_by_player_identity", Columns = new[] { nameof(PlayerIdentity) })]
public partial struct MatchPlayerCard
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong MatchPlayerCardId;

    public ulong MatchId;
    public Identity PlayerIdentity;
    public int SlotIndex;
    public int GridIndex;
    public int CellX;
    public int CellZ;
    public int CardStableId;
    public int Quantity;
    public int Level;
    public List<CardRuntimeModifierState> RuntimeModifiers;
}

[SpacetimeDB.Table(Accessor = "PlayerBattleView", Public = true)]
public partial struct PlayerBattleView
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerIdentity;

    public ulong MatchId;
    public ulong RoundId;
    public ulong BattleId;
    public Identity OpponentIdentity;
    public bool IsActive;
    public Timestamp UpdatedAt;
}
