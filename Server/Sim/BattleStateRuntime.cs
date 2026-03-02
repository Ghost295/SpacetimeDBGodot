using System;
using System.Collections.Generic;

internal sealed class BattleStateRuntime
{
    public int UnitCount;
    public int Tick;
    public ulong RngState;

    public FixVec2[] Positions;
    public FixVec2[] Velocities;
    public Fix64[] Health;
    public int[] ArchetypeIds;
    public Fix64[] AttackDamageBonus;
    public byte[] Teams;
    public byte[] States;
    public int[] AttackCooldownTicks;
    public int[] TargetIndices;
    public int[] TargetCooldownTicks;
    public int[] TargetStuckTicks;
    public Fix64[] TargetLastDistanceSq;
    public int[] StatusPermanentMask;
    public int[] StatusBleedTicks;
    public int[] StatusBurnTicks;
    public int[] StatusSlowTicks;
    public int[] StatusStunTicks;
    public int[] StatusArmorBreakTicks;

    public BattleStateRuntime(int unitCount)
    {
        UnitCount = Math.Max(0, unitCount);
        Tick = 0;
        RngState = 0;
        Positions = new FixVec2[UnitCount];
        Velocities = new FixVec2[UnitCount];
        Health = new Fix64[UnitCount];
        ArchetypeIds = new int[UnitCount];
        AttackDamageBonus = new Fix64[UnitCount];
        Teams = new byte[UnitCount];
        States = new byte[UnitCount];
        AttackCooldownTicks = new int[UnitCount];
        TargetIndices = new int[UnitCount];
        TargetCooldownTicks = new int[UnitCount];
        TargetStuckTicks = new int[UnitCount];
        TargetLastDistanceSq = new Fix64[UnitCount];
        StatusPermanentMask = new int[UnitCount];
        StatusBleedTicks = new int[UnitCount];
        StatusBurnTicks = new int[UnitCount];
        StatusSlowTicks = new int[UnitCount];
        StatusStunTicks = new int[UnitCount];
        StatusArmorBreakTicks = new int[UnitCount];
    }

    public static BattleStateRuntime FromBlob(BattleStateBlob blob)
    {
        var unitCount = ResolveCount(blob);
        var runtime = new BattleStateRuntime(unitCount)
        {
            Tick = blob.Tick,
            RngState = blob.RngState,
        };

        for (var i = 0; i < unitCount; i++)
        {
            runtime.Positions[i] = blob.Positions![i];
            runtime.Velocities[i] = blob.Velocities![i];
            runtime.Health[i] = blob.Health![i];
            runtime.ArchetypeIds[i] = blob.ArchetypeIds![i];
            runtime.AttackDamageBonus[i] = blob.AttackDamageBonus![i];
            runtime.Teams[i] = blob.Teams![i];
            runtime.States[i] = blob.States![i];
            runtime.AttackCooldownTicks[i] = blob.AttackCooldownTicks![i];
            runtime.TargetIndices[i] = blob.TargetIndices![i];
            runtime.TargetCooldownTicks[i] = blob.TargetCooldownTicks![i];
            runtime.TargetStuckTicks[i] = blob.TargetStuckTicks![i];
            runtime.TargetLastDistanceSq[i] = blob.TargetLastDistanceSq![i];
            runtime.StatusPermanentMask[i] = GetOrDefault(blob.StatusPermanentMask, i);
            runtime.StatusBleedTicks[i] = GetOrDefault(blob.StatusBleedTicks, i);
            runtime.StatusBurnTicks[i] = GetOrDefault(blob.StatusBurnTicks, i);
            runtime.StatusSlowTicks[i] = GetOrDefault(blob.StatusSlowTicks, i);
            runtime.StatusStunTicks[i] = GetOrDefault(blob.StatusStunTicks, i);
            runtime.StatusArmorBreakTicks[i] = GetOrDefault(blob.StatusArmorBreakTicks, i);
        }

        return runtime;
    }

