using SpacetimeDB;

[SpacetimeDB.Table(Accessor = "MatchPlayerShopState", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "match_player_shop_state_by_match_id", Columns = new[] { nameof(MatchId) })]
[SpacetimeDB.Index.BTree(Accessor = "match_player_shop_state_by_player_identity", Columns = new[] { nameof(PlayerIdentity) })]
[SpacetimeDB.Index.BTree(Accessor = "match_player_shop_state_by_round_id", Columns = new[] { nameof(RoundId) })]
public partial struct MatchPlayerShopState
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong MatchPlayerShopStateId;

    public ulong MatchId;
    public ulong RoundId;
    public int RoundNumber;
    public Identity PlayerIdentity;

    public int Gold;
    public int ShopLevel;
    public bool IsFrozen;
    public int OfferRollCounter;
    public int OffersPerShop;
    public bool RequestedBattleStart;

    public Timestamp UpdatedAt;
}

[SpacetimeDB.Table(Accessor = "MatchPlayerShopOffer", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "match_player_shop_offer_by_match_id", Columns = new[] { nameof(MatchId) })]
[SpacetimeDB.Index.BTree(Accessor = "match_player_shop_offer_by_player_identity", Columns = new[] { nameof(PlayerIdentity) })]
[SpacetimeDB.Index.BTree(Accessor = "match_player_shop_offer_by_round_id", Columns = new[] { nameof(RoundId) })]
public partial struct MatchPlayerShopOffer
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong MatchPlayerShopOfferId;

    public ulong MatchId;
    public ulong RoundId;
    public int RoundNumber;
    public Identity PlayerIdentity;
    public int OfferIndex;

    public int CardStableId;
    public string CardId;
    public int PriceGold;
    public int CardSizeX;
    public int CardSizeY;
}
