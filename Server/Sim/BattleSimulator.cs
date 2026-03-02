using System;

internal readonly struct BattleSimulationConfig
{
    public BattleSimulationConfig(
        uint tickRateTps,
        int maxTicks,
        Fix64 worldMinX,
        Fix64 worldMaxX,
        Fix64 worldMinY,
        Fix64 worldMaxY)
    {
        TickRateTps = Math.Max(1u, tickRateTps);
        MaxTicks = Math.Max(1, maxTicks);
        WorldMinX = worldMinX;
        WorldMaxX = worldMaxX;
        WorldMinY = worldMinY;
        WorldMaxY = worldMaxY;
    }

    public uint TickRateTps { get; }
    public int MaxTicks { get; }
    public Fix64 WorldMinX { get; }
    public Fix64 WorldMaxX { get; }
    public Fix64 WorldMinY { get; }
    public Fix64 WorldMaxY { get; }

    public Fix64 SpatialCellSize => Fix64.FromInt(2);
    public Fix64 SeekWeight => Fix64.FromRatio(7, 5);
    public Fix64 SeparationWeight => Fix64.FromRatio(9, 2);
    public Fix64 MaxSteeringForce => Fix64.FromInt(20);
    public Fix64 NeighborQueryRange => Fix64.FromInt(12);
}

internal readonly struct BattleStepResult
{
    public BattleStepResult(bool completed, byte winnerTeam, int aliveTeamA, int aliveTeamB, ulong digest)
    {
        Completed = completed;
        WinnerTeam = winnerTeam;
        AliveTeamA = aliveTeamA;
        AliveTeamB = aliveTeamB;
        Digest = digest;
    }

    public bool Completed { get; }
    public byte WinnerTeam { get; }
    public int AliveTeamA { get; }
    public int AliveTeamB { get; }
    public ulong Digest { get; }
}

