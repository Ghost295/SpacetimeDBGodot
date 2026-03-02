using Godot;
using System;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Game.VAT;
using SpacetimeDB.Types;

namespace SpacetimeDB.Game;

public class SpacetimeSync
{
    const string SERVER_URL = "http://127.0.0.1:3000";
    const string MODULE_NAME = "big-battles";
    const long FIX64_ONE_RAW = 1L << 32;
    const float FIX64_TO_FLOAT = 1.0f / FIX64_ONE_RAW;
    const byte MATCH_STATUS_SHOP = 1;
    const byte MATCH_STATUS_BATTLE = 2;
    const byte MATCH_STATUS_COMPLETED = 3;

    public event Action OnConnected;
    public event Action OnSubscriptionApplied;
    public event Action OnLobbyDataChanged;
    
    public Identity LocalIdentity { get; private set; }
    public DbConnection Conn { get; private set; }
    public bool AutoBootstrapMatchFlow { get; set; } = false;
    public bool HasActiveBattle => _hasActiveBattle;
    public ulong ActiveBattleId => _activeBattleId;

    public Dictionary<int, VATInstanceHandle> Entities = new();
    
    public VATModel Model { get; set; }

    private ulong _activeBattleId;
    private bool _hasActiveBattle;
    private bool _awaitingMatchJoin;
    private int _lastRenderedTick = -1;

    public void Start()
    {
        // Clear game state in case we've disconnected and reconnected
        Entities.Clear();
        _activeBattleId = 0;
        _hasActiveBattle = false;
        _awaitingMatchJoin = false;
        _lastRenderedTick = -1;

        // In order to build a connection to SpacetimeDB we need to register
        // our callbacks and specify a SpacetimeDB server URI and module name.
        var builder = DbConnection.Builder()
            .OnConnect(HandleConnect)
            .OnConnectError(HandleConnectError)
            .OnDisconnect(HandleDisconnect)
            .WithUri(SERVER_URL)
            .WithDatabaseName(MODULE_NAME);

        // If the user has a SpacetimeDB auth token stored in the Unity PlayerPrefs,
        // we can use it to authenticate the connection.
        // For testing purposes, it is often convenient to comment the following lines out and
        // export an executable for the project using File -> Build Settings.
        // Then, you can run the executable multiple times. Since the executable will not check for
        // a saved auth token, each run of will receive a different Identifier,
        // and their circles will be able to eat each other.
        // if (AuthToken.Token != "")
        // {
        //     GD.Print("Using auth token!");
        //     builder = builder.WithToken(AuthToken.Token);
        // }

        // Building the connection will establish a connection to the SpacetimeDB
        // server.
        Conn = builder.Build();
    }

    // Called when we connect to SpacetimeDB and receive our client identity
    void HandleConnect(DbConnection conn, Identity identity, string token)
    {
        GD.Print("Connected!");
        // AuthToken.SaveToken(token);
        
        GD.Print($"Token: {token}");
        
        LocalIdentity = identity;
        GD.Print($"Local identity: {identity}");
        
        conn.Db.Match.OnInsert += MatchOnInsert;
        conn.Db.Match.OnUpdate += MatchOnUpdate;
        conn.Db.MatchPlayer.OnInsert += MatchPlayerOnInsert;
        conn.Db.MatchPlayer.OnUpdate += MatchPlayerOnUpdate;
        conn.Db.MatchPlayerShopState.OnInsert += MatchPlayerShopStateOnInsert;
        conn.Db.MatchPlayerShopState.OnUpdate += MatchPlayerShopStateOnUpdate;
        conn.Db.MatchPlayerShopOffer.OnInsert += MatchPlayerShopOfferOnInsert;
        conn.Db.MatchPlayerShopOffer.OnUpdate += MatchPlayerShopOfferOnUpdate;
        conn.Db.MatchPlayerShopOffer.OnDelete += MatchPlayerShopOfferOnDelete;
        conn.Db.PlayerBattleView.OnInsert += PlayerBattleViewOnInsert;
        conn.Db.PlayerBattleView.OnUpdate += PlayerBattleViewOnUpdate;
        conn.Db.BattleSnapshot.OnInsert += BattleSnapshotOnInsert;
        
        GD.Print("Registered event handlers");

        OnConnected?.Invoke();
        
        // Subscribe explicitly to battle-related tables.
        Conn.SubscriptionBuilder()
            .OnApplied(HandleSubscriptionApplied)
            .Subscribe(new[]
            {
                "SELECT * FROM Match",
                "SELECT * FROM MatchPlayer",
                "SELECT * FROM MatchRound",
                "SELECT * FROM MatchPlayerCard",
                "SELECT * FROM MatchPlayerShopState",
                "SELECT * FROM MatchPlayerShopOffer",
                "SELECT * FROM PlayerBattleView",
                "SELECT * FROM Battle",
                "SELECT * FROM BattleSnapshot",
            });
        
        GD.Print("Subscribed to match/battle tables");
    }

