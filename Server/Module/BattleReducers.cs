using System;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CreateBattle(
        ReducerContext ctx,
        ulong matchId,
        ulong roundId,
        Identity playerA,
        Identity playerB,
        uint tickRateTps,
        uint snapshotEveryNTicks,
        int maxTicks,
        int unitsPerPlayer)
    {
        _ = GetMatchOrThrow(ctx, matchId);
        if (ctx.Db.match_round.RoundId.Find(roundId) is not MatchRound round || round.MatchId != matchId)
        {
            throw new Exception("Round does not belong to the provided match.");
        }

        if (tickRateTps == 0 || snapshotEveryNTicks == 0)
        {
            throw new Exception("tickRateTps and snapshotEveryNTicks must be > 0.");
        }

        if (maxTicks <= 0 || unitsPerPlayer <= 0)
        {
            throw new Exception("maxTicks and unitsPerPlayer must be > 0.");
        }

        _ = CreateBattleInternal(
            ctx,
            matchId,
            roundId,
            playerA,
            playerB,
            tickRateTps,
            snapshotEveryNTicks,
            maxTicks,
            unitsPerPlayer,
            roundId ^ matchId ^ (ulong)unitsPerPlayer);
    }

    [SpacetimeDB.Reducer]
    public static void StartBattle(ReducerContext ctx, ulong battleId)
    {
        var battle = GetBattleOrThrow(ctx, battleId);
        if (battle.Status == SimConstants.BattleCompleted)
        {
            throw new Exception("Cannot restart a completed battle.");
        }

        var updated = battle;
        updated.Status = SimConstants.BattleRunning;
        ctx.Db.battle.BattleId.Update(updated);
        UpsertBattleTimer(ctx, battleId, updated.TickRateTps);
    }

    [SpacetimeDB.Reducer]
    public static void StopBattle(ReducerContext ctx, ulong battleId)
    {
        var battle = GetBattleOrThrow(ctx, battleId);
        if (battle.Status == SimConstants.BattleCompleted)
        {
            return;
        }

        var updated = battle;
        updated.Status = SimConstants.BattleStopped;
        ctx.Db.battle.BattleId.Update(updated);
        DeleteBattleTimer(ctx, battleId);
    }

    [SpacetimeDB.Reducer]
    public static void TickBattle(ReducerContext ctx, BattleTickTimer timer)
    {
        if (ctx.Db.battle.BattleId.Find(timer.BattleId) is not Battle battle)
        {
            DeleteBattleTimer(ctx, timer.BattleId);
            return;
        }

        if (battle.Status != SimConstants.BattleRunning)
        {
            return;
        }

        if (ctx.Db.battle_state.BattleId.Find(timer.BattleId) is not BattleState battleStateRow)
        {
            var missingStateBattle = battle;
            missingStateBattle.Status = SimConstants.BattleStopped;
            ctx.Db.battle.BattleId.Update(missingStateBattle);
            DeleteBattleTimer(ctx, timer.BattleId);
            return;
        }

        var runtime = BattleStateRuntime.FromBlob(battleStateRow.State);
        var stepResult = BattleSimulator.Step(
            runtime,
            new BattleSimulationConfig(
                battle.TickRateTps,
                battle.MaxTicks,
                battle.WorldMinX,
                battle.WorldMaxX,
                battle.WorldMinY,
                battle.WorldMaxY));

        var updatedBlob = runtime.ToBlob();
        var updatedStateRow = battleStateRow;
        updatedStateRow.State = updatedBlob;
        ctx.Db.battle_state.BattleId.Update(updatedStateRow);

        var updatedBattle = battle;
        updatedBattle.CurrentTick = updatedBlob.Tick;
        updatedBattle.UnitCount = updatedBlob.UnitCount;
        updatedBattle.LastDigest = stepResult.Digest;

        var snapshotEvery = Math.Max(1u, updatedBattle.SnapshotEveryNTicks);
        if (updatedBlob.Tick % snapshotEvery == 0 || stepResult.Completed)
        {
            InsertBattleSnapshot(ctx, updatedBattle, updatedBlob, stepResult.Digest);
        }

        if (stepResult.Completed)
        {
            updatedBattle.Status = SimConstants.BattleCompleted;
            updatedBattle.WinnerTeam = stepResult.WinnerTeam;
            updatedBattle.HasWinnerPlayer = stepResult.WinnerTeam != SimConstants.TeamNeutral;
            updatedBattle.WinnerPlayer = ResolveWinnerPlayer(updatedBattle, stepResult.WinnerTeam);
            updatedBattle.CompletedAt = ctx.Timestamp;
            updatedBattle.HasCompletedAt = true;
            ctx.Db.battle.BattleId.Update(updatedBattle);
            DeleteBattleTimer(ctx, timer.BattleId);
            return;
        }

        ctx.Db.battle.BattleId.Update(updatedBattle);
    }
}
