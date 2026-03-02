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

        ctx.Db.match.Insert(new Match
        {
            MatchId = 0,
            CreatedBy = ctx.Sender,
            Status = SimConstants.MatchLobby,
            CurrentRound = 0,
            TickRateTps = tickRateTps,
            SnapshotEveryNTicks = snapshotEveryNTicks,
            MaxBattleTicks = maxBattleTicks,
            UnitsPerPlayer = unitsPerPlayer,
            StartingHealth = startingHealth,
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
        var nextSeat = 0;
        for (var i = 0; i < players.Count; i++)
        {
            nextSeat = Math.Max(nextSeat, players[i].SeatIndex + 1);
        }

        ctx.Db.match_player.Insert(new MatchPlayer
        {
            MatchPlayerId = 0,
            MatchId = matchId,
            PlayerIdentity = ctx.Sender,
            SeatIndex = nextSeat,
            Health = match.StartingHealth,
            Eliminated = false,
            JoinedAt = ctx.Timestamp,
        });
    }

    [SpacetimeDB.Reducer]
    public static void LeaveMatch(ReducerContext ctx, ulong matchId)
    {
        _ = GetMatchOrThrow(ctx, matchId);
        if (FindPlayer(ctx, matchId, ctx.Sender) is not MatchPlayer player)
        {
            throw new Exception("Player is not part of this match.");
        }

        ctx.Db.match_player.MatchPlayerId.Delete(player.MatchPlayerId);
    }

    [SpacetimeDB.Reducer]
    public static void StartRound(ReducerContext ctx, ulong matchId)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        if (match.Status == SimConstants.MatchBattle)
        {
            throw new Exception("Match already has an active round.");
        }

        if (match.Status == SimConstants.MatchCompleted)
        {
            throw new Exception("Cannot start a round on a completed match.");
        }

        var players = GetPlayersForMatch(ctx, matchId);
        var activePlayers = new List<MatchPlayer>();
        for (var i = 0; i < players.Count; i++)
        {
            if (!players[i].Eliminated && players[i].Health > 0)
            {
                activePlayers.Add(players[i]);
            }
        }

        if (activePlayers.Count < 2)
        {
            throw new Exception("At least two active players are required to start a round.");
        }

        activePlayers.Sort((a, b) => a.SeatIndex.CompareTo(b.SeatIndex));
        var round = ctx.Db.match_round.Insert(new MatchRound
        {
            RoundId = 0,
            MatchId = matchId,
            RoundNumber = match.CurrentRound + 1,
            Status = SimConstants.RoundBattle,
            BattleCount = 0,
            StartedAt = ctx.Timestamp,
            EndedAt = default,
            HasEndedAt = false,
        });

        var battleCount = 0;
        for (var i = 0; i + 1 < activePlayers.Count; i += 2)
        {
            var playerA = activePlayers[i];
            var playerB = activePlayers[i + 1];
            CreateBattleInternal(
                ctx,
                matchId,
                round.RoundId,
                playerA.PlayerIdentity,
                playerB.PlayerIdentity,
                match.TickRateTps,
                match.SnapshotEveryNTicks,
                match.MaxBattleTicks,
                match.UnitsPerPlayer,
                ((ulong)round.RoundNumber << 32) ^ (ulong)i ^ matchId);
            battleCount++;
        }

        var updatedRound = round;
        updatedRound.BattleCount = battleCount;
        ctx.Db.match_round.RoundId.Update(updatedRound);

        var updatedMatch = match;
        updatedMatch.Status = battleCount > 0 ? SimConstants.MatchBattle : SimConstants.MatchShop;
        updatedMatch.CurrentRound = round.RoundNumber;
        updatedMatch.UpdatedAt = ctx.Timestamp;
        ctx.Db.match.MatchId.Update(updatedMatch);
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

        if (round.Status != SimConstants.RoundBattle)
        {
            throw new Exception("Round is not in battle phase.");
        }

        var battles = GetBattlesForRound(ctx, matchId, round.RoundId);
        for (var i = 0; i < battles.Count; i++)
        {
            if (battles[i].Status != SimConstants.BattleCompleted)
            {
                throw new Exception("Cannot end round while battles are still active.");
            }
        }

        for (var i = 0; i < battles.Count; i++)
        {
            var battle = battles[i];
            if (battle.WinnerTeam == SimConstants.TeamA)
            {
                ApplyRoundDamage(ctx, matchId, battle.PlayerB, 1);
            }
            else if (battle.WinnerTeam == SimConstants.TeamB)
            {
                ApplyRoundDamage(ctx, matchId, battle.PlayerA, 1);
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
        ctx.Db.match_round.RoundId.Update(updatedRound);

        var updatedMatch = match;
        updatedMatch.Status = activeCount <= 1 ? SimConstants.MatchCompleted : SimConstants.MatchShop;
        updatedMatch.UpdatedAt = ctx.Timestamp;
        updatedMatch.HasWinner = activeCount == 1;
        updatedMatch.Winner = activeCount == 1 ? remainingIdentity : default;
        ctx.Db.match.MatchId.Update(updatedMatch);
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
        ctx.Db.match_player.MatchPlayerId.Update(updated);
    }
}
