using System;

public sealed class SimUnitArchetype
{
    public SimUnitArchetype(
        int stableId,
        string id,
        Fix64 maxHealth,
        Fix64 maxSpeed,
        Fix64 collisionRadius,
        Fix64 separationRadius,
        Fix64 attackDamage,
        Fix64 attackRange,
        int attackCooldownTicks)
    {
        StableId = stableId;
        Id = id ?? string.Empty;
        MaxHealth = maxHealth;
        MaxSpeed = maxSpeed;
        CollisionRadius = collisionRadius;
        SeparationRadius = separationRadius;
        AttackDamage = attackDamage;
        AttackRange = attackRange;
        AttackCooldownTicks = Math.Max(1, attackCooldownTicks);
    }

    public int StableId { get; }
    public string Id { get; }
    public Fix64 MaxHealth { get; }
    public Fix64 MaxSpeed { get; }
    public Fix64 CollisionRadius { get; }
    public Fix64 SeparationRadius { get; }
    public Fix64 AttackDamage { get; }
    public Fix64 AttackRange { get; }
    public int AttackCooldownTicks { get; }
}

public readonly struct SimCardSpawnEntry
{
    public SimCardSpawnEntry(int unitArchetypeId, int count)
    {
        UnitArchetypeId = unitArchetypeId;
        Count = count;
    }

    public int UnitArchetypeId { get; }
    public int Count { get; }
}

public sealed class SimCardDefinition
{
    public SimCardDefinition(int stableId, string id, SimCardSpawnEntry[] spawns)
    {
        StableId = stableId;
        Id = id ?? string.Empty;
        Spawns = spawns ?? [];
    }

    public int StableId { get; }
    public string Id { get; }
    public SimCardSpawnEntry[] Spawns { get; }
}
