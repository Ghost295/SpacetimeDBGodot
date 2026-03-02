using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    private static readonly Fix64 DefaultWorldMinX = Fix64.FromInt(0);
    private static readonly Fix64 DefaultWorldMaxX = Fix64.FromInt(100);
    private static readonly Fix64 DefaultWorldMinY = Fix64.FromInt(0);
    private static readonly Fix64 DefaultWorldMaxY = Fix64.FromInt(100);

    private static readonly Fix64 SpawnPadding = Fix64.FromInt(8);
    private static readonly Fix64 SpawnSpacing = Fix64.FromRatio(3, 2);

    private static Match GetMatchOrThrow(ReducerContext ctx, ulong matchId)
    {
        if (ctx.Db.match.MatchId.Find(matchId) is Match match)
        {
            return match;
        }

        throw new Exception($"Match '{matchId}' was not found.");
    }

    private static Battle GetBattleOrThrow(ReducerContext ctx, ulong battleId)
    {
        if (ctx.Db.battle.BattleId.Find(battleId) is Battle battle)
        {
            return battle;
        }

        throw new Exception($"Battle '{battleId}' was not found.");
    }

    private static MatchRound? FindCurrentRound(ReducerContext ctx, ulong matchId, int roundNumber)
    {
        MatchRound? found = null;
        foreach (var round in ctx.Db.match_round.Iter())
        {
            if (round.MatchId == matchId && round.RoundNumber == roundNumber)
            {
                found = round;
                break;
            }
        }

        return found;
    }

    private static List<MatchPlayer> GetPlayersForMatch(ReducerContext ctx, ulong matchId)
    {
        var players = new List<MatchPlayer>();
        foreach (var player in ctx.Db.match_player.Iter())
        {
            if (player.MatchId == matchId)
            {
                players.Add(player);
            }
        }

        return players;
    }

    private static MatchPlayer? FindPlayer(ReducerContext ctx, ulong matchId, Identity identity)
    {
        foreach (var player in ctx.Db.match_player.Iter())
        {
            if (player.MatchId == matchId && player.PlayerIdentity == identity)
            {
                return player;
            }
        }

        return null;
    }

    private static List<Battle> GetBattlesForRound(ReducerContext ctx, ulong matchId, ulong roundId)
    {
        var battles = new List<Battle>();
        foreach (var battle in ctx.Db.battle.Iter())
        {
            if (battle.MatchId == matchId && battle.RoundId == roundId)
            {
                battles.Add(battle);
            }
        }

        return battles;
    }

    private static ulong CreateBattleInternal(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerA,
        Identity playerB,
        uint tickRateTps,
        uint snapshotEveryNTicks,
        int maxTicks,
        int unitsPerPlayer,
        ulong rngSeed)
    {
        var battle = ctx.Db.battle.Insert(new Battle
        {
            BattleId = 0,
            MatchId = matchId,
            RoundId = roundId,
            PlayerA = playerA,
            PlayerB = playerB,
            Status = SimConstants.BattleRunning,
            TickRateTps = Math.Max(1u, tickRateTps),
            SnapshotEveryNTicks = Math.Max(1u, snapshotEveryNTicks),
            MaxTicks = Math.Max(1, maxTicks),
            CurrentTick = 0,
            UnitCount = 0,
            WorldMinX = DefaultWorldMinX,
            WorldMaxX = DefaultWorldMaxX,
            WorldMinY = DefaultWorldMinY,
            WorldMaxY = DefaultWorldMaxY,
            WinnerTeam = SimConstants.TeamNeutral,
            HasWinnerPlayer = false,
            WinnerPlayer = default,
            LastDigest = 0,
            CreatedAt = ctx.Timestamp,
            CompletedAt = default,
            HasCompletedAt = false,
        });

        var initialState = BuildInitialBattleState(
            unitsPerPlayer,
            battle.WorldMinX,
            battle.WorldMaxX,
            battle.WorldMinY,
            battle.WorldMaxY,
            rngSeed);

        ctx.Db.battle_state.Insert(new BattleState
        {
            BattleId = battle.BattleId,
            State = initialState,
        });

        var updateBattle = battle;
        updateBattle.UnitCount = initialState.UnitCount;
        ctx.Db.battle.BattleId.Update(updateBattle);
        UpsertBattleTimer(ctx, battle.BattleId, battle.TickRateTps);
        InsertBattleSnapshot(ctx, updateBattle, initialState, BattleDigest.Compute(BattleStateRuntime.FromBlob(initialState)));
        return battle.BattleId;
    }

    private static void UpsertBattleTimer(ReducerContext ctx, ulong battleId, uint tickRateTps)
    {
        var intervalMs = 1000.0 / Math.Max(1u, tickRateTps);
        var timerRow = new BattleTickTimer
        {
            BattleId = battleId,
            ScheduledAt = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(intervalMs)),
        };

        if (ctx.Db.battle_tick_timer.BattleId.Find(battleId) is BattleTickTimer existing)
        {
            _ = existing;
            ctx.Db.battle_tick_timer.BattleId.Update(timerRow);
        }
        else
        {
            ctx.Db.battle_tick_timer.Insert(timerRow);
        }
    }

    private static void DeleteBattleTimer(ReducerContext ctx, ulong battleId)
    {
        if (ctx.Db.battle_tick_timer.BattleId.Find(battleId) is BattleTickTimer timer)
        {
            _ = timer;
            ctx.Db.battle_tick_timer.BattleId.Delete(battleId);
        }
    }

    private static void InsertBattleSnapshot(ReducerContext ctx, Battle battle, BattleStateBlob state, ulong digest)
    {
        ctx.Db.battle_snapshot.Insert(new BattleSnapshot
        {
            SnapshotId = 0,
            BattleId = battle.BattleId,
            MatchId = battle.MatchId,
            RoundId = battle.RoundId,
            Tick = state.Tick,
            Digest = digest,
            Snapshot = BattleSnapshotBlob.FromState(state, digest),
            CreatedAt = ctx.Timestamp,
        });
    }

    private static BattleStateBlob BuildInitialBattleState(
        int unitsPerPlayer,
        Fix64 worldMinX,
        Fix64 worldMaxX,
        Fix64 worldMinY,
        Fix64 worldMaxY,
        ulong rngState)
    {
        var safeUnitsPerPlayer = Math.Max(1, unitsPerPlayer);
        var totalUnits = safeUnitsPerPlayer * 2;
        var blob = BattleStateBlob.CreateEmpty(totalUnits, rngState);

        var midY = (worldMinY + worldMaxY) / Fix64.FromInt(2);
        var teamASpawnX = worldMinX + SpawnPadding;
        var teamBSpawnX = worldMaxX - SpawnPadding;

        var teamAEntries = BuildTeamSpawnEntries(safeUnitsPerPlayer, SimConstants.TeamA);
        var teamBEntries = BuildTeamSpawnEntries(safeUnitsPerPlayer, SimConstants.TeamB);

        var teamAStart = 0;
        for (var i = 0; i < teamAEntries.Count; i++)
        {
            AddSpawnedUnit(blob, teamAEntries[i], teamAStart++, teamASpawnX, midY, safeUnitsPerPlayer, true);
        }

        var teamBStart = 0;
        for (var i = 0; i < teamBEntries.Count; i++)
        {
            AddSpawnedUnit(blob, teamBEntries[i], teamBStart++, teamBSpawnX, midY, safeUnitsPerPlayer, false);
        }

        blob.UnitCount = blob.Positions.Count;
        return blob;
    }

    private static List<(int ArchetypeId, byte Team)> BuildTeamSpawnEntries(int unitsPerPlayer, byte team)
    {
        var entries = new List<(int ArchetypeId, byte Team)>(unitsPerPlayer);
        var spearmanCount = (unitsPerPlayer * 2) / 3;
        var archerCount = unitsPerPlayer - spearmanCount;

        for (var i = 0; i < spearmanCount; i++)
        {
            entries.Add((0, team));
        }

        for (var i = 0; i < archerCount; i++)
        {
            entries.Add((1, team));
        }

        return entries;
    }

    private static void AddSpawnedUnit(
        BattleStateBlob blob,
        (int ArchetypeId, byte Team) spawn,
        int slot,
        Fix64 baseX,
        Fix64 midY,
        int unitsPerPlayer,
        bool moveRight)
    {
        var unit = SimContentRegistry.GetUnitByStableId(spawn.ArchetypeId);
        var columns = Math.Max(1, (int)Math.Sqrt(Math.Max(1, unitsPerPlayer)));
        var row = slot / columns;
        var column = slot % columns;

        var rowOffset = SpawnSpacing * Fix64.FromInt(row);
        var centeredColumn = (Fix64.FromInt(column) - (Fix64.FromInt(columns - 1) / Fix64.FromInt(2)));
        var columnOffset = centeredColumn * SpawnSpacing;
        var x = moveRight ? baseX + rowOffset : baseX - rowOffset;
        var y = midY + columnOffset;

        blob.Positions.Add(new FixVec2(x, y));
        blob.Velocities.Add(FixVec2.Zero);
        blob.Health.Add(unit.MaxHealth);
        blob.ArchetypeIds.Add(spawn.ArchetypeId);
        blob.Teams.Add(spawn.Team);
        blob.States.Add(SimConstants.UnitMoving);
        blob.AttackCooldownTicks.Add(0);
        blob.TargetIndices.Add(-1);
    }

    private static Identity ResolveWinnerPlayer(Battle battle, byte winnerTeam)
    {
        if (winnerTeam == SimConstants.TeamA)
        {
            return battle.PlayerA;
        }

        if (winnerTeam == SimConstants.TeamB)
        {
            return battle.PlayerB;
        }

        return default;
    }
}
