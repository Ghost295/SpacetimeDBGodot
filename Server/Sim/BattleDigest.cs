internal static class BattleDigest
{
    public static ulong Compute(BattleStateRuntime state)
    {
        const ulong fnvOffset = 1469598103934665603UL;
        const ulong fnvPrime = 1099511628211UL;

        var hash = fnvOffset;
        for (var i = 0; i < state.UnitCount; i++)
        {
            if (state.States[i] == SimConstants.UnitDead)
            {
                continue;
            }

            hash ^= (ulong)state.Positions[i].X.Raw;
            hash *= fnvPrime;
            hash ^= (ulong)state.Positions[i].Y.Raw;
            hash *= fnvPrime;
            hash ^= (ulong)state.Health[i].Raw;
            hash *= fnvPrime;
            hash ^= (ulong)state.Teams[i];
            hash *= fnvPrime;
            hash ^= (ulong)state.ArchetypeIds[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.AttackCooldownTicks[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.StatusPermanentMask[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.StatusBleedTicks[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.StatusBurnTicks[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.StatusSlowTicks[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.StatusStunTicks[i];
            hash *= fnvPrime;
            hash ^= (ulong)(uint)state.StatusArmorBreakTicks[i];
            hash *= fnvPrime;
        }

        hash ^= (ulong)state.Tick;
        hash *= fnvPrime;
        return hash;
    }
}