    public BattleStateBlob ToBlob()
    {
        var positions = new List<FixVec2>(UnitCount);
        var velocities = new List<FixVec2>(UnitCount);
        var health = new List<Fix64>(UnitCount);
        var archetypeIds = new List<int>(UnitCount);
        var attackDamageBonus = new List<Fix64>(UnitCount);
        var teams = new List<byte>(UnitCount);
        var states = new List<byte>(UnitCount);
        var cooldowns = new List<int>(UnitCount);
        var targets = new List<int>(UnitCount);
        var targetCooldowns = new List<int>(UnitCount);
        var targetStuckTicks = new List<int>(UnitCount);
        var targetLastDistSq = new List<Fix64>(UnitCount);
        var statusPermanentMask = new List<int>(UnitCount);
        var statusBleedTicks = new List<int>(UnitCount);
        var statusBurnTicks = new List<int>(UnitCount);
        var statusSlowTicks = new List<int>(UnitCount);
        var statusStunTicks = new List<int>(UnitCount);
        var statusArmorBreakTicks = new List<int>(UnitCount);

        for (var i = 0; i < UnitCount; i++)
        {
            positions.Add(Positions[i]);
            velocities.Add(Velocities[i]);
            health.Add(Health[i]);
            archetypeIds.Add(ArchetypeIds[i]);
            attackDamageBonus.Add(AttackDamageBonus[i]);
            teams.Add(Teams[i]);
            states.Add(States[i]);
            cooldowns.Add(AttackCooldownTicks[i]);
            targets.Add(TargetIndices[i]);
            targetCooldowns.Add(TargetCooldownTicks[i]);
            targetStuckTicks.Add(TargetStuckTicks[i]);
            targetLastDistSq.Add(TargetLastDistanceSq[i]);
            statusPermanentMask.Add(StatusPermanentMask[i]);
            statusBleedTicks.Add(StatusBleedTicks[i]);
            statusBurnTicks.Add(StatusBurnTicks[i]);
            statusSlowTicks.Add(StatusSlowTicks[i]);
            statusStunTicks.Add(StatusStunTicks[i]);
            statusArmorBreakTicks.Add(StatusArmorBreakTicks[i]);
        }

        return new BattleStateBlob
        {
            UnitCount = UnitCount,
            Tick = Tick,
            RngState = RngState,
            Positions = positions,
            Velocities = velocities,
            Health = health,
            ArchetypeIds = archetypeIds,
            AttackDamageBonus = attackDamageBonus,
            Teams = teams,
            States = states,
            AttackCooldownTicks = cooldowns,
            TargetIndices = targets,
            TargetCooldownTicks = targetCooldowns,
            TargetStuckTicks = targetStuckTicks,
            TargetLastDistanceSq = targetLastDistSq,
            StatusPermanentMask = statusPermanentMask,
            StatusBleedTicks = statusBleedTicks,
            StatusBurnTicks = statusBurnTicks,
            StatusSlowTicks = statusSlowTicks,
            StatusStunTicks = statusStunTicks,
            StatusArmorBreakTicks = statusArmorBreakTicks,
        };
    }

    public int CountAlive(byte team)
    {
        var count = 0;
        for (var i = 0; i < UnitCount; i++)
        {
            if (Teams[i] == team && States[i] != SimConstants.UnitDead)
            {
                count++;
            }
        }

        return count;
    }

    private static int ResolveCount(BattleStateBlob blob)
    {
        var lengths = new[]
        {
            blob.UnitCount,
            blob.Positions?.Count ?? 0,
            blob.Velocities?.Count ?? 0,
            blob.Health?.Count ?? 0,
            blob.ArchetypeIds?.Count ?? 0,
            blob.AttackDamageBonus?.Count ?? 0,
            blob.Teams?.Count ?? 0,
            blob.States?.Count ?? 0,
            blob.AttackCooldownTicks?.Count ?? 0,
            blob.TargetIndices?.Count ?? 0,
            blob.TargetCooldownTicks?.Count ?? 0,
            blob.TargetStuckTicks?.Count ?? 0,
            blob.TargetLastDistanceSq?.Count ?? 0,
        };

        var count = int.MaxValue;
        for (var i = 0; i < lengths.Length; i++)
        {
            count = Math.Min(count, Math.Max(0, lengths[i]));
        }

        return count == int.MaxValue ? 0 : count;
    }

    private static int GetOrDefault(List<int>? values, int index)
    {
        if (values == null || index < 0 || index >= values.Count)
        {
            return 0;
        }

        return values[index];
    }
}