internal static class BattleSimulator
{
    public static BattleStepResult Step(BattleStateRuntime state, BattleSimulationConfig config)
    {
        var unitCount = state.UnitCount;
        var force = new FixVec2[unitCount];
        var attackTarget = new int[unitCount];
        var attackDamage = new Fix64[unitCount];
        var newCooldown = new int[unitCount];
        var nextTarget = new int[unitCount];
        var damageAccum = new Fix64[unitCount];
        Array.Fill(attackTarget, -1);
        Array.Fill(nextTarget, -1);

        var dt = Fix64.One / Fix64.FromInt((int)config.TickRateTps);
        var grid = new BattleSpatialGrid(
            config.WorldMinX,
            config.WorldMinY,
            config.WorldMaxX,
            config.WorldMaxY,
            config.SpatialCellSize,
            unitCount);
        grid.Rebuild(state);

        for (var i = 0; i < unitCount; i++)
        {
            if (state.States[i] == SimConstants.UnitDead)
            {
                continue;
            }

            if (!SimContentRegistry.TryGetUnitByStableId(state.ArchetypeIds[i], out var archetype))
            {
                state.States[i] = SimConstants.UnitDead;
                state.Health[i] = Fix64.Zero;
                continue;
            }

            var position = state.Positions[i];
            var velocity = state.Velocities[i];

            var targetIndex = grid.FindNearestEnemy(state, i, config.NeighborQueryRange);
            nextTarget[i] = targetIndex;

            var separationForce = FixVec2.Zero;
            var separationCount = 0;
            grid.ForEachNeighbor(
                state,
                i,
                archetype.SeparationRadius,
                (neighborIndex, delta, distSq) =>
                {
                    if (state.Teams[neighborIndex] != state.Teams[i])
                    {
                        return;
                    }

                    var dist = Fix64.Sqrt(distSq);
                    if (dist <= Fix64.Epsilon)
                    {
                        return;
                    }

                    var dir = delta / dist;
                    var t = Fix64.One - (dist / archetype.SeparationRadius);
                    if (t <= Fix64.Zero)
                    {
                        return;
                    }

                    separationForce -= dir * (t * t);
                    separationCount++;
                });

            if (separationCount > 0)
            {
                separationForce = (separationForce / Fix64.FromInt(separationCount)) * config.SeparationWeight;
            }

            var seekForce = FixVec2.Zero;
            if (targetIndex >= 0 && state.States[targetIndex] != SimConstants.UnitDead)
            {
                var toEnemy = state.Positions[targetIndex] - position;
                var distanceSq = toEnemy.SqrMagnitude;
                var attackRangeSq = archetype.AttackRange * archetype.AttackRange;

                if (distanceSq <= attackRangeSq)
                {
                    if (state.AttackCooldownTicks[i] <= 0)
                    {
                        attackTarget[i] = targetIndex;
                        attackDamage[i] = archetype.AttackDamage;
                        newCooldown[i] = archetype.AttackCooldownTicks;
                    }
                }
                else if (distanceSq > Fix64.Epsilon)
                {
                    var desired = toEnemy.Normalized() * archetype.MaxSpeed;
                    seekForce = (desired - velocity) * config.SeekWeight;
                }
            }

            var steering = seekForce + separationForce;
            force[i] = FixVec2.ClampMagnitude(steering, config.MaxSteeringForce);
        }

        for (var i = 0; i < unitCount; i++)
        {
            var target = attackTarget[i];
            if (target >= 0 && target < unitCount)
            {
                damageAccum[target] += attackDamage[i];
            }
        }

        for (var i = 0; i < unitCount; i++)
        {
            if (state.States[i] == SimConstants.UnitDead)
            {
                continue;
            }

            if (!SimContentRegistry.TryGetUnitByStableId(state.ArchetypeIds[i], out var archetype))
            {
                state.States[i] = SimConstants.UnitDead;
                state.Health[i] = Fix64.Zero;
                continue;
            }

            if (damageAccum[i] > Fix64.Zero)
            {
                state.Health[i] -= damageAccum[i];
                if (state.Health[i] <= Fix64.Zero)
                {
                    state.Health[i] = Fix64.Zero;
                    state.States[i] = SimConstants.UnitDead;
                    state.Velocities[i] = FixVec2.Zero;
                    continue;
                }
            }

            if (state.AttackCooldownTicks[i] > 0)
            {
                state.AttackCooldownTicks[i]--;
            }

            if (attackTarget[i] >= 0)
            {
                state.AttackCooldownTicks[i] = Math.Max(1, newCooldown[i]);
                state.States[i] = SimConstants.UnitAttacking;
            }
            else if (nextTarget[i] >= 0)
            {
                state.States[i] = SimConstants.UnitMoving;
            }
            else
            {
                state.States[i] = SimConstants.UnitIdle;
            }

            state.TargetIndices[i] = nextTarget[i];
            var velocity = state.Velocities[i] + (force[i] * dt);
            velocity = FixVec2.ClampMagnitude(velocity, archetype.MaxSpeed);
            var position = state.Positions[i] + (velocity * dt);

            position.X = Fix64.Clamp(position.X, config.WorldMinX, config.WorldMaxX);
            position.Y = Fix64.Clamp(position.Y, config.WorldMinY, config.WorldMaxY);
            state.Velocities[i] = velocity;
            state.Positions[i] = position;
        }

        state.Tick++;

        var aliveTeamA = state.CountAlive(SimConstants.TeamA);
        var aliveTeamB = state.CountAlive(SimConstants.TeamB);
        var complete = state.Tick >= config.MaxTicks || aliveTeamA == 0 || aliveTeamB == 0;

        byte winner = SimConstants.TeamNeutral;
        if (complete)
        {
            if (aliveTeamA > aliveTeamB)
            {
                winner = SimConstants.TeamA;
            }
            else if (aliveTeamB > aliveTeamA)
            {
                winner = SimConstants.TeamB;
            }
        }

        var digest = BattleDigest.Compute(state);
        return new BattleStepResult(complete, winner, aliveTeamA, aliveTeamB, digest);
    }
}
