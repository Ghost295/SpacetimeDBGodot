using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CreateMatch(
        ReducerContext ctx,
        uint tickRateTps,
        uint snapshotEveryNTicks,
        int maxBattleTicks,
        int unitsPerPlayer,
        int startingHealth)
    {
        if (tickRateTps == 0)
        {
            throw new Exception("tickRateTps must be > 0");
        }

        if (snapshotEveryNTicks == 0)
        {
            throw new Exception("snapshotEveryNTicks must be > 0");
        }

        if (maxBattleTicks <= 0)
        {
            throw new Exception("maxBattleTicks must be > 0");
        }

        if (unitsPerPlayer <= 0)
        {
            throw new Exception("unitsPerPlayer must be > 0");
        }

        if (startingHealth <= 0)
        {
            throw new Exception("startingHealth must be > 0");
        }

        ctx.Db.Match.Insert(new Match
        {
            MatchId = 0,
            CreatedBy = ctx.Sender,
            Status = SimConstants.MatchLobby,
            CurrentRound = 0,
            TickRateTps = tickRateTps,
            SnapshotEveryNTicks = snapshotEveryNTicks,
            SnapshotRetention = 180,
            MaxBattleTicks = maxBattleTicks,
            UnitsPerPlayer = unitsPerPlayer,
            StartingHealth = startingHealth,
            RoundDamage = 1,
            ShopBaseDurationSeconds = 25,
            ShopDurationIncreaseSeconds = 5,
            ShopGoldPerRound = 25,
            ShopRerollCost = 1,
            ShopOffersPerRound = 3,
            ShopMaxLevel = 6,
            ShopBaseUpgradeCost = 5,
            ShopUpgradeCostPerLevel = 2,
            StaticContentHash = SimContentRegistry.StaticContentHash,
            MapFlowFieldHash = BakedFlowField.BakeHashBase64,
            CreatedAt = ctx.Timestamp,
            UpdatedAt = ctx.Timestamp,
            HasWinner = false,
            Winner = default,
        });
    }

    [SpacetimeDB.Reducer]
    public static void JoinMatch(ReducerContext ctx, ulong matchId)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Match is already completed.");
        }

        if (match.Status == SimConstants.MatchBattle)
        {
            throw new Exception("Cannot join while battle phase is active.");
        }

        if (FindPlayer(ctx, matchId, ctx.Sender) is MatchPlayer existing)
        {
            _ = existing;
            throw new Exception("Player already joined this match.");
        }

        var players = GetPlayersForMatch(ctx, matchId);
        if (players.Count >= 2)
        {
            throw new Exception("This match already has two players.");
        }

        var seat0Used = false;
        var seat1Used = false;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].SeatIndex == 0)
            {
                seat0Used = true;
            }
            else if (players[i].SeatIndex == 1)
            {
                seat1Used = true;
            }
        }

        var nextSeat = !seat0Used ? 0 : (!seat1Used ? 1 : -1);
        if (nextSeat < 0)
        {
            throw new Exception("No available 1v1 seat was found.");
        }

        ctx.Db.MatchPlayer.Insert(new MatchPlayer
        {
            MatchPlayerId = 0,
            MatchId = matchId,
            PlayerIdentity = ctx.Sender,
            SeatIndex = nextSeat,
            Health = match.StartingHealth,
            Eliminated = false,
            JoinedAt = ctx.Timestamp,
        });

        SeedDefaultLoadoutForPlayer(ctx, matchId, ctx.Sender);

        var refreshedMatch = GetMatchOrThrow(ctx, matchId);
        var activePlayers = GetActivePlayersForMatch(matchId, ctx);
        if (activePlayers.Count == 2 &&
            refreshedMatch.Status != SimConstants.MatchBattle &&
            refreshedMatch.Status != SimConstants.MatchCompleted)
        {
            StartShopRoundInternal(ctx, refreshedMatch, Math.Max(1, refreshedMatch.CurrentRound + 1));
        }
    }

    [SpacetimeDB.Reducer]
    public static void LeaveMatch(ReducerContext ctx, ulong matchId)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (FindPlayer(ctx, matchId, ctx.Sender) is not MatchPlayer player)
        {
            throw new Exception("Player is not part of this match.");
        }

        ctx.Db.MatchPlayer.MatchPlayerId.Delete(player.MatchPlayerId);

        var cards = GetPlayerCards(ctx, matchId, ctx.Sender);
        for (var i = 0; i < cards.Count; i++)
        {
            ctx.Db.MatchPlayerCard.MatchPlayerCardId.Delete(cards[i].MatchPlayerCardId);
        }

        if (ctx.Db.PlayerBattleView.PlayerIdentity.Find(ctx.Sender) is PlayerBattleView viewRow &&
            viewRow.MatchId == matchId)
        {
            ctx.Db.PlayerBattleView.PlayerIdentity.Delete(ctx.Sender);
        }

        RemoveShopRowsForPlayer(ctx, matchId, ctx.Sender);

        var remainingActivePlayers = GetActivePlayersForMatch(matchId, ctx);
        if (remainingActivePlayers.Count <= 1)
        {
            var updatedMatch = match;
            updatedMatch.Status = SimConstants.MatchCompleted;
            updatedMatch.UpdatedAt = ctx.Timestamp;
            updatedMatch.HasWinner = remainingActivePlayers.Count == 1;
            updatedMatch.Winner = remainingActivePlayers.Count == 1
                ? remainingActivePlayers[0].PlayerIdentity
                : default;
            ctx.Db.Match.MatchId.Update(updatedMatch);
            DeleteShopTimer(ctx, matchId);
        }
    }

    [SpacetimeDB.Reducer]
    public static void StartRound(ReducerContext ctx, ulong matchId)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Cannot start a round on a completed match.");
        }

        if (match.Status == SimConstants.MatchLobby)
        {
            var activePlayers = GetActivePlayersForMatch(matchId, ctx);
            if (activePlayers.Count < 2)
            {
                throw new Exception("At least two active players are required to start a round.");
            }

            StartShopRoundInternal(ctx, match, Math.Max(1, match.CurrentRound + 1));
        }

        RequestBattleStart(ctx, matchId);
    }

    [SpacetimeDB.Reducer]
    public static void EndRound(ReducerContext ctx, ulong matchId)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.CurrentRound <= 0)
        {
            throw new Exception("No active round exists for this match.");
        }

        if (FindCurrentRound(ctx, matchId, match.CurrentRound) is not MatchRound round)
        {
            throw new Exception("Current round row is missing.");
        }

        _ = TryFinalizeRoundIfComplete(ctx, matchId, round.RoundId, throwIfIncomplete: true);
    }

    [SpacetimeDB.Reducer]
    public static void TickShopRoundTimer(ReducerContext ctx, ShopRoundTimer timer)
    {
        if (ctx.Db.Match.MatchId.Find(timer.MatchId) is not Match match)
        {
            DeleteShopTimer(ctx, timer.MatchId);
            return;
        }

        if (ctx.Db.MatchRound.RoundId.Find(timer.RoundId) is not MatchRound round)
        {
            DeleteShopTimer(ctx, timer.MatchId);
            return;
        }

        _ = StartBattleRoundInternal(ctx, match, round, skipIfNotShop: true);
    }

    private static MatchRound StartShopRoundInternal(ReducerContext ctx, Match match, int roundNumber)
    {
        var activePlayers = GetActivePlayersForMatch(match.MatchId, ctx);
        if (activePlayers.Count < 2)
        {
            throw new Exception("At least two active players are required to start shop.");
        }

        if (match.Status == SimConstants.MatchShop &&
            match.CurrentRound == roundNumber &&
            FindCurrentRound(ctx, match.MatchId, roundNumber) is MatchRound existingRound &&
            existingRound.Status == SimConstants.RoundPending)
        {
            return existingRound;
        }

        var round = ctx.Db.MatchRound.Insert(new MatchRound
        {
            RoundId = 0,
            MatchId = match.MatchId,
            RoundNumber = Math.Max(1, roundNumber),
            Status = SimConstants.RoundPending,
            BattleCount = 0,
            StartedAt = ctx.Timestamp,
            EndedAt = default,
            HasEndedAt = false,
        });

        var updatedMatch = match;
        updatedMatch.Status = SimConstants.MatchShop;
        updatedMatch.CurrentRound = round.RoundNumber;
        updatedMatch.UpdatedAt = ctx.Timestamp;
        ctx.Db.Match.MatchId.Update(updatedMatch);

        for (var i = 0; i < activePlayers.Count; i++)
        {
            StartOrRefreshShopStateForPlayer(ctx, updatedMatch, round, activePlayers[i].PlayerIdentity);
        }

        UpsertShopTimer(ctx, match.MatchId, round.RoundId, round.RoundNumber, ComputeShopDurationSeconds(updatedMatch, round.RoundNumber));
        return round;
    }

    private static bool StartBattleRoundInternal(ReducerContext ctx, Match match, MatchRound round, bool skipIfNotShop)
    {
        if (skipIfNotShop &&
            (match.Status != SimConstants.MatchShop || round.Status != SimConstants.RoundPending))
        {
            return false;
        }

        if (round.Status == SimConstants.RoundBattle)
        {
            return true;
        }

        var activePlayers = GetActivePlayersForMatch(match.MatchId, ctx);
        if (activePlayers.Count < 2)
        {
            var completedRound = round;
            completedRound.Status = SimConstants.RoundCompleted;
            completedRound.EndedAt = ctx.Timestamp;
            completedRound.HasEndedAt = true;
            ctx.Db.MatchRound.RoundId.Update(completedRound);

            var completedMatch = match;
            completedMatch.Status = SimConstants.MatchCompleted;
            completedMatch.UpdatedAt = ctx.Timestamp;
            completedMatch.HasWinner = activePlayers.Count == 1;
            completedMatch.Winner = activePlayers.Count == 1 ? activePlayers[0].PlayerIdentity : default;
            ctx.Db.Match.MatchId.Update(completedMatch);
            DeleteShopTimer(ctx, match.MatchId);
            return false;
        }

        var playerA = activePlayers[0];
        var playerB = activePlayers[1];
        _ = CreateBattleInternal(
            ctx,
            match.MatchId,
            round.RoundId,
            playerA.PlayerIdentity,
            playerB.PlayerIdentity,
            match.TickRateTps,
            match.SnapshotEveryNTicks,
            match.MaxBattleTicks,
            match.UnitsPerPlayer,
            ((ulong)round.RoundNumber << 32) ^ round.RoundId ^ match.MatchId);

        var updatedRound = round;
        updatedRound.Status = SimConstants.RoundBattle;
        updatedRound.BattleCount = 1;
        ctx.Db.MatchRound.RoundId.Update(updatedRound);

        var updatedMatch = match;
        updatedMatch.Status = SimConstants.MatchBattle;
        updatedMatch.UpdatedAt = ctx.Timestamp;
        ctx.Db.Match.MatchId.Update(updatedMatch);
        DeleteShopTimer(ctx, match.MatchId);
        return true;
    }

    private static bool TryFinalizeRoundIfComplete(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        bool throwIfIncomplete)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (ctx.Db.MatchRound.RoundId.Find(roundId) is not MatchRound round)
        {
            if (throwIfIncomplete)
            {
                throw new Exception("Round row is missing.");
            }

            return false;
        }

        if (round.Status != SimConstants.RoundBattle)
        {
            if (throwIfIncomplete)
            {
                throw new Exception("Round is not in battle phase.");
            }

            return false;
        }

        var battles = GetBattlesForRound(ctx, matchId, round.RoundId);
        for (var i = 0; i < battles.Count; i++)
        {
            if (battles[i].Status != SimConstants.BattleCompleted)
            {
                if (throwIfIncomplete)
                {
                    throw new Exception("Cannot end round while battles are still active.");
                }

                return false;
            }
        }

        for (var i = 0; i < battles.Count; i++)
        {
            var battle = battles[i];
            if (battle.WinnerTeam == SimConstants.TeamA)
            {
                ApplyRoundDamage(ctx, matchId, battle.PlayerB, Math.Max(1, match.RoundDamage));
            }
            else if (battle.WinnerTeam == SimConstants.TeamB)
            {
                ApplyRoundDamage(ctx, matchId, battle.PlayerA, Math.Max(1, match.RoundDamage));
            }
        }

        var allPlayers = GetPlayersForMatch(ctx, matchId);
        var activeCount = 0;
        Identity remainingIdentity = default;
        for (var i = 0; i < allPlayers.Count; i++)
        {
            var player = allPlayers[i];
            if (!player.Eliminated && player.Health > 0)
            {
                activeCount++;
                remainingIdentity = player.PlayerIdentity;
            }
        }

        var updatedRound = round;
        updatedRound.Status = SimConstants.RoundCompleted;
        updatedRound.EndedAt = ctx.Timestamp;
        updatedRound.HasEndedAt = true;
        ctx.Db.MatchRound.RoundId.Update(updatedRound);

        for (var i = 0; i < battles.Count; i++)
        {
            SetPlayerBattleView(ctx, battles[i].PlayerA, battles[i].PlayerB, battles[i], false);
            SetPlayerBattleView(ctx, battles[i].PlayerB, battles[i].PlayerA, battles[i], false);
        }

        if (activeCount <= 1)
        {
            var completedMatch = match;
            completedMatch.Status = SimConstants.MatchCompleted;
            completedMatch.UpdatedAt = ctx.Timestamp;
            completedMatch.HasWinner = activeCount == 1;
            completedMatch.Winner = activeCount == 1 ? remainingIdentity : default;
            ctx.Db.Match.MatchId.Update(completedMatch);
            DeleteShopTimer(ctx, matchId);
            return true;
        }

        _ = StartShopRoundInternal(ctx, match, round.RoundNumber + 1);

        return true;
    }

    private static void ApplyRoundDamage(ReducerContext ctx, ulong matchId, Identity playerIdentity, int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        if (FindPlayer(ctx, matchId, playerIdentity) is not MatchPlayer player)
        {
            return;
        }

        var updated = player;
        updated.Health = Math.Max(0, player.Health - damage);
        updated.Eliminated = updated.Health <= 0;
        ctx.Db.MatchPlayer.MatchPlayerId.Update(updated);
    }

    private static void RemoveShopRowsForPlayer(ReducerContext ctx, ulong matchId, Identity playerIdentity)
    {
        var statesToDelete = new List<ulong>();
        foreach (var state in ctx.Db.MatchPlayerShopState.Iter())
        {
            if (state.MatchId == matchId && state.PlayerIdentity == playerIdentity)
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
            if (offer.MatchId == matchId && offer.PlayerIdentity == playerIdentity)
            {
                offersToDelete.Add(offer.MatchPlayerShopOfferId);
            }
        }

        for (var i = 0; i < offersToDelete.Count; i++)
        {
            ctx.Db.MatchPlayerShopOffer.MatchPlayerShopOfferId.Delete(offersToDelete[i]);
        }
    }
}
