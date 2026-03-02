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
        if (ctx.Db.MatchRound.RoundId.Find(roundId) is not MatchRound round || round.MatchId != matchId)
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
        ctx.Db.Battle.BattleId.Update(updated);
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
        ctx.Db.Battle.BattleId.Update(updated);
        DeleteBattleTimer(ctx, battleId);
    }

    [SpacetimeDB.Reducer]
    public static void SetUnitTarget(ReducerContext ctx, ulong battleId, int unitIndex, int targetUnitIndex)
    {
        var battle = GetBattleOrThrow(ctx, battleId);
        if (battle.Status != SimConstants.BattleRunning)
        {
            throw new Exception("Battle is not running.");
        }

        if (ctx.Sender != battle.PlayerA && ctx.Sender != battle.PlayerB)
        {
            throw new Exception("Sender is not part of this battle.");
        }

        if (unitIndex < 0 || targetUnitIndex < 0)
        {
            throw new Exception("unitIndex and targetUnitIndex must be >= 0.");
        }

        ctx.Db.BattleCommand.Insert(new BattleCommand
        {
            CommandId = 0,
            BattleId = battleId,
            Issuer = ctx.Sender,
            UnitIndex = unitIndex,
            TargetUnitIndex = targetUnitIndex,
            TickIssued = battle.CurrentTick,
            Consumed = false,
        });
    }

    [SpacetimeDB.Reducer]
    public static void QueueUnitStatus(
        ReducerContext ctx,
        ulong battleId,
        int unitIndex,
        byte statusKind,
        bool isPermanent,
        int durationTicks)
    {
        var battle = GetBattleOrThrow(ctx, battleId);
        if (battle.Status != SimConstants.BattleRunning)
        {
            throw new Exception("Battle is not running.");
        }

        if (ctx.Sender != battle.PlayerA && ctx.Sender != battle.PlayerB)
        {
            throw new Exception("Sender is not part of this battle.");
        }

        if (unitIndex < 0)
        {
            throw new Exception("unitIndex must be >= 0.");
        }

        var resolvedKind = (SimStatusEffectKind)statusKind;
        if (StatusMaskForKind(resolvedKind) == 0)
        {
            throw new Exception("statusKind is invalid.");
        }

        if (durationTicks < 0)
        {
            throw new Exception("durationTicks must be >= 0.");
        }

        if (!isPermanent && durationTicks <= 0)
        {
            throw new Exception("durationTicks must be > 0 for non-permanent statuses.");
        }

        ctx.Db.BattleStatusCommand.Insert(new BattleStatusCommand
        {
            CommandId = 0,
            BattleId = battleId,
            Issuer = ctx.Sender,
            UnitIndex = unitIndex,
            StatusKind = statusKind,
            IsPermanent = isPermanent,
            DurationTicks = durationTicks,
            TickIssued = battle.CurrentTick,
            Consumed = false,
        });
    }

    [SpacetimeDB.Reducer]
    public static void TickBattle(ReducerContext ctx, BattleTickTimer timer)
    {
        if (ctx.Db.Battle.BattleId.Find(timer.BattleId) is not Battle battle)
        {
            DeleteBattleTimer(ctx, timer.BattleId);
            return;
        }

        if (battle.Status != SimConstants.BattleRunning)
        {
            return;
        }

        if (ctx.Db.BattleState.BattleId.Find(timer.BattleId) is not BattleState battleStateRow)
        {
            var missingStateBattle = battle;
            missingStateBattle.Status = SimConstants.BattleStopped;
            ctx.Db.Battle.BattleId.Update(missingStateBattle);
            DeleteBattleTimer(ctx, timer.BattleId);
            return;
        }

        var runtime = BattleStateRuntime.FromBlob(battleStateRow.State);
        ApplyPendingStatusCommands(ctx, battle, runtime);
        ApplyPendingTargetCommands(ctx, battle, runtime);
        var stepResult = BattleSimulator.Step(
            runtime,
            new BattleSimulationConfig(
                battle.TickRateTps,
                battle.MaxTicks,
                battle.WorldMinX,
                battle.WorldMaxX,
                battle.WorldMinY,
                battle.WorldMaxY,
                BakedFlowField.GetBuildingObstacles()));

        var updatedBlob = runtime.ToBlob();
        var updatedStateRow = battleStateRow;
        updatedStateRow.State = updatedBlob;
        ctx.Db.BattleState.BattleId.Update(updatedStateRow);

        var updatedBattle = battle;
        updatedBattle.CurrentTick = updatedBlob.Tick;
        updatedBattle.UnitCount = updatedBlob.UnitCount;
        updatedBattle.LastDigest = stepResult.Digest;

        var snapshotEvery = Math.Max(1u, updatedBattle.SnapshotEveryNTicks);
        if (updatedBlob.Tick % snapshotEvery == 0 || stepResult.Completed)
        {
            InsertBattleSnapshot(ctx, updatedBattle, updatedBlob, stepResult.Digest);
            TrimBattleSnapshots(ctx, updatedBattle.BattleId, updatedBattle.SnapshotRetention);
        }

        if (stepResult.Completed)
        {
            updatedBattle.Status = SimConstants.BattleCompleted;
            updatedBattle.WinnerTeam = stepResult.WinnerTeam;
            updatedBattle.HasWinnerPlayer = stepResult.WinnerTeam != SimConstants.TeamNeutral;
            updatedBattle.WinnerPlayer = ResolveWinnerPlayer(updatedBattle, stepResult.WinnerTeam);
            updatedBattle.CompletedAt = ctx.Timestamp;
            updatedBattle.HasCompletedAt = true;
            ctx.Db.Battle.BattleId.Update(updatedBattle);
            TrimBattleSnapshots(ctx, updatedBattle.BattleId, updatedBattle.SnapshotRetention);
            DeleteBattleTimer(ctx, timer.BattleId);
            SetPlayerBattleView(ctx, updatedBattle.PlayerA, updatedBattle.PlayerB, updatedBattle, false);
            SetPlayerBattleView(ctx, updatedBattle.PlayerB, updatedBattle.PlayerA, updatedBattle, false);
            _ = TryFinalizeRoundIfComplete(ctx, updatedBattle.MatchId, updatedBattle.RoundId, throwIfIncomplete: false);
            return;
        }

        ctx.Db.Battle.BattleId.Update(updatedBattle);
    }
}
