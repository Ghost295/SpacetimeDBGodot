using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    private static readonly Fix64 SpawnSpacing = Fix64.FromRatio(3, 2);
    private static readonly Fix64 Half = Fix64.FromRatio(1, 2);
    private const int StatusMaskBleed = 1 << 0;
    private const int StatusMaskBurn = 1 << 1;
    private const int StatusMaskSlow = 1 << 2;
    private const int StatusMaskStun = 1 << 3;
    private const int StatusMaskArmorBreak = 1 << 4;

    private static Match GetMatchOrThrow(ReducerContext ctx, ulong matchId)
    {
        if (ctx.Db.Match.MatchId.Find(matchId) is Match match)
        {
            return match;
        }

        throw new Exception($"Match '{matchId}' was not found.");
    }

    private static Battle GetBattleOrThrow(ReducerContext ctx, ulong battleId)
    {
        if (ctx.Db.Battle.BattleId.Find(battleId) is Battle battle)
        {
            return battle;
        }

        throw new Exception($"Battle '{battleId}' was not found.");
    }

    private static MatchRound? FindCurrentRound(ReducerContext ctx, ulong matchId, int roundNumber)
    {
        MatchRound? found = null;
        foreach (var round in ctx.Db.MatchRound.Iter())
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
        foreach (var player in ctx.Db.MatchPlayer.Iter())
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
        foreach (var player in ctx.Db.MatchPlayer.Iter())
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
        foreach (var battle in ctx.Db.Battle.Iter())
        {
            if (battle.MatchId == matchId && battle.RoundId == roundId)
            {
                battles.Add(battle);
            }
        }

        return battles;
    }

    private static bool TryGetBattleWorldBounds(
        out Fix64 worldMinX,
        out Fix64 worldMaxX,
        out Fix64 worldMinY,
        out Fix64 worldMaxY)
    {
        worldMinX = Fix64.Zero;
        worldMaxX = Fix64.Zero;
        worldMinY = Fix64.Zero;
        worldMaxY = Fix64.Zero;
        if (!BakedFlowField.IsConfigured)
        {
            return false;
        }

        var minX = BakedFlowField.WorldOriginX;
        var minY = BakedFlowField.WorldOriginZ;
        var maxX = minX + BakedFlowField.FieldSizeX;
        var maxY = minY + BakedFlowField.FieldSizeY;
        if (maxX <= minX || maxY <= minY)
        {
            return false;
        }

        worldMinX = Fix64.FromInt(minX);
        worldMaxX = Fix64.FromInt(maxX);
        worldMinY = Fix64.FromInt(minY);
        worldMaxY = Fix64.FromInt(maxY);
        return true;
    }

    private static void SeedDefaultLoadoutForPlayer(ReducerContext ctx, ulong matchId, Identity playerIdentity)
    {
        var existingCards = GetPlayerCards(ctx, matchId, playerIdentity);
        if (existingCards.Count > 0)
        {
            return;
        }

        var team = ResolvePlayerTeamOrThrow(ctx, matchId, playerIdentity);
        var defaultGridIndex = ResolveDefaultGridIndexForTeamOrThrow(team);
        var starterCards = new[] { 0, 1 };
        for (var slot = 0; slot < starterCards.Length; slot++)
        {
            var stableId = starterCards[slot];
            if (!SimContentRegistry.TryGetCardByStableId(stableId, out var card))
            {
                continue;
            }

            if (!TryFindFirstPlacement(
                    ctx,
                    matchId,
                    playerIdentity,
                    defaultGridIndex,
                    card.CardSizeX,
                    card.CardSizeY,
                    out var cellX,
                    out var cellZ))
            {
                continue;
            }

            UpsertPlayerCardSlot(
                ctx,
                matchId,
                playerIdentity,
                slot,
                defaultGridIndex,
                cellX,
                cellZ,
                stableId,
                quantity: 1,
                level: 1,
                runtimeModifiers: new List<CardRuntimeModifierState>());
        }
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
        var match = GetMatchOrThrow(ctx, matchId);
        if (!TryGetBattleWorldBounds(out var worldMinX, out var worldMaxX, out var worldMinY, out var worldMaxY))
        {
            throw new Exception("Baked flowfield world bounds are not configured.");
        }

        var battle = ctx.Db.Battle.Insert(new Battle
        {
            BattleId = 0,
            MatchId = matchId,
            RoundId = roundId,
            PlayerA = playerA,
            PlayerB = playerB,
            Status = SimConstants.BattleRunning,
            TickRateTps = Math.Max(1u, tickRateTps),
            SnapshotEveryNTicks = Math.Max(1u, snapshotEveryNTicks),
            SnapshotRetention = Math.Max(1, match.SnapshotRetention),
            MaxTicks = Math.Max(1, maxTicks),
            CurrentTick = 0,
            UnitCount = 0,
            StaticContentHash = match.StaticContentHash,
            WorldMinX = worldMinX,
            WorldMaxX = worldMaxX,
            WorldMinY = worldMinY,
            WorldMaxY = worldMaxY,
            WinnerTeam = SimConstants.TeamNeutral,
            HasWinnerPlayer = false,
            WinnerPlayer = default,
            LastDigest = 0,
            CreatedAt = ctx.Timestamp,
            CompletedAt = default,
            HasCompletedAt = false,
        });

        var initialState = BuildInitialBattleState(
            ctx,
            matchId,
            playerA,
            playerB,
            unitsPerPlayer,
            battle.TickRateTps,
            rngSeed);

        ctx.Db.BattleState.Insert(new BattleState
        {
            BattleId = battle.BattleId,
            State = initialState,
        });

        var updateBattle = battle;
        updateBattle.UnitCount = initialState.UnitCount;
        ctx.Db.Battle.BattleId.Update(updateBattle);
        UpsertBattleTimer(ctx, battle.BattleId, battle.TickRateTps);
        InsertBattleSnapshot(ctx, updateBattle, initialState, BattleDigest.Compute(BattleStateRuntime.FromBlob(initialState)));
        SetPlayerBattleView(ctx, playerA, playerB, updateBattle, true);
        SetPlayerBattleView(ctx, playerB, playerA, updateBattle, true);
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

        if (ctx.Db.BattleTickTimer.BattleId.Find(battleId) is BattleTickTimer existing)
        {
            _ = existing;
            ctx.Db.BattleTickTimer.BattleId.Update(timerRow);
        }
        else
        {
            ctx.Db.BattleTickTimer.Insert(timerRow);
        }
    }

    private static void DeleteBattleTimer(ReducerContext ctx, ulong battleId)
    {
        if (ctx.Db.BattleTickTimer.BattleId.Find(battleId) is BattleTickTimer timer)
        {
            _ = timer;
            ctx.Db.BattleTickTimer.BattleId.Delete(battleId);
        }
    }

    private static void InsertBattleSnapshot(ReducerContext ctx, Battle battle, BattleStateBlob state, ulong digest)
    {
        ctx.Db.BattleSnapshot.Insert(new BattleSnapshot
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

    private static void TrimBattleSnapshots(ReducerContext ctx, ulong battleId, int keepCount)
    {
        if (keepCount <= 0)
        {
            return;
        }

        var snapshots = new List<BattleSnapshot>();
        foreach (var snapshot in ctx.Db.BattleSnapshot.Iter())
        {
            if (snapshot.BattleId == battleId)
            {
                snapshots.Add(snapshot);
            }
        }

        if (snapshots.Count <= keepCount)
        {
            return;
        }

        snapshots.Sort((a, b) =>
        {
            var tickCompare = a.Tick.CompareTo(b.Tick);
            return tickCompare != 0 ? tickCompare : a.SnapshotId.CompareTo(b.SnapshotId);
        });

        var deleteCount = snapshots.Count - keepCount;
        for (var i = 0; i < deleteCount; i++)
        {
            ctx.Db.BattleSnapshot.SnapshotId.Delete(snapshots[i].SnapshotId);
        }
    }

    private static BattleStateBlob BuildInitialBattleState(
        ReducerContext ctx,
        ulong matchId,
        Identity playerA,
        Identity playerB,
        int unitsPerPlayer,
        uint tickRateTps,
        ulong rngState)
    {
        var safeUnitsPerPlayer = Math.Max(1, unitsPerPlayer);
        var totalUnits = safeUnitsPerPlayer * 2;
        var blob = BattleStateBlob.CreateEmpty(totalUnits, rngState);

        var teamAEntries = BuildTeamSpawnEntriesFromLoadout(
            ctx,
            matchId,
            playerA,
            SimConstants.TeamA,
            safeUnitsPerPlayer);
        var teamBEntries = BuildTeamSpawnEntriesFromLoadout(
            ctx,
            matchId,
            playerB,
            SimConstants.TeamB,
            safeUnitsPerPlayer);

        for (var i = 0; i < teamAEntries.Count; i++)
        {
            AddSpawnedUnit(blob, teamAEntries[i], tickRateTps);
        }

        for (var i = 0; i < teamBEntries.Count; i++)
        {
            AddSpawnedUnit(blob, teamBEntries[i], tickRateTps);
        }

        blob.UnitCount = blob.Positions.Count;
        return blob;
    }

    private readonly struct SpawnEntryWithAnchor
    {
        public SpawnEntryWithAnchor(
            int archetypeId,
            byte team,
            Fix64 damageBonus,
            FixVec2 center,
            int cardSlotIndex,
            int cardSpawnCount)
        {
            ArchetypeId = archetypeId;
            Team = team;
            DamageBonus = damageBonus;
            Center = center;
            CardSlotIndex = cardSlotIndex;
            CardSpawnCount = Math.Max(1, cardSpawnCount);
        }

        public int ArchetypeId { get; }
        public byte Team { get; }
        public Fix64 DamageBonus { get; }
        public FixVec2 Center { get; }
        public int CardSlotIndex { get; }
        public int CardSpawnCount { get; }
    }

    private static List<SpawnEntryWithAnchor> BuildTeamSpawnEntriesFromLoadout(
        ReducerContext ctx,
        ulong matchId,
        Identity playerIdentity,
        byte team,
        int maxUnits)
    {
        var cards = GetPlayerCards(ctx, matchId, playerIdentity);
        if (cards.Count == 0)
        {
            throw new Exception($"Player '{playerIdentity}' does not have any card loadout rows.");
        }

        cards.Sort((a, b) =>
        {
            var gridCompare = a.GridIndex.CompareTo(b.GridIndex);
            if (gridCompare != 0)
            {
                return gridCompare;
            }

            var cellZCompare = a.CellZ.CompareTo(b.CellZ);
            if (cellZCompare != 0)
            {
                return cellZCompare;
            }

            var cellXCompare = a.CellX.CompareTo(b.CellX);
            if (cellXCompare != 0)
            {
                return cellXCompare;
            }

            return a.MatchPlayerCardId.CompareTo(b.MatchPlayerCardId);
        });

        var entries = new List<SpawnEntryWithAnchor>(Math.Max(1, maxUnits));
        for (var i = 0; i < cards.Count && entries.Count < maxUnits; i++)
        {
            var cardRow = cards[i];
            if (!SimContentRegistry.TryGetCardByStableId(cardRow.CardStableId, out var card))
            {
                throw new Exception($"Card stable id '{cardRow.CardStableId}' in loadout was not found.");
            }

            ValidateGridPlacementOrThrow(team, cardRow.GridIndex, cardRow.CellX, cardRow.CellZ, card.CardSizeX, card.CardSizeY);
            if (!BakedFlowField.TryResolveCardWorldCenter(
                    cardRow.GridIndex,
                    cardRow.CellX,
                    cardRow.CellZ,
                    card.CardSizeX,
                    card.CardSizeY,
                    out var spawnCenter))
            {
                throw new Exception(
                    $"Card instance '{cardRow.MatchPlayerCardId}' failed to resolve world center from grid placement.");
            }

            var copies = Math.Max(1, cardRow.Quantity);
            var level = Math.Max(1, cardRow.Level);
            var cardDamageBonus = ComputeCardDamageBonus(card, cardRow, level);
            var perCardArchetypes = new List<int>();
            for (var copy = 0; copy < copies && entries.Count < maxUnits; copy++)
            {
                for (var spawnIdx = 0; spawnIdx < card.Spawns.Length && entries.Count < maxUnits; spawnIdx++)
                {
                    var spawn = card.Spawns[spawnIdx];
                    var spawnCount = ComputeSpawnCount(spawn.BaseCount, spawn.GrowthMultiplier, level);
                    for (var count = 0; count < spawnCount && entries.Count < maxUnits; count++)
                    {
                        perCardArchetypes.Add(spawn.UnitArchetypeId);
                    }
                }
            }

            var cardSpawnCount = perCardArchetypes.Count;
            for (var localSlot = 0; localSlot < perCardArchetypes.Count && entries.Count < maxUnits; localSlot++)
            {
                entries.Add(new SpawnEntryWithAnchor(
                    perCardArchetypes[localSlot],
                    team,
                    cardDamageBonus,
                    spawnCenter,
                    localSlot,
                    cardSpawnCount));
            }
        }

        if (entries.Count == 0)
        {
            throw new Exception($"Player '{playerIdentity}' has loadout rows but no valid spawn entries.");
        }

        return entries;
    }

    private static Fix64 ComputeCardDamageBonus(SimCardDefinition card, MatchPlayerCard cardRow, int level)
    {
        var safeLevel = Math.Max(1, level);
        var bonus = card.BaseAttackDamageBonus +
                    (card.AttackDamageBonusPerLevel * Fix64.FromInt(safeLevel - 1));
        for (var i = 0; i < card.Modifiers.Length; i++)
        {
            bonus += card.Modifiers[i].ExtraAttackDamage;
        }

        var runtimeModifiers = cardRow.RuntimeModifiers ?? [];
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
                    $"Card runtime modifier '{runtimeModifier.ModifierStableId}' is invalid for card '{card.StableId}'.");
            }

            bonus += runtimeModifier.ExtraAttackDamage;
        }

        return bonus;
    }

    private static int ComputeSpawnCount(int baseCount, Fix64 growthMultiplier, int level)
    {
        if (baseCount <= 0)
        {
            return 0;
        }

        var safeLevel = Math.Max(1, level);
        var growthLevels = safeLevel - 1;
        var count = Fix64.FromInt(baseCount);
        if (growthLevels > 0)
        {
            var levelScale = Fix64.One + (growthMultiplier * Fix64.FromInt(growthLevels));
            if (levelScale < Fix64.Zero)
            {
                levelScale = Fix64.Zero;
            }

            count *= levelScale;
        }

        if (count <= Fix64.Zero)
        {
            return 0;
        }

        return Math.Max(1, (count + Half).FloorToInt());
    }

    private static void AddSpawnedUnit(
        BattleStateBlob blob,
        SpawnEntryWithAnchor spawn,
        uint tickRateTps)
    {
        var unit = SimContentRegistry.GetUnitByStableId(spawn.ArchetypeId);
        var columns = Math.Max(1, (int)Math.Sqrt(Math.Max(1, spawn.CardSpawnCount)));
        var row = spawn.CardSlotIndex / columns;
        var column = spawn.CardSlotIndex % columns;

        var rowOffset = SpawnSpacing * Fix64.FromInt(row);
        var centeredColumn = (Fix64.FromInt(column) - (Fix64.FromInt(columns - 1) / Fix64.FromInt(2)));
        var columnOffset = centeredColumn * SpawnSpacing;
        var x = spawn.Center.X + rowOffset;
        var y = spawn.Center.Y + columnOffset;

        blob.Positions.Add(new FixVec2(x, y));
        blob.Velocities.Add(FixVec2.Zero);
        blob.Health.Add(unit.MaxHealth);
        blob.ArchetypeIds.Add(spawn.ArchetypeId);
        blob.AttackDamageBonus.Add(spawn.DamageBonus);
        blob.Teams.Add(spawn.Team);
        blob.States.Add(SimConstants.UnitMoving);
        blob.AttackCooldownTicks.Add(0);
        blob.TargetIndices.Add(-1);
        blob.TargetCooldownTicks.Add(0);
        blob.TargetStuckTicks.Add(0);
        blob.TargetLastDistanceSq.Add(Fix64.Zero);
        ResolveInitialStatusState(unit, tickRateTps, out var permanentMask, out var bleedTicks, out var burnTicks, out var slowTicks, out var stunTicks, out var armorBreakTicks);
        blob.StatusPermanentMask.Add(permanentMask);
        blob.StatusBleedTicks.Add(bleedTicks);
        blob.StatusBurnTicks.Add(burnTicks);
        blob.StatusSlowTicks.Add(slowTicks);
        blob.StatusStunTicks.Add(stunTicks);
        blob.StatusArmorBreakTicks.Add(armorBreakTicks);
    }

    private static void ResolveInitialStatusState(
        SimUnitArchetype unit,
        uint tickRateTps,
        out int permanentMask,
        out int bleedTicks,
        out int burnTicks,
        out int slowTicks,
        out int stunTicks,
        out int armorBreakTicks)
    {
        permanentMask = 0;
        bleedTicks = 0;
        burnTicks = 0;
        slowTicks = 0;
        stunTicks = 0;
        armorBreakTicks = 0;

        var statuses = unit.Statuses ?? [];
        for (var i = 0; i < statuses.Length; i++)
        {
            var status = statuses[i];
            var mask = StatusMaskForKind(status.Kind);
            if (mask == 0)
            {
                continue;
            }

            if (status.IsPermanent)
            {
                permanentMask |= mask;
                continue;
            }

            var ticks = ConvertStatusTimeToTicks(status.TimeLeft, tickRateTps);
            if (ticks <= 0)
            {
                continue;
            }

            switch (status.Kind)
            {
                case SimStatusEffectKind.Bleed:
                    bleedTicks = Math.Max(bleedTicks, ticks);
                    break;
                case SimStatusEffectKind.Burn:
                    burnTicks = Math.Max(burnTicks, ticks);
                    break;
                case SimStatusEffectKind.Slow:
                    slowTicks = Math.Max(slowTicks, ticks);
                    break;
                case SimStatusEffectKind.Stun:
                    stunTicks = Math.Max(stunTicks, ticks);
                    break;
                case SimStatusEffectKind.ArmorBreak:
                    armorBreakTicks = Math.Max(armorBreakTicks, ticks);
                    break;
            }
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

    private static void SetPlayerBattleView(
        ReducerContext ctx,
        Identity playerIdentity,
        Identity opponentIdentity,
        Battle battle,
        bool isActive)
    {
        var row = new PlayerBattleView
        {
            PlayerIdentity = playerIdentity,
            MatchId = battle.MatchId,
            RoundId = battle.RoundId,
            BattleId = battle.BattleId,
            OpponentIdentity = opponentIdentity,
            IsActive = isActive,
            UpdatedAt = ctx.Timestamp,
        };

        if (ctx.Db.PlayerBattleView.PlayerIdentity.Find(playerIdentity) is PlayerBattleView existing)
        {
            _ = existing;
            ctx.Db.PlayerBattleView.PlayerIdentity.Update(row);
        }
        else
        {
            ctx.Db.PlayerBattleView.Insert(row);
        }
    }

    private static void ApplyPendingStatusCommands(
        ReducerContext ctx,
        Battle battle,
        BattleStateRuntime runtime)
    {
        var toDelete = new List<ulong>();
        foreach (var command in ctx.Db.BattleStatusCommand.Iter())
        {
            if (command.BattleId != battle.BattleId)
            {
                continue;
            }

            toDelete.Add(command.CommandId);
            if (command.Consumed)
            {
                continue;
            }

            var issuerIsA = command.Issuer == battle.PlayerA;
            var issuerIsB = command.Issuer == battle.PlayerB;
            if (!issuerIsA && !issuerIsB)
            {
                continue;
            }

            if (command.UnitIndex < 0 || command.UnitIndex >= runtime.UnitCount)
            {
                continue;
            }

            if (runtime.States[command.UnitIndex] == SimConstants.UnitDead)
            {
                continue;
            }

            ApplyStatusToRuntime(
                runtime,
                command.UnitIndex,
                (SimStatusEffectKind)command.StatusKind,
                command.IsPermanent,
                command.DurationTicks);
        }

        for (var i = 0; i < toDelete.Count; i++)
        {
            ctx.Db.BattleStatusCommand.CommandId.Delete(toDelete[i]);
        }
    }

    private static void ApplyStatusToRuntime(
        BattleStateRuntime runtime,
        int unitIndex,
        SimStatusEffectKind kind,
        bool isPermanent,
        int durationTicks)
    {
        if (unitIndex < 0 || unitIndex >= runtime.UnitCount)
        {
            return;
        }

        var mask = StatusMaskForKind(kind);
        if (mask == 0)
        {
            return;
        }

        if (isPermanent)
        {
            runtime.StatusPermanentMask[unitIndex] |= mask;
            return;
        }

        if (durationTicks <= 0)
        {
            return;
        }

        switch (kind)
        {
            case SimStatusEffectKind.Bleed:
                runtime.StatusBleedTicks[unitIndex] = Math.Max(runtime.StatusBleedTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.Burn:
                runtime.StatusBurnTicks[unitIndex] = Math.Max(runtime.StatusBurnTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.Slow:
                runtime.StatusSlowTicks[unitIndex] = Math.Max(runtime.StatusSlowTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.Stun:
                runtime.StatusStunTicks[unitIndex] = Math.Max(runtime.StatusStunTicks[unitIndex], durationTicks);
                break;
            case SimStatusEffectKind.ArmorBreak:
                runtime.StatusArmorBreakTicks[unitIndex] = Math.Max(runtime.StatusArmorBreakTicks[unitIndex], durationTicks);
                break;
        }
    }

    private static void ApplyPendingTargetCommands(
        ReducerContext ctx,
        Battle battle,
        BattleStateRuntime runtime)
    {
        var toDelete = new List<ulong>();
        foreach (var command in ctx.Db.BattleCommand.Iter())
        {
            if (command.BattleId != battle.BattleId)
            {
                continue;
            }

            toDelete.Add(command.CommandId);
            if (command.Consumed)
            {
                continue;
            }

            var issuerIsA = command.Issuer == battle.PlayerA;
            var issuerIsB = command.Issuer == battle.PlayerB;
            if (!issuerIsA && !issuerIsB)
            {
                continue;
            }

            if (command.UnitIndex < 0 ||
                command.UnitIndex >= runtime.UnitCount ||
                command.TargetUnitIndex < 0 ||
                command.TargetUnitIndex >= runtime.UnitCount)
            {
                continue;
            }

            var unitIndex = command.UnitIndex;
            var targetIndex = command.TargetUnitIndex;
            if (runtime.States[unitIndex] == SimConstants.UnitDead ||
                runtime.States[targetIndex] == SimConstants.UnitDead)
            {
                continue;
            }

            var expectedTeam = issuerIsA ? SimConstants.TeamA : SimConstants.TeamB;
            if (runtime.Teams[unitIndex] != expectedTeam ||
                runtime.Teams[unitIndex] == runtime.Teams[targetIndex])
            {
                continue;
            }

            runtime.TargetIndices[unitIndex] = targetIndex;
            runtime.TargetCooldownTicks[unitIndex] = 0;
            runtime.TargetStuckTicks[unitIndex] = 0;
            var delta = runtime.Positions[targetIndex] - runtime.Positions[unitIndex];
            runtime.TargetLastDistanceSq[unitIndex] = delta.SqrMagnitude;
            if (runtime.States[unitIndex] != SimConstants.UnitDead)
            {
                runtime.States[unitIndex] = SimConstants.UnitMoving;
            }
        }

        for (var i = 0; i < toDelete.Count; i++)
        {
            ctx.Db.BattleCommand.CommandId.Delete(toDelete[i]);
        }
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
