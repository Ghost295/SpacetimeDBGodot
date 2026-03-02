using System;
using SpacetimeDB;

public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CreateAndJoinDebugMatch(ReducerContext ctx)
    {
        var match = ctx.Db.Match.Insert(new Match
        {
            MatchId = 0,
            CreatedBy = ctx.Sender,
            Status = SimConstants.MatchLobby,
            CurrentRound = 0,
            TickRateTps = 20,
            SnapshotEveryNTicks = 1,
            SnapshotRetention = 10,
            MaxBattleTicks = 2400,
            UnitsPerPlayer = 100,
            StartingHealth = 10,
            RoundDamage = 10,
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

        ctx.Db.MatchPlayer.Insert(new MatchPlayer
        {
            MatchPlayerId = 0,
            MatchId = match.MatchId,
            PlayerIdentity = ctx.Sender,
            SeatIndex = 0,
            Health = match.StartingHealth,
            Eliminated = false,
            JoinedAt = ctx.Timestamp,
        });

        SeedDefaultLoadoutForPlayer(ctx, match.MatchId, ctx.Sender);
    }

    [SpacetimeDB.Reducer]
    public static void RunDeterministicProbe(ReducerContext ctx, ulong matchId, ulong seed)
    {
        var match = GetMatchOrThrow(ctx, matchId);
        var players = GetActivePlayersForMatch(matchId, ctx);
        if (players.Count < 2)
        {
            throw new Exception("Need two active players for deterministic probe.");
        }

        var stateA = BuildInitialBattleState(
            ctx,
            matchId,
            players[0].PlayerIdentity,
            players[1].PlayerIdentity,
            match.UnitsPerPlayer,
            match.TickRateTps,
            seed);
        var stateB = BuildInitialBattleState(
            ctx,
            matchId,
            players[0].PlayerIdentity,
            players[1].PlayerIdentity,
            match.UnitsPerPlayer,
            match.TickRateTps,
            seed);

        var runtimeA = BattleStateRuntime.FromBlob(stateA);
        var runtimeB = BattleStateRuntime.FromBlob(stateB);
        var digestA = BattleDigest.Compute(runtimeA);
        var digestB = BattleDigest.Compute(runtimeB);
        if (digestA != digestB)
        {
            throw new Exception($"Deterministic probe failed for seed {seed}. digestA={digestA}, digestB={digestB}");
        }

        if (!TryGetBattleWorldBounds(out var worldMinX, out var worldMaxX, out var worldMinY, out var worldMaxY))
        {
            throw new Exception("Deterministic probe failed because world bounds are not configured.");
        }

        var simulationConfig = new BattleSimulationConfig(
            match.TickRateTps,
            match.MaxBattleTicks,
            worldMinX,
            worldMaxX,
            worldMinY,
            worldMaxY,
            BakedFlowField.GetBuildingObstacles());
        var checkpointStride = Math.Max(1, (int)Math.Max(1u, match.TickRateTps));

        var ranStep = false;
        var checkpointCount = 0;
        BattleStepResult finalA = default;
        BattleStepResult finalB = default;
        for (var step = 0; step < simulationConfig.MaxTicks; step++)
        {
            ranStep = true;
            finalA = BattleSimulator.Step(runtimeA, simulationConfig);
            finalB = BattleSimulator.Step(runtimeB, simulationConfig);

            if (runtimeA.Tick != runtimeB.Tick)
            {
                throw new Exception(
                    $"Deterministic probe failed for seed {seed}. Tick mismatch {runtimeA.Tick} vs {runtimeB.Tick}.");
            }

            var isCheckpoint =
                (runtimeA.Tick % checkpointStride) == 0 ||
                finalA.Completed ||
                finalB.Completed;
            if (isCheckpoint)
            {
                checkpointCount++;
                if (finalA.Digest != finalB.Digest)
                {
                    throw new Exception(
                        $"Deterministic probe failed for seed {seed} at tick {runtimeA.Tick}. digestA={finalA.Digest}, digestB={finalB.Digest}");
                }

                if (finalA.AliveTeamA != finalB.AliveTeamA || finalA.AliveTeamB != finalB.AliveTeamB)
                {
                    throw new Exception(
                        $"Deterministic probe failed for seed {seed} at tick {runtimeA.Tick}. aliveA={finalA.AliveTeamA}/{finalB.AliveTeamA}, aliveB={finalA.AliveTeamB}/{finalB.AliveTeamB}");
                }
            }

            if (finalA.Completed != finalB.Completed)
            {
                throw new Exception(
                    $"Deterministic probe failed for seed {seed}. completion mismatch at tick {runtimeA.Tick}.");
            }

            if (finalA.Completed)
            {
                break;
            }
        }

        if (!ranStep)
        {
            throw new Exception($"Deterministic probe failed for seed {seed}. No simulation steps were executed.");
        }

        if (!finalA.Completed || !finalB.Completed)
        {
            throw new Exception($"Deterministic probe failed for seed {seed}. Simulation did not complete.");
        }

        if (finalA.Digest != finalB.Digest)
        {
            throw new Exception(
                $"Deterministic probe failed for seed {seed}. final digestA={finalA.Digest}, final digestB={finalB.Digest}");
        }

        var outcomeClassA = ClassifyProbeOutcome(finalA);
        var outcomeClassB = ClassifyProbeOutcome(finalB);
        if (outcomeClassA != outcomeClassB || finalA.WinnerTeam != finalB.WinnerTeam)
        {
            throw new Exception(
                $"Deterministic probe failed for seed {seed}. outcomeA={outcomeClassA} winner={finalA.WinnerTeam}, outcomeB={outcomeClassB} winner={finalB.WinnerTeam}, checkpoints={checkpointCount}");
        }
    }

    private static string ClassifyProbeOutcome(BattleStepResult result)
    {
        if (result.WinnerTeam == SimConstants.TeamA)
        {
            return "team_a_win";
        }

        if (result.WinnerTeam == SimConstants.TeamB)
        {
            return "team_b_win";
        }

        if (result.AliveTeamA == 0 && result.AliveTeamB == 0)
        {
            return "double_knockout";
        }

        if (result.AliveTeamA == result.AliveTeamB)
        {
            return "timeout_draw";
        }

        return result.AliveTeamA > result.AliveTeamB
            ? "timeout_team_a_advantage"
            : "timeout_team_b_advantage";
    }
}