    public void Update()
    {
        if (Conn == null)
        {
            return;
        }

        Conn.FrameTick();
    }

    void HandleConnectError(Exception ex)
    {
        GD.Print($"Connection error: {ex}");
    }

    void HandleDisconnect(DbConnection _conn, Exception ex)
    {
        GD.Print("Disconnected.");
        if (ex != null)
        {
            GD.PrintErr(ex);
        }
    }

    private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
    {
        GD.Print("Subscription applied!");
        OnSubscriptionApplied?.Invoke();
        NotifyLobbyDataChanged();
        if (AutoBootstrapMatchFlow)
        {
            BootstrapMatchAndBattle();
        }
    }

    private void MatchOnInsert(EventContext context, Match inserted)
    {
        _ = context;
        _ = inserted;
        NotifyLobbyDataChanged();
        if (AutoBootstrapMatchFlow)
        {
            BootstrapMatchAndBattle();
        }
    }

    private void MatchOnUpdate(EventContext context, Match oldRow, Match newRow)
    {
        _ = context;
        _ = oldRow;
        _ = newRow;
        NotifyLobbyDataChanged();
        if (AutoBootstrapMatchFlow)
        {
            BootstrapMatchAndBattle();
        }
    }

    private void MatchPlayerOnInsert(EventContext context, MatchPlayer inserted)
    {
        _ = context;
        _ = inserted;
        NotifyLobbyDataChanged();
        if (AutoBootstrapMatchFlow)
        {
            BootstrapMatchAndBattle();
        }
    }

    private void MatchPlayerOnUpdate(EventContext context, MatchPlayer oldRow, MatchPlayer newRow)
    {
        _ = context;
        _ = oldRow;
        _ = newRow;
        NotifyLobbyDataChanged();
        if (AutoBootstrapMatchFlow)
        {
            BootstrapMatchAndBattle();
        }
    }

    private void MatchPlayerShopStateOnInsert(EventContext context, MatchPlayerShopState inserted)
    {
        _ = context;
        _ = inserted;
        NotifyLobbyDataChanged();
    }

    private void MatchPlayerShopStateOnUpdate(EventContext context, MatchPlayerShopState oldRow, MatchPlayerShopState newRow)
    {
        _ = context;
        _ = oldRow;
        _ = newRow;
        NotifyLobbyDataChanged();
    }

    private void MatchPlayerShopOfferOnInsert(EventContext context, MatchPlayerShopOffer inserted)
    {
        _ = context;
        _ = inserted;
        NotifyLobbyDataChanged();
    }

    private void MatchPlayerShopOfferOnUpdate(EventContext context, MatchPlayerShopOffer oldRow, MatchPlayerShopOffer newRow)
    {
        _ = context;
        _ = oldRow;
        _ = newRow;
        NotifyLobbyDataChanged();
    }

    private void MatchPlayerShopOfferOnDelete(EventContext context, MatchPlayerShopOffer deleted)
    {
        _ = context;
        _ = deleted;
        NotifyLobbyDataChanged();
    }

    private void PlayerBattleViewOnInsert(EventContext context, PlayerBattleView inserted)
    {
        _ = context;
        HandlePlayerBattleViewChanged(inserted);
    }

    private void PlayerBattleViewOnUpdate(EventContext context, PlayerBattleView oldRow, PlayerBattleView newRow)
    {
        _ = context;
        _ = oldRow;
        HandlePlayerBattleViewChanged(newRow);
    }

