using System.Collections.Generic;

[SpacetimeDB.Type]
public partial struct BattleStateBlob
{
    public int UnitCount;
    public int Tick;
    public ulong RngState;

    public List<FixVec2> Positions;
    public List<FixVec2> Velocities;
    public List<Fix64> Health;
    public List<int> ArchetypeIds;
    public List<Fix64> AttackDamageBonus;
    public List<byte> Teams;
    public List<byte> States;
    public List<int> AttackCooldownTicks;
    public List<int> TargetIndices;
    public List<int> TargetCooldownTicks;
    public List<int> TargetStuckTicks;
    public List<Fix64> TargetLastDistanceSq;
    public List<int> StatusPermanentMask;
    public List<int> StatusBleedTicks;
    public List<int> StatusBurnTicks;
    public List<int> StatusSlowTicks;
    public List<int> StatusStunTicks;
    public List<int> StatusArmorBreakTicks;

    public static BattleStateBlob CreateEmpty(int capacity, ulong rngState)
    {
        return new BattleStateBlob
        {
            UnitCount = 0,
            Tick = 0,
            RngState = rngState,
            Positions = new List<FixVec2>(capacity),
            Velocities = new List<FixVec2>(capacity),
            Health = new List<Fix64>(capacity),
            ArchetypeIds = new List<int>(capacity),
            AttackDamageBonus = new List<Fix64>(capacity),
            Teams = new List<byte>(capacity),
            States = new List<byte>(capacity),
            AttackCooldownTicks = new List<int>(capacity),
            TargetIndices = new List<int>(capacity),
            TargetCooldownTicks = new List<int>(capacity),
            TargetStuckTicks = new List<int>(capacity),
            TargetLastDistanceSq = new List<Fix64>(capacity),
            StatusPermanentMask = new List<int>(capacity),
            StatusBleedTicks = new List<int>(capacity),
            StatusBurnTicks = new List<int>(capacity),
            StatusSlowTicks = new List<int>(capacity),
            StatusStunTicks = new List<int>(capacity),
            StatusArmorBreakTicks = new List<int>(capacity),
        };
    }
}

[SpacetimeDB.Type]
public partial struct BattleSnapshotBlob
{
    public int UnitCount;
    public int Tick;
    public ulong Digest;
    public List<FixVec2> Positions;
    public List<FixVec2> Velocities;
    public List<Fix64> Health;
    public List<byte> Teams;
    public List<byte> States;
    public List<int> ArchetypeIds;
    public List<Fix64> AttackDamageBonus;
    public List<int> StatusPermanentMask;
    public List<int> StatusBleedTicks;
    public List<int> StatusBurnTicks;
    public List<int> StatusSlowTicks;
    public List<int> StatusStunTicks;
    public List<int> StatusArmorBreakTicks;

    public static BattleSnapshotBlob FromState(BattleStateBlob state, ulong digest)
    {
        return new BattleSnapshotBlob
        {
            UnitCount = state.UnitCount,
            Tick = state.Tick,
            Digest = digest,
            Positions = new List<FixVec2>(state.Positions ?? []),
            Velocities = new List<FixVec2>(state.Velocities ?? []),
            Health = new List<Fix64>(state.Health ?? []),
            Teams = new List<byte>(state.Teams ?? []),
            States = new List<byte>(state.States ?? []),
            ArchetypeIds = new List<int>(state.ArchetypeIds ?? []),
            AttackDamageBonus = new List<Fix64>(state.AttackDamageBonus ?? []),
            StatusPermanentMask = new List<int>(state.StatusPermanentMask ?? []),
            StatusBleedTicks = new List<int>(state.StatusBleedTicks ?? []),
            StatusBurnTicks = new List<int>(state.StatusBurnTicks ?? []),
            StatusSlowTicks = new List<int>(state.StatusSlowTicks ?? []),
            StatusStunTicks = new List<int>(state.StatusStunTicks ?? []),
            StatusArmorBreakTicks = new List<int>(state.StatusArmorBreakTicks ?? []),
        };
    }
}
