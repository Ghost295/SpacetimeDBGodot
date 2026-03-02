using System;

internal readonly struct BattleSimulationConfig
{
    public BattleSimulationConfig(
        uint tickRateTps,
        int maxTicks,
        Fix64 worldMinX,
        Fix64 worldMaxX,
        Fix64 worldMinY,
        Fix64 worldMaxY,
        BakedBuildingObstacle[]? buildingObstacles = null)
    {
        TickRateTps = Math.Max(1u, tickRateTps);
        MaxTicks = Math.Max(1, maxTicks);
        WorldMinX = worldMinX;
        WorldMaxX = worldMaxX;
        WorldMinY = worldMinY;
        WorldMaxY = worldMaxY;
        BuildingObstacles = buildingObstacles ?? Array.Empty<BakedBuildingObstacle>();
    }

    public uint TickRateTps { get; }
    public int MaxTicks { get; }
    public Fix64 WorldMinX { get; }
    public Fix64 WorldMaxX { get; }
    public Fix64 WorldMinY { get; }
    public Fix64 WorldMaxY { get; }
    public BakedBuildingObstacle[] BuildingObstacles { get; }

    public Fix64 SpatialCellSize => Fix64.FromInt(3);
    public Fix64 SeekWeight => Fix64.FromRatio(3, 2);
    public Fix64 MarchSeekWeight => Fix64.One;
    public Fix64 SeparationWeight => Fix64.FromInt(8);
    public Fix64 CollisionPushStrength => Fix64.FromInt(50);
    public Fix64 MaxSteeringForce => Fix64.FromInt(25);
    public Fix64 VelocityDampingMoving => Fix64.FromRatio(23, 25);
    public Fix64 VelocityDampingNonMoving => Fix64.FromRatio(7, 10);
    public Fix64 VelocityStopEpsilon => Fix64.FromRatio(1, 20);
    public Fix64 StopDistance => Fix64.FromRatio(1, 2);
    public Fix64 TargetAcquireRange => Fix64.FromInt(30);
    public Fix64 TargetLoseRange => Fix64.FromInt(40);
    public Fix64 TargetProgressThreshold => Fix64.One;
    public int TargetStuckThresholdTicks => 20;
    public int MeleeTargetStuckThresholdTicks => 60;
    public int TargetAbandonCooldownTicks => 30;
    public int TargetAcquireStride => 2;
    public Fix64 AttackRangeHysteresis => Fix64.FromRatio(1, 2);
    public Fix64 FlowLookAhead => Fix64.FromInt(5);
    public Fix64 MeleeAttackRangeThreshold => Fix64.FromInt(3);
    public Fix64 MeleeApproachLateralBase => Fix64.Zero;
    public Fix64 MeleeApproachLateralStuckBonus => Fix64.FromRatio(11, 20);
    public int CongestionDensityThreshold => 6;
    public int CongestionSampleRadiusCells => 1;
    public Fix64 BuildingAvoidanceStrength => Fix64.FromInt(140);
    public Fix64 BuildingAvoidancePadding => Fix64.FromRatio(7, 20);
    public Fix64 BuildingAvoidanceMaxForce => Fix64.FromInt(80);
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
    private static readonly Fix64 Half = Fix64.FromRatio(1, 2);
    private static readonly Fix64 ArmorBreakArmorMultiplier = Fix64.FromRatio(1, 2);
    private static readonly Fix64 BleedDotHealthRatio = Fix64.FromRatio(1, 100);
    private static readonly Fix64 BurnDotHealthRatio = Fix64.FromRatio(3, 200);
    private static readonly Fix64 MinStatusDotDamage = Fix64.FromRatio(1, 4);
    private const int StatusMaskBleed = 1 << 0;
    private const int StatusMaskBurn = 1 << 1;
    private const int StatusMaskSlow = 1 << 2;
    private const int StatusMaskStun = 1 << 3;
    private const int StatusMaskArmorBreak = 1 << 4;

    private readonly struct ResolvedAttackProfile
    {
        public ResolvedAttackProfile(
            Fix64 attackDamage,
            Fix64 attackRange,
            int attackCooldownTicks,
            SimUnitStatusEffectDefinition[] statuses)
        {
            AttackDamage = attackDamage;
            AttackRange = attackRange < Fix64.Zero ? Fix64.Zero : attackRange;
            AttackCooldownTicks = Math.Max(1, attackCooldownTicks);
            Statuses = statuses ?? [];
        }

        public Fix64 AttackDamage { get; }
        public Fix64 AttackRange { get; }
        public int AttackCooldownTicks { get; }
        public SimUnitStatusEffectDefinition[] Statuses { get; }
    }

    public static BattleStepResult Step(BattleStateRuntime state, BattleSimulationConfig config)
    {
        var unitCount = state.UnitCount;
        var force = new FixVec2[unitCount];
        var maxSpeedByUnit = new Fix64[unitCount];
        var attackTarget = new int[unitCount];
        var attackDamage = new Fix64[unitCount];
        var attackStatuses = new SimUnitStatusEffectDefinition[unitCount][];
        var newCooldown = new int[unitCount];
        var nextTarget = new int[unitCount];
        var nextTargetCooldown = new int[unitCount];
        var nextTargetStuck = new int[unitCount];
        var nextTargetLastDistSq = new Fix64[unitCount];
        var freezeMovement = new bool[unitCount];
        var holdAttackState = new bool[unitCount];
        var hasMovementIntent = new bool[unitCount];
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
        var congestionField = new BattleCongestionField(config);
        congestionField.Rebuild(state);

        var hasTeamACenter = TryComputeTeamCenter(state, SimConstants.TeamA, out var teamACenter);
        var hasTeamBCenter = TryComputeTeamCenter(state, SimConstants.TeamB, out var teamBCenter);

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
            var team = state.Teams[i];
            var hasStunStatus = IsStatusActive(state, i, SimStatusEffectKind.Stun);
            var unitMaxSpeed = archetype.MaxSpeed;
            maxSpeedByUnit[i] = unitMaxSpeed;

            var targetIndex = state.TargetIndices[i];
            var targetCooldown = state.TargetCooldownTicks[i];
            var targetStuckTicks = state.TargetStuckTicks[i];
            var targetLastDistanceSq = state.TargetLastDistanceSq[i];
            var unitIsMelee = archetype.AttackRange <= config.MeleeAttackRangeThreshold;
            var stuckThresholdTicks = unitIsMelee
                ? Math.Max(1, config.MeleeTargetStuckThresholdTicks)
                : Math.Max(1, config.TargetStuckThresholdTicks);
            if (targetCooldown > 0)
            {
                targetCooldown--;
            }

            var loseRangeSq = config.TargetLoseRange * config.TargetLoseRange;
            if (targetIndex >= 0)
            {
                if (targetIndex >= unitCount ||
                    state.States[targetIndex] == SimConstants.UnitDead ||
                    !SimContentRegistry.TryGetUnitByStableId(state.ArchetypeIds[targetIndex], out _))
                {
                    targetIndex = -1;
                    targetStuckTicks = 0;
                    targetLastDistanceSq = Fix64.Zero;
                }
                else
                {
                    var toTargetNow = state.Positions[targetIndex] - position;
                    var distNowSq = toTargetNow.SqrMagnitude;
                    if (distNowSq > loseRangeSq)
                    {
                        targetIndex = -1;
                        targetStuckTicks = 0;
                        targetLastDistanceSq = Fix64.Zero;
                    }
                    else
                    {
                        var progress = targetLastDistanceSq - distNowSq;
                        if (targetLastDistanceSq > Fix64.Zero && progress < config.TargetProgressThreshold)
                        {
                            targetStuckTicks++;
                        }
                        else
                        {
                            targetStuckTicks = 0;
                        }

                        targetLastDistanceSq = distNowSq;
                        if (targetStuckTicks >= stuckThresholdTicks)
                        {
                            targetIndex = -1;
                            targetCooldown = config.TargetAbandonCooldownTicks;
                            targetStuckTicks = 0;
                            targetLastDistanceSq = Fix64.Zero;
                        }
                    }
                }
            }

            if (targetIndex < 0 &&
                targetCooldown <= 0 &&
                ((state.Tick + i) % Math.Max(1, config.TargetAcquireStride) == 0))
            {
                targetIndex = grid.FindNearestEnemy(state, i, config.TargetAcquireRange);
                if (targetIndex >= 0)
                {
                    var toTargetNow = state.Positions[targetIndex] - position;
                    targetLastDistanceSq = toTargetNow.SqrMagnitude;
                    targetStuckTicks = 0;
                }
            }

            nextTarget[i] = targetIndex;
            nextTargetCooldown[i] = targetCooldown;
            nextTargetStuck[i] = targetStuckTicks;
            nextTargetLastDistSq[i] = targetLastDistanceSq;

            var collisionForce = FixVec2.Zero;
            var separationForce = FixVec2.Zero;
            var separationCount = 0;
            var collisionQueryRadius = Fix64.Max(
                archetype.SeparationRadius,
                archetype.CollisionRadius * Fix64.FromInt(2));
            grid.ForEachNeighbor(
                state,
                i,
                collisionQueryRadius,
                (neighborIndex, delta, distSq) =>
                {
                    if (!SimContentRegistry.TryGetUnitByStableId(state.ArchetypeIds[neighborIndex], out var neighborArchetype))
                    {
                        return;
                    }

                    var dist = Fix64.Sqrt(distSq);
                    if (dist <= Fix64.Epsilon)
                    {
                        return;
                    }

                    var dir = delta / dist;
                    var collisionDistance = archetype.CollisionRadius + neighborArchetype.CollisionRadius;
                    if (collisionDistance > Fix64.Zero && dist < collisionDistance)
                    {
                        var overlap = collisionDistance - dist;
                        var push = config.CollisionPushStrength * overlap;
                        collisionForce -= dir * push;
                    }

                    if (state.Teams[neighborIndex] != state.Teams[i])
                    {
                        return;
                    }

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

            var buildingForce = FixVec2.Zero;
            var hasBuildingOverlap = false;
            var buildingObstacles = config.BuildingObstacles;
            for (var obstacleIdx = 0; obstacleIdx < buildingObstacles.Length; obstacleIdx++)
            {
                var obstacle = buildingObstacles[obstacleIdx];
                var deltaToObstacle = position - new FixVec2(obstacle.X, obstacle.Z);
                var distSq = deltaToObstacle.SqrMagnitude;
                var keepOut = obstacle.Radius + archetype.CollisionRadius + config.BuildingAvoidancePadding;
                var keepOutSq = keepOut * keepOut;
                if (distSq >= keepOutSq)
                {
                    continue;
                }

                var distance = distSq > Fix64.Epsilon ? Fix64.Sqrt(distSq) : Fix64.Zero;
                var directionAway = distance > Fix64.Epsilon
                    ? deltaToObstacle / distance
                    : new FixVec2(Fix64.One, Fix64.Zero);
                var overlap = keepOut - distance;
                var push = config.BuildingAvoidanceStrength * overlap;
                buildingForce += directionAway * push;
                hasBuildingOverlap = true;
            }

            var seekForce = FixVec2.Zero;
            var hasTargetPoint = false;
            var targetPoint = FixVec2.Zero;
            var attackRangeSq = Fix64.Zero;
            var isMeleeTarget = false;
            var freezeInPlace = false;
            if (!hasStunStatus &&
                targetIndex >= 0 &&
                state.States[targetIndex] != SimConstants.UnitDead &&
                SimContentRegistry.TryGetUnitByStableId(state.ArchetypeIds[targetIndex], out var targetArchetype))
            {
                var toEnemy = state.Positions[targetIndex] - position;
                var distanceSq = toEnemy.SqrMagnitude;
                var attackProfile = ResolveAttackProfile(
                    archetype,
                    team,
                    targetArchetype,
                    state.Teams[targetIndex],
                    distanceSq,
                    config.TickRateTps);
                attackRangeSq = attackProfile.AttackRange * attackProfile.AttackRange;
                isMeleeTarget = attackProfile.AttackRange <= config.MeleeAttackRangeThreshold;
                var attackHoldRange = attackProfile.AttackRange + config.AttackRangeHysteresis;
                var attackHoldRangeSq = attackHoldRange * attackHoldRange;
                holdAttackState[i] = distanceSq <= attackHoldRangeSq;
                freezeInPlace = state.States[i] == SimConstants.UnitAttacking && distanceSq <= attackRangeSq;

                if (distanceSq <= attackRangeSq)
                {
                    if (state.AttackCooldownTicks[i] <= 0)
                    {
                        var totalAttackDamage = attackProfile.AttackDamage + state.AttackDamageBonus[i];
                        if (totalAttackDamage < Fix64.Zero)
                        {
                            totalAttackDamage = Fix64.Zero;
                        }

                        attackTarget[i] = targetIndex;
                        attackDamage[i] = totalAttackDamage;
                        attackStatuses[i] = attackProfile.Statuses;
                        newCooldown[i] = attackProfile.AttackCooldownTicks;
                    }
                }
                else if (distanceSq > attackHoldRangeSq)
                {
                    hasTargetPoint = true;
                    if (isMeleeTarget)
                    {
                        var meleeApproachThresholdTicks = Math.Max(1, config.MeleeTargetStuckThresholdTicks);
                        targetPoint = ComputeMeleeApproachPoint(
                            position,
                            state.Positions[targetIndex],
                            attackProfile.AttackRange,
                            config.StopDistance,
                            targetStuckTicks,
                            meleeApproachThresholdTicks,
                            i,
                            targetIndex,
                            config.MeleeApproachLateralBase,
                            config.MeleeApproachLateralStuckBonus);
                    }
                    else
                    {
                        targetPoint = state.Positions[targetIndex];
                    }
                }
            }
            else if (!hasStunStatus && TryGetFlowDirection(team, position, out var flowDirection))
            {
                hasTargetPoint = true;
                targetPoint = position + (flowDirection * config.FlowLookAhead);
            }
            else if (!hasStunStatus && team == SimConstants.TeamA && hasTeamBCenter)
            {
                hasTargetPoint = true;
                targetPoint = teamBCenter;
            }
            else if (!hasStunStatus && team == SimConstants.TeamB && hasTeamACenter)
            {
                hasTargetPoint = true;
                targetPoint = teamACenter;
            }

            if (hasTargetPoint)
            {
                var toTarget = targetPoint - position;
                var targetDistSq = toTarget.SqrMagnitude;
                if (targetDistSq > Fix64.Epsilon)
                {
                    var desiredDir = toTarget.Normalized();
                    var localDensity = congestionField.GetLocalDensity(
                        team,
                        position,
                        config.CongestionSampleRadiusCells);
                    if (localDensity >= config.CongestionDensityThreshold)
                    {
                        var suggestedDirection = congestionField.SuggestDirection(team, position, desiredDir);
                        var congestionBlendWeight = targetIndex >= 0 && isMeleeTarget
                            ? Fix64.FromRatio(4, 5)
                            : Fix64.FromRatio(2, 5);
                        desiredDir = ((desiredDir * (Fix64.One - congestionBlendWeight)) + (suggestedDirection * congestionBlendWeight)).Normalized();
                    }

                    var isInAttackRange = targetIndex >= 0 && targetDistSq <= attackRangeSq;
                    if (!isInAttackRange)
                    {
                        var desired = desiredDir * unitMaxSpeed;
                        var seekWeight = targetIndex >= 0 ? config.SeekWeight : config.MarchSeekWeight;
                        seekForce = (desired - velocity) * seekWeight;
                        hasMovementIntent[i] = true;
                    }
                }
            }

            var steering = seekForce + collisionForce + separationForce + buildingForce;
            var maxSteeringForce = hasBuildingOverlap
                ? Fix64.Max(config.MaxSteeringForce, config.BuildingAvoidanceMaxForce)
                : config.MaxSteeringForce;
            if (freezeInPlace)
            {
                freezeMovement[i] = true;
                force[i] = hasBuildingOverlap
                    ? FixVec2.ClampMagnitude(buildingForce, maxSteeringForce)
                    : FixVec2.Zero;
            }
            else
            {
                force[i] = FixVec2.ClampMagnitude(steering, maxSteeringForce);
            }
        }

        for (var i = 0; i < unitCount; i++)
        {
            var target = attackTarget[i];
            if (target >= 0 && target < unitCount)
            {
                if (SimContentRegistry.TryGetUnitByStableId(state.ArchetypeIds[target], out var targetArchetype))
                {
                    damageAccum[target] += ApplyArmorMitigation(state, target, attackDamage[i], targetArchetype);
                }
                else
                {
                    damageAccum[target] += attackDamage[i];
                }

                ApplyStatusesFromAttack(state, target, attackStatuses[i], config.TickRateTps);
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
                continue;
            }

            var statusDotDamage = ComputeStatusDotDamage(state, i, archetype);
            if (statusDotDamage > Fix64.Zero)
            {
                damageAccum[i] += statusDotDamage;
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
                ClearTimedStatuses(state, i);
                continue;
            }
            var hasStunStatus = IsStatusActive(state, i, SimStatusEffectKind.Stun);
            var unitMaxSpeed = maxSpeedByUnit[i] > Fix64.Zero
                ? maxSpeedByUnit[i]
                : archetype.MaxSpeed;

            if (damageAccum[i] > Fix64.Zero)
            {
                state.Health[i] -= damageAccum[i];
                if (state.Health[i] <= Fix64.Zero)
                {
                    state.Health[i] = Fix64.Zero;
                    state.States[i] = SimConstants.UnitDead;
                    state.Velocities[i] = FixVec2.Zero;
                    ClearTimedStatuses(state, i);
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
            else if (hasStunStatus)
            {
                state.States[i] = SimConstants.UnitIdle;
            }
            else if (holdAttackState[i] && nextTarget[i] >= 0)
            {
                state.States[i] = SimConstants.UnitAttacking;
            }
            else if (nextTarget[i] >= 0 || hasMovementIntent[i])
            {
                state.States[i] = SimConstants.UnitMoving;
            }
            else
            {
                state.States[i] = SimConstants.UnitIdle;
            }

            state.TargetIndices[i] = nextTarget[i];
            state.TargetCooldownTicks[i] = nextTargetCooldown[i];
            state.TargetStuckTicks[i] = nextTargetStuck[i];
            state.TargetLastDistanceSq[i] = nextTargetLastDistSq[i];
            if (hasStunStatus)
            {
                state.Velocities[i] = FixVec2.Zero;
                TickStatusDurations(state, i);
                continue;
            }

            var velocity = state.Velocities[i];
            if (freezeMovement[i])
            {
                velocity = force[i].SqrMagnitude > Fix64.Epsilon
                    ? force[i] * dt
                    : FixVec2.Zero;
            }
            else
            {
                velocity += force[i] * dt;
                var damping = state.States[i] == SimConstants.UnitMoving
                    ? config.VelocityDampingMoving
                    : config.VelocityDampingNonMoving;
                velocity *= damping;
                var stopEpsilonSq = config.VelocityStopEpsilon * config.VelocityStopEpsilon;
                if (velocity.SqrMagnitude < stopEpsilonSq)
                {
                    velocity = FixVec2.Zero;
                }

                velocity = FixVec2.ClampMagnitude(velocity, unitMaxSpeed);
                if (state.States[i] == SimConstants.UnitMoving && unitMaxSpeed > Fix64.Zero)
                {
                    var moveDirection = velocity.SqrMagnitude > Fix64.Epsilon
                        ? velocity.Normalized()
                        : force[i].Normalized();
                    if (moveDirection.SqrMagnitude > Fix64.Epsilon)
                    {
                        velocity = moveDirection * unitMaxSpeed;
                    }
                }
            }

            var position = state.Positions[i] + (velocity * dt);

            position.X = Fix64.Clamp(position.X, config.WorldMinX, config.WorldMaxX);
            position.Y = Fix64.Clamp(position.Y, config.WorldMinY, config.WorldMaxY);
            state.Velocities[i] = velocity;
            state.Positions[i] = position;
            TickStatusDurations(state, i);
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

    private static ResolvedAttackProfile ResolveAttackProfile(
        SimUnitArchetype attackerArchetype,
        byte attackerTeam,
        SimUnitArchetype targetArchetype,
        byte targetTeam,
        Fix64 distanceSq,
        uint tickRateTps)
    {
        if (TrySelectAttackAbility(attackerArchetype, distanceSq, out var selectedAbility))
        {
            var damage = ApplyAbilityBonuses(
                selectedAbility,
                attackerTeam,
                targetArchetype.UnitClass,
                targetTeam);
            damage = ApplyNonBasicDamageProfiles(damage, attackerArchetype, targetArchetype);
            var cooldown = ComputeAttackCooldownTicksFromSpeed(selectedAbility.AttackSpeed, tickRateTps);
            return new ResolvedAttackProfile(damage, selectedAbility.AttackRange, cooldown, selectedAbility.Statuses);
        }

        var fallbackDamage = ApplyNonBasicDamageProfiles(
            attackerArchetype.AttackDamage,
            attackerArchetype,
            targetArchetype);
        return new ResolvedAttackProfile(
            fallbackDamage,
            attackerArchetype.AttackRange,
            attackerArchetype.AttackCooldownTicks,
            []);
    }

    private static bool TrySelectAttackAbility(
        SimUnitArchetype attackerArchetype,
        Fix64 distanceSq,
        out SimUnitAbilityDefinition selectedAbility)
    {
        selectedAbility = default;
        var abilities = attackerArchetype.Abilities ?? [];
        if (abilities.Length == 0)
        {
            return false;
        }

        var foundReachable = false;
        var foundFallback = false;
        var bestReachableRange = Fix64.Zero;
        var bestFallbackRange = Fix64.Zero;
        for (var i = 0; i < abilities.Length; i++)
        {
            var ability = abilities[i];
            if (ability.AttackRange <= Fix64.Zero || ability.AttackSpeed <= Fix64.Zero)
            {
                continue;
            }

            var abilityRangeSq = ability.AttackRange * ability.AttackRange;
            if (abilityRangeSq >= distanceSq)
            {
                if (!foundReachable || ability.AttackRange < bestReachableRange)
                {
                    selectedAbility = ability;
                    bestReachableRange = ability.AttackRange;
                    foundReachable = true;
                }
            }
            else if (!foundReachable && (!foundFallback || ability.AttackRange > bestFallbackRange))
            {
                selectedAbility = ability;
                bestFallbackRange = ability.AttackRange;
                foundFallback = true;
            }
        }

        return foundReachable || foundFallback;
    }

    private static Fix64 ApplyAbilityBonuses(
        SimUnitAbilityDefinition ability,
        byte attackerTeam,
        SimUnitClass targetUnitClass,
        byte targetTeam)
    {
        var damage = ability.AttackDamage;
        var bonusMultiplier = Fix64.One;
        var bonuses = ability.Bonuses ?? [];
        for (var i = 0; i < bonuses.Length; i++)
        {
            var bonus = bonuses[i];
            if (!DoesBonusApplyToTarget(bonus.TargetClass, attackerTeam, targetTeam, targetUnitClass))
            {
                continue;
            }

            if (bonus.IsMultiplier)
            {
                bonusMultiplier *= Fix64.One + bonus.Amount;
            }
            else
            {
                damage += bonus.Amount;
            }
        }

        if (bonusMultiplier < Fix64.Zero)
        {
            bonusMultiplier = Fix64.Zero;
        }

        return damage * bonusMultiplier;
    }

    private static bool DoesBonusApplyToTarget(
        SimBonusTargetClass targetClass,
        byte attackerTeam,
        byte targetTeam,
        SimUnitClass targetUnitClass)
    {
        return targetClass switch
        {
            SimBonusTargetClass.Any => true,
            SimBonusTargetClass.Infantry => targetUnitClass is SimUnitClass.LightInfantry or SimUnitClass.HeavyInfantry,
            SimBonusTargetClass.Archer => targetUnitClass == SimUnitClass.Archer,
            SimBonusTargetClass.Cavalry => targetUnitClass is SimUnitClass.LightCavalry or SimUnitClass.HeavyCavalry,
            SimBonusTargetClass.Friendly => attackerTeam == targetTeam,
            SimBonusTargetClass.Enemy => attackerTeam != targetTeam,
            _ => false,
        };
    }

    private static int ComputeAttackCooldownTicksFromSpeed(Fix64 attackSpeed, uint tickRateTps)
    {
        if (attackSpeed <= Fix64.Zero)
        {
            return 1;
        }

        var safeTps = tickRateTps > int.MaxValue ? int.MaxValue : (int)Math.Max(1u, tickRateTps);
        var ticksFixed = Fix64.FromInt(safeTps) / attackSpeed;
        return Math.Max(1, (ticksFixed + Half).FloorToInt());
    }

    private static Fix64 ApplyNonBasicDamageProfiles(
        Fix64 baseDamage,
        SimUnitArchetype attackerArchetype,
        SimUnitArchetype targetArchetype)
    {
        var resolvedDamage = baseDamage + attackerArchetype.AreaDamage;
        if (targetArchetype.UnitClass == SimUnitClass.Siege)
        {
            resolvedDamage += attackerArchetype.SiegeDamage;
        }

        return resolvedDamage;
    }

    private static Fix64 ApplyArmorMitigation(
        BattleStateRuntime state,
        int targetIndex,
        Fix64 incomingDamage,
        SimUnitArchetype targetArchetype)
    {
        if (incomingDamage <= Fix64.Zero)
        {
            return Fix64.Zero;
        }

        var effectiveArmor = targetArchetype.Armor;
        if (IsStatusActive(state, targetIndex, SimStatusEffectKind.ArmorBreak))
        {
            effectiveArmor *= ArmorBreakArmorMultiplier;
        }

        var mitigated = incomingDamage - effectiveArmor;
        return mitigated < Fix64.Zero ? Fix64.Zero : mitigated;
    }

    private static void ApplyStatusesFromAttack(
        BattleStateRuntime state,
        int targetIndex,
        SimUnitStatusEffectDefinition[]? statuses,
        uint tickRateTps)
    {
        if (statuses == null || statuses.Length == 0)
        {
            return;
        }

        if (targetIndex < 0 || targetIndex >= state.UnitCount)
        {
            return;
        }

        for (var i = 0; i < statuses.Length; i++)
        {
            var status = statuses[i];
            var durationTicks = status.IsPermanent
                ? 0
                : ConvertStatusTimeToTicks(status.TimeLeft, tickRateTps);
            ApplyStatusToUnit(state, targetIndex, status.Kind, status.IsPermanent, durationTicks);
        }
    }

    private static void ApplyStatusToUnit(
        BattleStateRuntime state,
        int unitIndex,
        SimStatusEffectKind kind,
        bool isPermanent,
        int durationTicks)
    {
        var mask = StatusMaskForKind(kind);
        if (mask == 0)
        {
            return;
        }

        if (isPermanent)
        {
            state.StatusPermanentMask[unitIndex] |= mask;
            return;
        }

        if (durationTicks <= 0)
        {
            return;
        }

        switch (kind)
        {
            case SimStatusEffectKind.Bleed:
                state.StatusBleedTicks[unitIndex] = Math.Max(state.StatusBleedTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.Burn:
                state.StatusBurnTicks[unitIndex] = Math.Max(state.StatusBurnTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.Slow:
                state.StatusSlowTicks[unitIndex] = Math.Max(state.StatusSlowTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.Stun:
                state.StatusStunTicks[unitIndex] = Math.Max(state.StatusStunTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.ArmorBreak:
                state.StatusArmorBreakTicks[unitIndex] = Math.Max(state.StatusArmorBreakTicks[unitIndex], durationTicks);
                break;
        }
    }

    private static int ConvertStatusTimeToTicks(Fix64 timeLeft, uint tickRateTps)
    {
        if (timeLeft <= Fix64.Zero)
        {
            return 0;
        }

        var safeTps = tickRateTps > int.MaxValue ? int.MaxValue : (int)Math.Max(1u, tickRateTps);
        var tickCount = (timeLeft * Fix64.FromInt(safeTps)) + Half;
        return Math.Max(1, tickCount.FloorToInt());
    }

    private static Fix64 ComputeStatusDotDamage(BattleStateRuntime state, int unitIndex, SimUnitArchetype archetype)
    {
        var damage = Fix64.Zero;
        if (IsStatusActive(state, unitIndex, SimStatusEffectKind.Bleed))
        {
            damage += Fix64.Max(MinStatusDotDamage, archetype.MaxHealth * BleedDotHealthRatio);
        }

        if (IsStatusActive(state, unitIndex, SimStatusEffectKind.Burn))
        {
            damage += Fix64.Max(MinStatusDotDamage, archetype.MaxHealth * BurnDotHealthRatio);
        }

        return damage;
    }

    private static bool IsStatusActive(BattleStateRuntime state, int unitIndex, SimStatusEffectKind kind)
    {
        if (unitIndex < 0 || unitIndex >= state.UnitCount)
        {
            return false;
        }

        var mask = StatusMaskForKind(kind);
        if (mask == 0)
        {
            return false;
        }

        if ((state.StatusPermanentMask[unitIndex] & mask) != 0)
        {
            return true;
        }

        return kind switch
        {
            SimStatusEffectKind.Bleed => state.StatusBleedTicks[unitIndex] > 0,
            SimStatusEffectKind.Burn => state.StatusBurnTicks[unitIndex] > 0,
            SimStatusEffectKind.Slow => state.StatusSlowTicks[unitIndex] > 0,
            SimStatusEffectKind.Stun => state.StatusStunTicks[unitIndex] > 0,
            SimStatusEffectKind.ArmorBreak => state.StatusArmorBreakTicks[unitIndex] > 0,
            _ => false,
        };
    }

    private static void TickStatusDurations(BattleStateRuntime state, int unitIndex)
    {
        if ((state.StatusPermanentMask[unitIndex] & StatusMaskBleed) == 0 && state.StatusBleedTicks[unitIndex] > 0)
        {
            state.StatusBleedTicks[unitIndex]--;
        }

        if ((state.StatusPermanentMask[unitIndex] & StatusMaskBurn) == 0 && state.StatusBurnTicks[unitIndex] > 0)
        {
            state.StatusBurnTicks[unitIndex]--;
        }

        if ((state.StatusPermanentMask[unitIndex] & StatusMaskSlow) == 0 && state.StatusSlowTicks[unitIndex] > 0)
        {
            state.StatusSlowTicks[unitIndex]--;
        }

        if ((state.StatusPermanentMask[unitIndex] & StatusMaskStun) == 0 && state.StatusStunTicks[unitIndex] > 0)
        {
            state.StatusStunTicks[unitIndex]--;
        }

        if ((state.StatusPermanentMask[unitIndex] & StatusMaskArmorBreak) == 0 && state.StatusArmorBreakTicks[unitIndex] > 0)
        {
            state.StatusArmorBreakTicks[unitIndex]--;
        }
    }

    private static void ClearTimedStatuses(BattleStateRuntime state, int unitIndex)
    {
        state.StatusBleedTicks[unitIndex] = 0;
        state.StatusBurnTicks[unitIndex] = 0;
        state.StatusSlowTicks[unitIndex] = 0;
        state.StatusStunTicks[unitIndex] = 0;
        state.StatusArmorBreakTicks[unitIndex] = 0;
    }

    private static int StatusMaskForKind(SimStatusEffectKind kind)
    {
        return kind switch
        {
            SimStatusEffectKind.Bleed => StatusMaskBleed,
            SimStatusEffectKind.Burn => StatusMaskBurn,
            SimStatusEffectKind.Slow => StatusMaskSlow,
            SimStatusEffectKind.Stun => StatusMaskStun,
            SimStatusEffectKind.ArmorBreak => StatusMaskArmorBreak,
            _ => 0,
        };
    }

    private static bool TryGetFlowDirection(byte team, FixVec2 position, out FixVec2 flowDirection)
    {
        flowDirection = FixVec2.Zero;
        if (!BakedFlowField.HasTeamFlow(team))
        {
            return false;
        }

        if (!BakedFlowField.TryGetDirection(
                team,
                (float)position.X.ToDouble(),
                (float)position.Y.ToDouble(),
                out var direction))
        {
            return false;
        }

        flowDirection = new FixVec2(
            Fix64.FromDouble(direction.x),
            Fix64.FromDouble(direction.y));
        if (flowDirection.SqrMagnitude <= Fix64.Epsilon)
        {
            return false;
        }

        flowDirection = flowDirection.Normalized();
        return true;
    }

    private static FixVec2 ComputeMeleeApproachPoint(
        FixVec2 attackerPosition,
        FixVec2 targetPosition,
        Fix64 attackRange,
        Fix64 stopDistance,
        int stuckTicks,
        int stuckThresholdTicks,
        int attackerIndex,
        int targetIndex,
        Fix64 lateralBase,
        Fix64 lateralStuckBonus)
    {
        var toTarget = targetPosition - attackerPosition;
        var distSq = toTarget.SqrMagnitude;
        if (distSq <= Fix64.Epsilon)
        {
            return targetPosition;
        }

        var ringRadius = attackRange - stopDistance;
        var minRingRadius = Fix64.FromRatio(1, 10);
        if (ringRadius < minRingRadius)
        {
            ringRadius = minRingRadius;
        }

        var stuckT = Fix64.Zero;
        if (stuckThresholdTicks > 0 && stuckTicks > 0)
        {
            stuckT = Fix64.Clamp(
                Fix64.FromInt(stuckTicks) / Fix64.FromInt(stuckThresholdTicks),
                Fix64.Zero,
                Fix64.One);
        }

        var lateralMagnitude = lateralBase + (stuckT * lateralStuckBonus);
        var fromTarget = attackerPosition - targetPosition;
        var fromTargetDir = fromTarget.Normalized();
        if (fromTargetDir.SqrMagnitude <= Fix64.Epsilon)
        {
            fromTargetDir = new FixVec2(Fix64.One, Fix64.Zero);
        }

        var hash = unchecked(((uint)attackerIndex * 73856093u) ^ ((uint)Math.Max(0, targetIndex) * 19349663u));
        var lateralSign = (hash & 1u) == 0u ? Fix64.One : -Fix64.One;
        var perpendicular = new FixVec2(-fromTargetDir.Y, fromTargetDir.X);
        var approachDir = fromTargetDir + (perpendicular * lateralSign * lateralMagnitude);
        if (approachDir.SqrMagnitude <= Fix64.Epsilon)
        {
            approachDir = fromTargetDir;
        }
        else
        {
            approachDir = approachDir.Normalized();
        }

        return targetPosition + (approachDir * ringRadius);
    }

    private static bool TryComputeTeamCenter(BattleStateRuntime state, byte team, out FixVec2 center)
    {
        center = FixVec2.Zero;
        var count = 0;
        for (var i = 0; i < state.UnitCount; i++)
        {
            if (state.States[i] == SimConstants.UnitDead || state.Teams[i] != team)
            {
                continue;
            }

            center += state.Positions[i];
            count++;
        }

        if (count <= 0)
        {
            return false;
        }

        center /= Fix64.FromInt(count);
        return true;
    }
}