    private void HandlePlayerBattleViewChanged(PlayerBattleView view)
    {
        if (view.PlayerIdentity != LocalIdentity)
        {
            return;
        }

        _activeBattleId = view.BattleId;
        _hasActiveBattle = view.IsActive;
        _lastRenderedTick = -1;
        NotifyLobbyDataChanged();

        if (!_hasActiveBattle)
        {
            ClearRenderedUnits();
            return;
        }

        RenderLatestSnapshotForActiveBattle();
    }

    private void BattleSnapshotOnInsert(EventContext context, BattleSnapshot inserted)
    {
        _ = context;
        if (!_hasActiveBattle || inserted.BattleId != _activeBattleId)
        {
            return;
        }

        if (inserted.Tick < _lastRenderedTick)
        {
            return;
        }

        ApplySnapshot(inserted.Snapshot);
        _lastRenderedTick = inserted.Tick;
    }

    private void BootstrapMatchAndBattle()
    {
        if (!TryGetLocalMatchPlayer(out var localMatchPlayer))
        {
            if (_awaitingMatchJoin)
            {
                return;
            }

            if (TryFindJoinableMatch(out var joinableMatchId))
            {
                GD.Print($"Joining existing match {joinableMatchId}.");
                JoinMatch(joinableMatchId);
            }
            else
            {
                GD.Print("Creating debug match for local player.");
                CreateDebugMatch();
            }
            return;
        }

        _awaitingMatchJoin = false;
    }

    private bool TryGetLocalMatchPlayer(out MatchPlayer local)
    {
        foreach (var row in Conn.Db.MatchPlayer.Iter())
        {
            if (row.PlayerIdentity == LocalIdentity && !row.Eliminated && row.Health > 0)
            {
                local = row;
                return true;
            }
        }

        local = default;
        return false;
    }

