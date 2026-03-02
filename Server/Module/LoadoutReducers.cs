using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void SetMatchPlayerCard(
        ReducerContext ctx,
        ulong matchId,
        int slotIndex,
        int cardStableId,
        int quantity,
        int level)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchBattle)
        {
            throw new Exception("Cannot edit loadout during battle phase.");
        }

        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Cannot edit loadout for completed matches.");
        }

        if (slotIndex < 0)
        {
            throw new Exception("slotIndex must be >= 0.");
        }

        if (quantity <= 0)
        {
            throw new Exception("quantity must be > 0.");
        }

        if (level <= 0)
        {
            throw new Exception("level must be > 0.");
        }

        var team = ResolvePlayerTeamOrThrow(ctx, matchId, ctx.Sender);
        var card = SimContentRegistry.GetCardByStableId(cardStableId);
        EnsurePlayerInMatchOrThrow(ctx, matchId, ctx.Sender);
        var existingSlot = FindPlayerCardSlot(ctx, matchId, ctx.Sender, slotIndex);
        var targetGrid = ResolveDefaultGridIndexForTeamOrThrow(team);
        if (!TryFindFirstPlacement(
                ctx,
                matchId,
                ctx.Sender,
                targetGrid,
                card.CardSizeX,
                card.CardSizeY,
                out var targetCellX,
                out var targetCellZ,
                existingSlot?.MatchPlayerCardId ?? 0))
        {
            throw new Exception("No legal placement cell is available for this card.");
        }

        UpsertPlayerCardSlot(
            ctx,
            matchId,
            ctx.Sender,
            slotIndex,
            targetGrid,
            targetCellX,
            targetCellZ,
            cardStableId,
            quantity,
            level,
            new List<CardRuntimeModifierState>());
    }

    [SpacetimeDB.Reducer]
    public static void SetMatchPlayerCardWithModifiers(
        ReducerContext ctx,
        ulong matchId,
        int slotIndex,
        int cardStableId,
        int quantity,
        int level,
        List<CardRuntimeModifierState> runtimeModifiers)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchBattle)
        {
            throw new Exception("Cannot edit loadout during battle phase.");
        }

        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Cannot edit loadout for completed matches.");
        }

        if (slotIndex < 0)
        {
            throw new Exception("slotIndex must be >= 0.");
        }

        if (quantity <= 0)
        {
            throw new Exception("quantity must be > 0.");
        }

        if (level <= 0)
        {
            throw new Exception("level must be > 0.");
        }

        var team = ResolvePlayerTeamOrThrow(ctx, matchId, ctx.Sender);
        var card = SimContentRegistry.GetCardByStableId(cardStableId);
        ValidateRuntimeModifiersOrThrow(card, runtimeModifiers);
        EnsurePlayerInMatchOrThrow(ctx, matchId, ctx.Sender);
        var existingSlot = FindPlayerCardSlot(ctx, matchId, ctx.Sender, slotIndex);
        var targetGrid = ResolveDefaultGridIndexForTeamOrThrow(team);
        if (!TryFindFirstPlacement(
                ctx,
                matchId,
                ctx.Sender,
                targetGrid,
                card.CardSizeX,
                card.CardSizeY,
                out var targetCellX,
                out var targetCellZ,
                existingSlot?.MatchPlayerCardId ?? 0))
        {
            throw new Exception("No legal placement cell is available for this card.");
        }

        UpsertPlayerCardSlot(
            ctx,
            matchId,
            ctx.Sender,
            slotIndex,
            targetGrid,
            targetCellX,
            targetCellZ,
            cardStableId,
            quantity,
            level,
            runtimeModifiers ?? new List<CardRuntimeModifierState>());
    }

    [SpacetimeDB.Reducer]
    public static void ClearMatchPlayerCard(ReducerContext ctx, ulong matchId, int slotIndex)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchBattle)
        {
            throw new Exception("Cannot edit loadout during battle phase.");
        }

        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Cannot edit loadout for completed matches.");
        }

        if (slotIndex < 0)
        {
            throw new Exception("slotIndex must be >= 0.");
        }

        EnsurePlayerInMatchOrThrow(ctx, matchId, ctx.Sender);
        if (FindPlayerCardSlot(ctx, matchId, ctx.Sender, slotIndex) is MatchPlayerCard slot)
        {
            ctx.Db.MatchPlayerCard.MatchPlayerCardId.Delete(slot.MatchPlayerCardId);
        }
    }

    private static void EnsurePlayerInMatchOrThrow(ReducerContext ctx, ulong matchId, Identity playerIdentity)
    {
        if (FindPlayer(ctx, matchId, playerIdentity) is not MatchPlayer)
        {
            throw new Exception("Player is not part of this match.");
        }
    }

    private static MatchPlayerCard? FindPlayerCardSlot(ReducerContext ctx, ulong matchId, Identity playerIdentity, int slotIndex)
    {
        foreach (var row in ctx.Db.MatchPlayerCard.Iter())
        {
            if (row.MatchId == matchId &&
                row.PlayerIdentity == playerIdentity &&
                row.SlotIndex == slotIndex)
            {
                return row;
            }
        }

        return null;
    }

    private static List<MatchPlayerCard> GetPlayerCards(ReducerContext ctx, ulong matchId, Identity playerIdentity)
    {
        var rows = new List<MatchPlayerCard>();
        foreach (var row in ctx.Db.MatchPlayerCard.Iter())
        {
            if (row.MatchId == matchId && row.PlayerIdentity == playerIdentity)
            {
                rows.Add(row);
            }
        }

        rows.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
        return rows;
    }

    private static void UpsertPlayerCardSlot(
        ReducerContext ctx,
        ulong matchId,
        Identity playerIdentity,
        int slotIndex,
        int gridIndex,
        int cellX,
        int cellZ,
        int cardStableId,
        int quantity,
        int level,
        List<CardRuntimeModifierState> runtimeModifiers)
    {
        var card = SimContentRegistry.GetCardByStableId(cardStableId);
        var team = ResolvePlayerTeamOrThrow(ctx, matchId, playerIdentity);
        ValidateGridPlacementOrThrow(team, gridIndex, cellX, cellZ, card.CardSizeX, card.CardSizeY);

        var normalizedQuantity = Math.Max(1, quantity);
        var normalizedLevel = Math.Max(1, level);
        var normalizedModifiers = NormalizeRuntimeModifiers(runtimeModifiers);
        if (FindPlayerCardSlot(ctx, matchId, playerIdentity, slotIndex) is MatchPlayerCard existing)
        {
            if (!CanPlaceCardAt(
                    ctx,
                    matchId,
                    playerIdentity,
                    gridIndex,
                    card.CardSizeX,
                    card.CardSizeY,
                    cellX,
                    cellZ,
                    existing.MatchPlayerCardId))
            {
                throw new Exception("Card placement collides with another placed card.");
            }

            var updated = existing;
            updated.GridIndex = gridIndex;
            updated.CellX = cellX;
            updated.CellZ = cellZ;
            updated.CardStableId = cardStableId;
            updated.Quantity = normalizedQuantity;
            updated.Level = normalizedLevel;
            updated.RuntimeModifiers = normalizedModifiers;
            ctx.Db.MatchPlayerCard.MatchPlayerCardId.Update(updated);
            return;
        }

        if (!CanPlaceCardAt(
                ctx,
                matchId,
                playerIdentity,
                gridIndex,
                card.CardSizeX,
                card.CardSizeY,
                cellX,
                cellZ))
        {
            throw new Exception("Card placement collides with another placed card.");
        }

        ctx.Db.MatchPlayerCard.Insert(new MatchPlayerCard
        {
            MatchPlayerCardId = 0,
            MatchId = matchId,
            PlayerIdentity = playerIdentity,
            SlotIndex = slotIndex,
            GridIndex = gridIndex,
            CellX = cellX,
            CellZ = cellZ,
            CardStableId = cardStableId,
            Quantity = normalizedQuantity,
            Level = normalizedLevel,
            RuntimeModifiers = normalizedModifiers,
        });
    }

    private static void ValidateRuntimeModifiersOrThrow(
        SimCardDefinition card,
        List<CardRuntimeModifierState> runtimeModifiers)
    {
        if (runtimeModifiers == null || runtimeModifiers.Count == 0)
        {
            return;
        }

        for (var i = 0; i < runtimeModifiers.Count; i++)
        {
            var runtimeModifier = runtimeModifiers[i];
            var found = false;
            for (var j = 0; j < card.Modifiers.Length; j++)
            {
                if (card.Modifiers[j].ModifierStableId == runtimeModifier.ModifierStableId)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new Exception(
                    $"Runtime modifier stable id '{runtimeModifier.ModifierStableId}' is not valid for card '{card.StableId}'.");
            }
        }
    }

    private static List<CardRuntimeModifierState> NormalizeRuntimeModifiers(List<CardRuntimeModifierState> runtimeModifiers)
    {
        var normalized = runtimeModifiers == null
            ? new List<CardRuntimeModifierState>()
            : new List<CardRuntimeModifierState>(runtimeModifiers);
        normalized.Sort((a, b) =>
        {
            var stableCompare = a.ModifierStableId.CompareTo(b.ModifierStableId);
            return stableCompare != 0
                ? stableCompare
                : a.ExtraAttackDamage.Raw.CompareTo(b.ExtraAttackDamage.Raw);
        });
        return normalized;
    }
}
