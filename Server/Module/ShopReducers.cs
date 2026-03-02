using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void BuyShopOffer(ReducerContext ctx, ulong matchId, int offerIndex, int targetGridIndex)
    {
        var (match, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        var team = ResolvePlayerTeamOrThrow(ctx, matchId, player.PlayerIdentity);
        var state = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);
        if (FindShopOffer(ctx, matchId, round.RoundId, player.PlayerIdentity, offerIndex) is not MatchPlayerShopOffer offer)
        {
            throw new Exception("Offer index is out of range.");
        }

        if (state.Gold < offer.PriceGold)
        {
            throw new Exception($"Need {offer.PriceGold} gold but only have {state.Gold}.");
        }

        var card = SimContentRegistry.GetCardByStableId(offer.CardStableId);
        ValidateGridPlacementOrThrow(team, targetGridIndex, 0, 0, card.CardSizeX, card.CardSizeY);
        if (!TryFindFirstPlacement(
                ctx,
                matchId,
                player.PlayerIdentity,
                targetGridIndex,
                card.CardSizeX,
                card.CardSizeY,
                out var targetCellX,
                out var targetCellZ))
        {
            throw new Exception("Target placement grid has no room for this card.");
        }

        var updatedState = state;
        updatedState.Gold = Math.Max(0, state.Gold - offer.PriceGold);
        updatedState.UpdatedAt = ctx.Timestamp;
        ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Update(updatedState);
        ctx.Db.MatchPlayerShopOffer.MatchPlayerShopOfferId.Delete(offer.MatchPlayerShopOfferId);

        var slotIndex = GetNextSlotIndexForPlayer(ctx, matchId, player.PlayerIdentity);
        UpsertPlayerCardSlot(
            ctx,
            matchId,
            player.PlayerIdentity,
            slotIndex,
            targetGridIndex,
            targetCellX,
            targetCellZ,
            offer.CardStableId,
            quantity: 1,
            level: Math.Max(1, updatedState.ShopLevel),
            new List<CardRuntimeModifierState>());
    }

    [SpacetimeDB.Reducer]
    public static void RerollShopOffers(ReducerContext ctx, ulong matchId)
    {
        var (match, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        var state = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);
        var rerollCost = Math.Max(0, match.ShopRerollCost);
        if (state.Gold < rerollCost)
        {
            throw new Exception($"Need {rerollCost} gold but only have {state.Gold}.");
        }

        var updatedState = state;
        updatedState.Gold = Math.Max(0, state.Gold - rerollCost);
        updatedState.OfferRollCounter = Math.Max(0, state.OfferRollCounter + 1);
        updatedState.IsFrozen = false;
        updatedState.UpdatedAt = ctx.Timestamp;
        ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Update(updatedState);

        GenerateShopOffers(
            ctx,
            match,
            round.RoundId,
            round.RoundNumber,
            player.PlayerIdentity,
            updatedState.OfferRollCounter,
            Math.Max(1, updatedState.OffersPerShop));
    }

    [SpacetimeDB.Reducer]
    public static void SetShopFreeze(ReducerContext ctx, ulong matchId, bool freeze)
    {
        var (_, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        var state = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);
        if (state.IsFrozen == freeze)
        {
            return;
        }

        var updatedState = state;
        updatedState.IsFrozen = freeze;
        updatedState.UpdatedAt = ctx.Timestamp;
        ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Update(updatedState);
    }

    [SpacetimeDB.Reducer]
    public static void UpgradeShop(ReducerContext ctx, ulong matchId)
    {
        var (match, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        var state = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);

        if (state.ShopLevel >= Math.Max(1, match.ShopMaxLevel))
        {
            throw new Exception("Shop is already at max level.");
        }

        var upgradeCost = ComputeUpgradeCost(match, state.ShopLevel);
        if (state.Gold < upgradeCost)
        {
            throw new Exception($"Need {upgradeCost} gold but only have {state.Gold}.");
        }

        var updatedState = state;
        updatedState.Gold = Math.Max(0, state.Gold - upgradeCost);
        updatedState.ShopLevel = Math.Max(1, state.ShopLevel + 1);
        updatedState.UpdatedAt = ctx.Timestamp;
        ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Update(updatedState);
    }

    [SpacetimeDB.Reducer]
    public static void MoveShopCard(
        ReducerContext ctx,
        ulong matchId,
        ulong matchPlayerCardId,
        int targetGridIndex,
        int targetCellX,
        int targetCellZ)
    {
        var (_, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        _ = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);
        if (ctx.Db.MatchPlayerCard.MatchPlayerCardId.Find(matchPlayerCardId) is not MatchPlayerCard row ||
            row.MatchId != matchId ||
            row.PlayerIdentity != player.PlayerIdentity)
        {
            throw new Exception("Card instance was not found for this player.");
        }

        var card = SimContentRegistry.GetCardByStableId(row.CardStableId);
        var team = ResolvePlayerTeamOrThrow(ctx, matchId, player.PlayerIdentity);
        ValidateGridPlacementOrThrow(team, targetGridIndex, targetCellX, targetCellZ, card.CardSizeX, card.CardSizeY);
        if (!CanPlaceCardAt(
                ctx,
                matchId,
                player.PlayerIdentity,
                targetGridIndex,
                card.CardSizeX,
                card.CardSizeY,
                targetCellX,
                targetCellZ,
                row.MatchPlayerCardId))
        {
            throw new Exception("Target placement collides with another card.");
        }

        var updated = row;
        updated.GridIndex = targetGridIndex;
        updated.CellX = targetCellX;
        updated.CellZ = targetCellZ;
        ctx.Db.MatchPlayerCard.MatchPlayerCardId.Update(updated);
    }

    [SpacetimeDB.Reducer]
    public static void SellShopCard(ReducerContext ctx, ulong matchId, ulong matchPlayerCardId)
    {
        var (_, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        _ = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);
        if (ctx.Db.MatchPlayerCard.MatchPlayerCardId.Find(matchPlayerCardId) is not MatchPlayerCard row ||
            row.MatchId != matchId ||
            row.PlayerIdentity != player.PlayerIdentity)
        {
            throw new Exception("Card instance was not found for this player.");
        }

        ctx.Db.MatchPlayerCard.MatchPlayerCardId.Delete(row.MatchPlayerCardId);
    }

    [SpacetimeDB.Reducer]
    public static void RequestBattleStart(ReducerContext ctx, ulong matchId)
    {
        var (match, round) = RequireShopPhaseOrThrow(ctx, matchId);
        var player = EnsureShopPlayerOrThrow(ctx, matchId, ctx.Sender);
        var state = GetShopStateOrThrow(ctx, matchId, round.RoundId, player.PlayerIdentity);

        if (!state.RequestedBattleStart)
        {
            var updated = state;
            updated.RequestedBattleStart = true;
            updated.UpdatedAt = ctx.Timestamp;
            ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Update(updated);
        }

        var activePlayers = GetActivePlayersForMatch(matchId, ctx);
        if (activePlayers.Count < 2)
        {
            return;
        }

        var readyCount = 0;
        for (var i = 0; i < activePlayers.Count; i++)
        {
            var active = activePlayers[i];
            if (FindShopState(ctx, matchId, round.RoundId, active.PlayerIdentity) is MatchPlayerShopState activeState &&
                activeState.RequestedBattleStart)
            {
                readyCount++;
            }
        }

        if (readyCount >= 2)
        {
            StartBattleRoundInternal(ctx, match, round, skipIfNotShop: true);
        }
    }

    private static (Match Match, MatchRound Round) RequireShopPhaseOrThrow(ReducerContext ctx, ulong matchId)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Match is completed.");
        }

        if (match.Status != SimConstants.MatchShop)
        {
            throw new Exception("Shop actions are only allowed during shop phase.");
        }

        if (match.CurrentRound <= 0)
        {
            throw new Exception("Current round is not initialized.");
        }

        if (FindCurrentRound(ctx, matchId, match.CurrentRound) is not MatchRound round ||
            round.Status != SimConstants.RoundPending)
        {
            throw new Exception("Current round is not in shop status.");
        }

        return (match, round);
    }

    private static MatchPlayer EnsureShopPlayerOrThrow(ReducerContext ctx, ulong matchId, Identity identity)
    {
        if (FindPlayer(ctx, matchId, identity) is MatchPlayer player &&
            !player.Eliminated &&
            player.Health > 0)
        {
            return player;
        }

        throw new Exception("Player is not active in this match.");
    }

    private static List<MatchPlayer> GetActivePlayersForMatch(ulong matchId, ReducerContext ctx)
    {
        var players = GetPlayersForMatch(ctx, matchId);
        var active = new List<MatchPlayer>(2);
        for (var i = 0; i < players.Count; i++)
        {
            if (!players[i].Eliminated && players[i].Health > 0)
            {
                active.Add(players[i]);
            }
        }

        active.Sort((a, b) => a.SeatIndex.CompareTo(b.SeatIndex));
        return active;
    }
}