    private bool TryFindJoinableMatch(out ulong matchId)
    {
        matchId = 0;
        var found = false;
        foreach (var match in Conn.Db.Match.Iter())
        {
            if (match.Status == MATCH_STATUS_BATTLE || match.Status == MATCH_STATUS_COMPLETED)
            {
                continue;
            }

            var activePlayers = CountActivePlayers(match.MatchId);
            if (activePlayers >= 2)
            {
                continue;
            }

            if (!found || match.MatchId < matchId)
            {
                matchId = match.MatchId;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetMatch(ulong matchId, out Match matchRow)
    {
        if (Conn.Db.Match.MatchId.Find(matchId) is Match found)
        {
            matchRow = found;
            return true;
        }

        matchRow = default;
        return false;
    }

    private int CountActivePlayers(ulong matchId)
    {
        var count = 0;
        foreach (var player in Conn.Db.MatchPlayer.Iter())
        {
            if (player.MatchId == matchId && !player.Eliminated && player.Health > 0)
            {
                count++;
            }
        }

        return count;
    }

    private void RenderLatestSnapshotForActiveBattle()
    {
        if (!_hasActiveBattle)
        {
            return;
        }

        var hasLatest = false;
        BattleSnapshot latest = default;
        foreach (var snapshot in Conn.Db.BattleSnapshot.Iter())
        {
            if (snapshot.BattleId != _activeBattleId)
            {
                continue;
            }

            if (!hasLatest || snapshot.Tick > latest.Tick)
            {
                latest = snapshot;
                hasLatest = true;
            }
        }

        if (!hasLatest)
        {
            return;
        }

        ApplySnapshot(latest.Snapshot);
        _lastRenderedTick = latest.Tick;
    }

    private void ApplySnapshot(BattleSnapshotBlob snapshot)
    {
        if (Model == null)
        {
            return;
        }

        if (snapshot.Positions == null || snapshot.Health == null)
        {
            ClearRenderedUnits();
            return;
        }

        var positions = snapshot.Positions;
        var health = snapshot.Health;
        var keepAlive = new HashSet<int>();
        var count = Math.Min(snapshot.UnitCount, Math.Min(positions.Count, health.Count));
        for (var i = 0; i < count; i++)
        {
            if (health[i].Raw <= 0)
            {
                continue;
            }

            var position = positions[i];
            var worldX = Fix64ToFloat(position.X);
            var worldY = Fix64ToFloat(position.Y);
            var transform = new Transform3D(Basis.Identity, new Vector3(worldX, 1, worldY));
            keepAlive.Add(i);

            if (Entities.TryGetValue(i, out var existingHandle))
            {
                GameCore.VATModelManager.SetInstanceTransform(existingHandle, transform);
                continue;
            }

            var handle = GameCore.VATModelManager.SpawnInstance(Model, transform);
            Entities[i] = handle;
        }

        var toRemove = new List<int>();
        foreach (var key in Entities.Keys)
        {
            if (!keepAlive.Contains(key))
            {
                toRemove.Add(key);
            }
        }

        for (var i = 0; i < toRemove.Count; i++)
        {
            var key = toRemove[i];
            if (Entities.Remove(key, out var handle))
            {
                GameCore.VATModelManager.DestroyInstance(handle);
            }
        }
    }

    private static float Fix64ToFloat(Fix64 value)
    {
        return value.Raw * FIX64_TO_FLOAT;
    }

    private void ClearRenderedUnits()
    {
        foreach (var pair in Entities)
        {
            GameCore.VATModelManager.DestroyInstance(pair.Value);
        }

        Entities.Clear();
    }

    public bool IsConnected()
    {
        return Conn != null && Conn.IsActive;
    }

    public readonly struct LobbyMatchInfo
    {
        public readonly ulong MatchId;
        public readonly Identity CreatedBy;
        public readonly int ActivePlayers;
        public readonly byte Status;

        public LobbyMatchInfo(ulong matchId, Identity createdBy, int activePlayers, byte status)
        {
            MatchId = matchId;
            CreatedBy = createdBy;
            ActivePlayers = activePlayers;
            Status = status;
        }
    }

    public readonly struct ShopStateInfo
    {
        public readonly ulong MatchId;
        public readonly ulong RoundId;
        public readonly int RoundNumber;
        public readonly int Gold;
        public readonly int ShopLevel;
        public readonly bool IsFrozen;
        public readonly int OfferRollCounter;
        public readonly int OffersPerShop;
        public readonly bool RequestedBattleStart;

        public ShopStateInfo(
            ulong matchId,
            ulong roundId,
            int roundNumber,
            int gold,
            int shopLevel,
            bool isFrozen,
            int offerRollCounter,
            int offersPerShop,
            bool requestedBattleStart)
        {
            MatchId = matchId;
            RoundId = roundId;
            RoundNumber = roundNumber;
            Gold = gold;
            ShopLevel = shopLevel;
            IsFrozen = isFrozen;
            OfferRollCounter = offerRollCounter;
            OffersPerShop = offersPerShop;
            RequestedBattleStart = requestedBattleStart;
        }
    }

    public readonly struct ShopOfferInfo
    {
        public readonly ulong OfferId;
        public readonly int OfferIndex;
        public readonly string CardId;
        public readonly int CardStableId;
        public readonly int PriceGold;
        public readonly int CardSizeX;
        public readonly int CardSizeY;

        public ShopOfferInfo(
            ulong offerId,
            int offerIndex,
            string cardId,
            int cardStableId,
            int priceGold,
            int cardSizeX,
            int cardSizeY)
        {
            OfferId = offerId;
            OfferIndex = offerIndex;
            CardId = cardId;
            CardStableId = cardStableId;
            PriceGold = priceGold;
            CardSizeX = cardSizeX;
            CardSizeY = cardSizeY;
        }
    }

    public List<LobbyMatchInfo> GetOpenMatches()
    {
        var matches = new List<LobbyMatchInfo>();
        if (!IsConnected())
        {
            return matches;
        }

        foreach (var match in Conn.Db.Match.Iter())
        {
            if (match.Status == MATCH_STATUS_BATTLE || match.Status == MATCH_STATUS_COMPLETED)
            {
                continue;
            }

            matches.Add(new LobbyMatchInfo(
                match.MatchId,
                match.CreatedBy,
                CountActivePlayers(match.MatchId),
                match.Status));
        }

        matches.Sort((a, b) => a.MatchId.CompareTo(b.MatchId));
        return matches;
    }

    public bool TryGetLocalMatchId(out ulong matchId)
    {
        if (TryGetLocalMatchPlayer(out var local))
        {
            matchId = local.MatchId;
            return true;
        }

        matchId = 0;
        return false;
    }

    public bool TryGetLocalMatch(out Match match)
    {
        if (!TryGetLocalMatchId(out var matchId))
        {
            match = default;
            return false;
        }

        return TryGetMatch(matchId, out match);
    }

    public bool TryGetLocalShopState(out ShopStateInfo shopState)
    {
        shopState = default;
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return false;
        }

        var found = false;
        MatchPlayerShopState best = default;
        foreach (var row in Conn.Db.MatchPlayerShopState.Iter())
        {
            if (row.MatchId != matchId || row.PlayerIdentity != LocalIdentity)
            {
                continue;
            }

            if (!found
                || row.RoundNumber > best.RoundNumber
                || (row.RoundNumber == best.RoundNumber && row.UpdatedAt.MicrosecondsSinceUnixEpoch > best.UpdatedAt.MicrosecondsSinceUnixEpoch))
            {
                best = row;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        shopState = new ShopStateInfo(
            best.MatchId,
            best.RoundId,
            best.RoundNumber,
            best.Gold,
            best.ShopLevel,
            best.IsFrozen,
            best.OfferRollCounter,
            best.OffersPerShop,
            best.RequestedBattleStart);
        return true;
    }

    public List<ShopOfferInfo> GetLocalShopOffers()
    {
        var offers = new List<ShopOfferInfo>();
        if (!IsConnected() || !TryGetLocalShopState(out var shopState))
        {
            return offers;
        }

        foreach (var row in Conn.Db.MatchPlayerShopOffer.Iter())
        {
            if (row.MatchId != shopState.MatchId
                || row.RoundId != shopState.RoundId
                || row.PlayerIdentity != LocalIdentity)
            {
                continue;
            }

            offers.Add(new ShopOfferInfo(
                row.MatchPlayerShopOfferId,
                row.OfferIndex,
                row.CardId,
                row.CardStableId,
                row.PriceGold,
                row.CardSizeX,
                row.CardSizeY));
        }

        offers.Sort((a, b) => a.OfferIndex.CompareTo(b.OfferIndex));
        return offers;
    }

    public void CreateDebugMatch()
    {
        if (!IsConnected())
        {
            return;
        }

        _awaitingMatchJoin = true;
        Conn.Reducers.CreateAndJoinDebugMatch();
    }

    public void JoinMatch(ulong matchId)
    {
        if (!IsConnected())
        {
            return;
        }

        _awaitingMatchJoin = true;
        Conn.Reducers.JoinMatch(matchId);
    }

    public void BuyShopOffer(int offerIndex, int targetGridIndex)
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.BuyShopOffer(matchId, offerIndex, targetGridIndex);
    }

    public void BuyShopOffer(ulong offerId, int targetGridIndex)
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        foreach (var offer in Conn.Db.MatchPlayerShopOffer.Iter())
        {
            if (offer.MatchId != matchId ||
                offer.PlayerIdentity != LocalIdentity ||
                offer.MatchPlayerShopOfferId != offerId)
            {
                continue;
            }

            Conn.Reducers.BuyShopOffer(matchId, offer.OfferIndex, targetGridIndex);
            return;
        }
    }

    public void RerollShopOffers()
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.RerollShopOffers(matchId);
    }

    public void SetShopFreeze(bool isFrozen)
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.SetShopFreeze(matchId, isFrozen);
    }

    public void UpgradeShop()
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.UpgradeShop(matchId);
    }

    public void MoveShopCard(ulong matchPlayerCardId, byte gridIndex, int cellX, int cellZ)
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.MoveShopCard(matchId, matchPlayerCardId, gridIndex, cellX, cellZ);
    }

    public void SellShopCard(ulong matchPlayerCardId)
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.SellShopCard(matchId, matchPlayerCardId);
    }

    public void RequestBattleStart()
    {
        if (!IsConnected() || !TryGetLocalMatchId(out var matchId))
        {
            return;
        }

        Conn.Reducers.RequestBattleStart(matchId);
    }

    private void NotifyLobbyDataChanged()
    {
        OnLobbyDataChanged?.Invoke();
    }

    public void Disconnect()
    {
        ClearRenderedUnits();
        OnLobbyDataChanged = null;
        Conn.Disconnect();
        Conn = null;
    }
}
