using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    private static MatchPlayerShopState? FindShopState(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerIdentity)
    {
        foreach (var row in ctx.Db.MatchPlayerShopState.Iter())
        {
            if (row.MatchId == matchId &&
                row.RoundId == roundId &&
                row.PlayerIdentity == playerIdentity)
            {
                return row;
            }
        }

        return null;
    }

    private static MatchPlayerShopState? FindLatestShopStateForPlayer(
        ReducerContext ctx,
        ulong matchId,
        Identity playerIdentity)
    {
        MatchPlayerShopState? found = null;
        foreach (var row in ctx.Db.MatchPlayerShopState.Iter())
        {
            if (row.MatchId != matchId || row.PlayerIdentity != playerIdentity)
            {
                continue;
            }

            if (found is not MatchPlayerShopState current ||
                row.RoundNumber > current.RoundNumber ||
                (row.RoundNumber == current.RoundNumber && row.MatchPlayerShopStateId > current.MatchPlayerShopStateId))
            {
                found = row;
            }
        }

        return found;
    }

    private static MatchPlayerShopState GetShopStateOrThrow(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerIdentity)
    {
        if (FindShopState(ctx, matchId, roundId, playerIdentity) is MatchPlayerShopState state)
        {
            return state;
        }

        throw new Exception("Shop state was not found for this player.");
    }

    private static List<MatchPlayerShopState> GetShopStatesForRound(ReducerContext ctx, ulong matchId, ulong roundId)
    {
        var rows = new List<MatchPlayerShopState>();
        foreach (var row in ctx.Db.MatchPlayerShopState.Iter())
        {
            if (row.MatchId == matchId && row.RoundId == roundId)
            {
                rows.Add(row);
            }
        }

        rows.Sort((a, b) =>
        {
            var roundCompare = a.RoundNumber.CompareTo(b.RoundNumber);
            if (roundCompare != 0)
            {
                return roundCompare;
            }

            var playerCompare = string.CompareOrdinal(a.PlayerIdentity.ToString(), b.PlayerIdentity.ToString());
            if (playerCompare != 0)
            {
                return playerCompare;
            }

            return a.MatchPlayerShopStateId.CompareTo(b.MatchPlayerShopStateId);
        });
        return rows;
    }

    private static List<MatchPlayerShopOffer> GetShopOffersForPlayerRound(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerIdentity)
    {
        var rows = new List<MatchPlayerShopOffer>();
        foreach (var row in ctx.Db.MatchPlayerShopOffer.Iter())
        {
            if (row.MatchId == matchId &&
                row.RoundId == roundId &&
                row.PlayerIdentity == playerIdentity)
            {
                rows.Add(row);
            }
        }

        rows.Sort((a, b) =>
        {
            var offerCompare = a.OfferIndex.CompareTo(b.OfferIndex);
            return offerCompare != 0
                ? offerCompare
                : a.MatchPlayerShopOfferId.CompareTo(b.MatchPlayerShopOfferId);
        });
        return rows;
    }

    private static MatchPlayerShopOffer? FindShopOffer(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerIdentity,
        int offerIndex)
    {
        foreach (var row in ctx.Db.MatchPlayerShopOffer.Iter())
        {
            if (row.MatchId == matchId &&
                row.RoundId == roundId &&
                row.PlayerIdentity == playerIdentity &&
                row.OfferIndex == offerIndex)
            {
                return row;
            }
        }

        return null;
    }

    private static void ClearShopOffersForPlayerRound(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerIdentity)
    {
        var rows = GetShopOffersForPlayerRound(ctx, matchId, roundId, playerIdentity);
        for (var i = 0; i < rows.Count; i++)
        {
            ctx.Db.MatchPlayerShopOffer.MatchPlayerShopOfferId.Delete(rows[i].MatchPlayerShopOfferId);
        }
    }

    private static void DeleteShopRowsForMatch(ReducerContext ctx, ulong matchId)
    {
        var statesToDelete = new List<ulong>();
        foreach (var state in ctx.Db.MatchPlayerShopState.Iter())
        {
            if (state.MatchId == matchId)
            {
                statesToDelete.Add(state.MatchPlayerShopStateId);
            }
        }

        for (var i = 0; i < statesToDelete.Count; i++)
        {
            ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Delete(statesToDelete[i]);
        }

        var offersToDelete = new List<ulong>();
        foreach (var offer in ctx.Db.MatchPlayerShopOffer.Iter())
        {
            if (offer.MatchId == matchId)
            {
                offersToDelete.Add(offer.MatchPlayerShopOfferId);
            }
        }

        for (var i = 0; i < offersToDelete.Count; i++)
        {
            ctx.Db.MatchPlayerShopOffer.MatchPlayerShopOfferId.Delete(offersToDelete[i]);
        }

        DeleteShopTimer(ctx, matchId);
    }

    private static int ComputeShopDurationSeconds(Match match, int roundNumber)
    {
        var safeRound = Math.Max(1, roundNumber);
        var baseDuration = Math.Max(1, match.ShopBaseDurationSeconds);
        var durationIncrease = Math.Max(0, match.ShopDurationIncreaseSeconds);
        return baseDuration + ((safeRound - 1) * durationIncrease);
    }

    private static int ComputeUpgradeCost(Match match, int currentShopLevel)
    {
        var safeLevel = Math.Max(1, currentShopLevel);
        if (safeLevel >= Math.Max(1, match.ShopMaxLevel))
        {
            return 0;
        }

        return Math.Max(0, match.ShopBaseUpgradeCost + ((safeLevel - 1) * match.ShopUpgradeCostPerLevel));
    }

    private static ulong ComposeShopOfferSeed(
        ulong matchId,
        int roundNumber,
        int rollCounter,
        Identity playerIdentity)
    {
        var seed = 0x53484F505F4F4646UL; // "SHOP_OFF"
        seed = DeterministicRng.Mix(seed, matchId);
        seed = DeterministicRng.Mix(seed, (ulong)Math.Max(1, roundNumber));
        seed = DeterministicRng.Mix(seed, (ulong)Math.Max(0, rollCounter));
        seed = DeterministicRng.Mix(seed, DeterministicRng.HashString(playerIdentity.ToString()));
        return seed;
    }

    private static void GenerateShopOffers(
        ReducerContext ctx,
        Match match,
        ulong roundId,
        int roundNumber,
        Identity playerIdentity,
        int offerRollCounter,
        int offersPerShop,
        List<MatchPlayerShopOffer>? carriedFrozenOffers = null)
    {
        ClearShopOffersForPlayerRound(ctx, match.MatchId, roundId, playerIdentity);

        if (carriedFrozenOffers != null && carriedFrozenOffers.Count > 0)
        {
            var frozen = new List<MatchPlayerShopOffer>(carriedFrozenOffers);
            frozen.Sort((a, b) => a.OfferIndex.CompareTo(b.OfferIndex));
            for (var i = 0; i < frozen.Count && i < offersPerShop; i++)
            {
                var offer = frozen[i];
                ctx.Db.MatchPlayerShopOffer.Insert(new MatchPlayerShopOffer
                {
                    MatchPlayerShopOfferId = 0,
                    MatchId = match.MatchId,
                    RoundId = roundId,
                    RoundNumber = roundNumber,
                    PlayerIdentity = playerIdentity,
                    OfferIndex = i,
                    CardStableId = offer.CardStableId,
                    CardId = offer.CardId,
                    PriceGold = offer.PriceGold,
                    CardSizeX = offer.CardSizeX,
                    CardSizeY = offer.CardSizeY,
                });
            }

            return;
        }

        var candidates = new List<SimCardDefinition>();
        foreach (var card in SimContentRegistry.AllCards)
        {
            candidates.Add(card);
        }

        candidates.Sort((a, b) => a.StableId.CompareTo(b.StableId));
        if (candidates.Count == 0)
        {
            return;
        }

        var count = Math.Min(Math.Max(1, offersPerShop), candidates.Count);
        var rng = new DeterministicRng(
            ComposeShopOfferSeed(match.MatchId, roundNumber, offerRollCounter, playerIdentity));

        for (var i = 0; i < count; i++)
        {
            var swapIndex = i + rng.NextInt(candidates.Count - i);
            (candidates[i], candidates[swapIndex]) = (candidates[swapIndex], candidates[i]);

            var picked = candidates[i];
            ctx.Db.MatchPlayerShopOffer.Insert(new MatchPlayerShopOffer
            {
                MatchPlayerShopOfferId = 0,
                MatchId = match.MatchId,
                RoundId = roundId,
                RoundNumber = roundNumber,
                PlayerIdentity = playerIdentity,
                OfferIndex = i,
                CardStableId = picked.StableId,
                CardId = picked.Id,
                PriceGold = picked.PriceGold,
                CardSizeX = picked.CardSizeX,
                CardSizeY = picked.CardSizeY,
            });
        }
    }

    private static void StartOrRefreshShopStateForPlayer(
        ReducerContext ctx,
        Match match,
        MatchRound round,
        Identity playerIdentity)
    {
        var latestState = FindLatestShopStateForPlayer(ctx, match.MatchId, playerIdentity);
        var carriedShopLevel = 1;
        var useFrozenOffers = false;
        var frozenOffers = new List<MatchPlayerShopOffer>();
        if (latestState is MatchPlayerShopState previousState)
        {
            carriedShopLevel = Math.Max(1, previousState.ShopLevel);
            if (previousState.IsFrozen)
            {
                useFrozenOffers = true;
                frozenOffers = GetShopOffersForPlayerRound(ctx, match.MatchId, previousState.RoundId, playerIdentity);
            }
        }

        var offersPerShop = Math.Max(1, match.ShopOffersPerRound);
        var roundGold = Math.Max(0, match.ShopGoldPerRound);
        if (FindShopState(ctx, match.MatchId, round.RoundId, playerIdentity) is MatchPlayerShopState existingState)
        {
            var updated = existingState;
            updated.RoundNumber = round.RoundNumber;
            updated.Gold = roundGold;
            updated.ShopLevel = carriedShopLevel;
            updated.IsFrozen = false;
            updated.OfferRollCounter = 0;
            updated.OffersPerShop = offersPerShop;
            updated.RequestedBattleStart = false;
            updated.UpdatedAt = ctx.Timestamp;
            ctx.Db.MatchPlayerShopState.MatchPlayerShopStateId.Update(updated);
        }
        else
        {
            ctx.Db.MatchPlayerShopState.Insert(new MatchPlayerShopState
            {
                MatchPlayerShopStateId = 0,
                MatchId = match.MatchId,
                RoundId = round.RoundId,
                RoundNumber = round.RoundNumber,
                PlayerIdentity = playerIdentity,
                Gold = roundGold,
                ShopLevel = carriedShopLevel,
                IsFrozen = false,
                OfferRollCounter = 0,
                OffersPerShop = offersPerShop,
                RequestedBattleStart = false,
                UpdatedAt = ctx.Timestamp,
            });
        }

        GenerateShopOffers(
            ctx,
            match,
            round.RoundId,
            round.RoundNumber,
            playerIdentity,
            offerRollCounter: 0,
            offersPerShop,
            useFrozenOffers ? frozenOffers : null);
    }

    private static void UpsertShopTimer(ReducerContext ctx, ulong matchId, ulong roundId, int roundNumber, int shopDurationSeconds)
    {
        var safeDurationSeconds = Math.Max(1, shopDurationSeconds);
        var timerRow = new ShopRoundTimer
        {
            MatchId = matchId,
            RoundId = roundId,
            RoundNumber = roundNumber,
            ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromSeconds(safeDurationSeconds)),
        };

        if (ctx.Db.ShopRoundTimer.MatchId.Find(matchId) is ShopRoundTimer existing)
        {
            _ = existing;
            ctx.Db.ShopRoundTimer.MatchId.Update(timerRow);
        }
        else
        {
            ctx.Db.ShopRoundTimer.Insert(timerRow);
        }
    }

    private static void DeleteShopTimer(ReducerContext ctx, ulong matchId)
    {
        if (ctx.Db.ShopRoundTimer.MatchId.Find(matchId) is ShopRoundTimer timer)
        {
            _ = timer;
            ctx.Db.ShopRoundTimer.MatchId.Delete(matchId);
        }
    }

    private static bool TryResolvePlayerTeam(ReducerContext ctx, ulong matchId, Identity playerIdentity, out byte team)
    {
        team = SimConstants.TeamNeutral;
        if (FindPlayer(ctx, matchId, playerIdentity) is not MatchPlayer matchPlayer)
        {
            return false;
        }

        if (matchPlayer.SeatIndex == 0)
        {
            team = SimConstants.TeamA;
            return true;
        }

        if (matchPlayer.SeatIndex == 1)
        {
            team = SimConstants.TeamB;
            return true;
        }

        return false;
    }

    private static byte ResolvePlayerTeamOrThrow(ReducerContext ctx, ulong matchId, Identity playerIdentity)
    {
        if (TryResolvePlayerTeam(ctx, matchId, playerIdentity, out var team))
        {
            return team;
        }

        throw new Exception("Player is not seated in this 1v1 match.");
    }

    private static int GetNextSlotIndexForPlayer(ReducerContext ctx, ulong matchId, Identity playerIdentity)
    {
        var nextSlot = 0;
        foreach (var card in ctx.Db.MatchPlayerCard.Iter())
        {
            if (card.MatchId != matchId || card.PlayerIdentity != playerIdentity)
            {
                continue;
            }

            nextSlot = Math.Max(nextSlot, card.SlotIndex + 1);
        }

        return nextSlot;
    }

    private static List<MatchPlayerCard> GetCardsOnGrid(
        ReducerContext ctx,
        ulong matchId,
        Identity playerIdentity,
        int gridIndex,
        ulong ignoredInstanceId = 0)
    {
        var cards = new List<MatchPlayerCard>();
        foreach (var card in ctx.Db.MatchPlayerCard.Iter())
        {
            if (card.MatchId != matchId ||
                card.PlayerIdentity != playerIdentity ||
                card.GridIndex != gridIndex)
            {
                continue;
            }

            if (ignoredInstanceId != 0 && card.MatchPlayerCardId == ignoredInstanceId)
            {
                continue;
            }

            cards.Add(card);
        }

        return cards;
    }

    private static bool CanPlaceCardAt(
        ReducerContext ctx,
        ulong matchId,
        Identity playerIdentity,
        int gridIndex,
        int cardSizeX,
        int cardSizeY,
        int targetCellX,
        int targetCellZ,
        ulong ignoredInstanceId = 0)
    {
        if (!BakedFlowField.TryGetCardPlacementGrid(gridIndex, out var grid))
        {
            return false;
        }

        var safeSizeX = Math.Max(1, cardSizeX);
        var safeSizeY = Math.Max(1, cardSizeY);
        if (targetCellX < 0 ||
            targetCellZ < 0 ||
            targetCellX + safeSizeX > grid.CellsX ||
            targetCellZ + safeSizeY > grid.CellsZ)
        {
            return false;
        }

        var occupied = new bool[grid.CellsX, grid.CellsZ];
        var cards = GetCardsOnGrid(ctx, matchId, playerIdentity, gridIndex, ignoredInstanceId);
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (!SimContentRegistry.TryGetCardByStableId(card.CardStableId, out var cardDefinition))
            {
                continue;
            }

            var existingSizeX = Math.Max(1, cardDefinition.CardSizeX);
            var existingSizeY = Math.Max(1, cardDefinition.CardSizeY);
            if (card.CellX < 0 ||
                card.CellZ < 0 ||
                card.CellX + existingSizeX > grid.CellsX ||
                card.CellZ + existingSizeY > grid.CellsZ)
            {
                continue;
            }

            for (var z = card.CellZ; z < card.CellZ + existingSizeY; z++)
            {
                for (var x = card.CellX; x < card.CellX + existingSizeX; x++)
                {
                    occupied[x, z] = true;
                }
            }
        }

        for (var z = targetCellZ; z < targetCellZ + safeSizeY; z++)
        {
            for (var x = targetCellX; x < targetCellX + safeSizeX; x++)
            {
                if (occupied[x, z])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryFindFirstPlacement(
        ReducerContext ctx,
        ulong matchId,
        Identity playerIdentity,
        int gridIndex,
        int cardSizeX,
        int cardSizeY,
        out int cellX,
        out int cellZ,
        ulong ignoredInstanceId = 0)
    {
        cellX = -1;
        cellZ = -1;
        if (!BakedFlowField.TryGetCardPlacementGrid(gridIndex, out var grid))
        {
            return false;
        }

        var safeSizeX = Math.Max(1, cardSizeX);
        var safeSizeY = Math.Max(1, cardSizeY);
        if (safeSizeX > grid.CellsX || safeSizeY > grid.CellsZ)
        {
            return false;
        }

        var maxX = grid.CellsX - safeSizeX;
        var maxZ = grid.CellsZ - safeSizeY;
        for (var z = 0; z <= maxZ; z++)
        {
            for (var x = 0; x <= maxX; x++)
            {
                if (!CanPlaceCardAt(
                        ctx,
                        matchId,
                        playerIdentity,
                        gridIndex,
                        safeSizeX,
                        safeSizeY,
                        x,
                        z,
                        ignoredInstanceId))
                {
                    continue;
                }

                cellX = x;
                cellZ = z;
                return true;
            }
        }

        return false;
    }

    private static List<BakedCardPlacementGrid> GetTeamPlacementGrids(byte team)
    {
        var grids = new List<BakedCardPlacementGrid>();
        for (var i = 0; i < BakedFlowField.CardPlacementGridCount; i++)
        {
            if (!BakedFlowField.TryGetCardPlacementGrid(i, out var grid))
            {
                continue;
            }

            if (grid.Team == team)
            {
                grids.Add(grid);
            }
        }

        grids.Sort((a, b) =>
        {
            var centerZCompare = a.CenterZ.Raw.CompareTo(b.CenterZ.Raw);
            if (centerZCompare != 0)
            {
                return centerZCompare;
            }

            var centerXCompare = a.CenterX.Raw.CompareTo(b.CenterX.Raw);
            if (centerXCompare != 0)
            {
                return centerXCompare;
            }

            return a.GridIndex.CompareTo(b.GridIndex);
        });
        return grids;
    }

    private static int ResolveDefaultGridIndexForTeamOrThrow(byte team)
    {
        var grids = GetTeamPlacementGrids(team);
        if (grids.Count == 0)
        {
            throw new Exception("No placement grids are available for this team.");
        }

        return grids[0].GridIndex;
    }

    private static void ValidateGridPlacementOrThrow(
        byte expectedTeam,
        int gridIndex,
        int cellX,
        int cellZ,
        int cardSizeX,
        int cardSizeY)
    {
        if (!BakedFlowField.TryGetCardPlacementGrid(gridIndex, out var grid))
        {
            throw new Exception($"Placement grid '{gridIndex}' does not exist.");
        }

        if (grid.Team != expectedTeam)
        {
            throw new Exception("Card was placed on a grid that belongs to the other team.");
        }

        if (!BakedFlowField.TryResolveCardWorldCenter(
                gridIndex,
                cellX,
                cellZ,
                cardSizeX,
                cardSizeY,
                out _))
        {
            throw new Exception("Card placement is outside the authored card grid bounds.");
        }
    }
}
