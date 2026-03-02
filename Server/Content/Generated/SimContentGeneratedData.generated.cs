public static class SimContentGeneratedData
{
    public const string StaticContentHash = "bootstrap-v1-spearman";

    public static readonly SimUnitArchetype[] Units =
    [
        new SimUnitArchetype(
            stableId: 0,
            id: "spearman",
            maxHealth: Fix64.FromInt(120),
            maxSpeed: Fix64.FromRatio(9, 2),
            collisionRadius: Fix64.FromRatio(3, 5),
            separationRadius: Fix64.FromInt(2),
            attackDamage: Fix64.FromInt(12),
            attackRange: Fix64.FromRatio(5, 2),
            attackCooldownTicks: 18),
        new SimUnitArchetype(
            stableId: 1,
            id: "archer",
            maxHealth: Fix64.FromInt(80),
            maxSpeed: Fix64.FromInt(4),
            collisionRadius: Fix64.FromRatio(1, 2),
            separationRadius: Fix64.FromInt(2),
            attackDamage: Fix64.FromInt(9),
            attackRange: Fix64.FromInt(8),
            attackCooldownTicks: 24),
    ];

    public static readonly SimCardDefinition[] Cards =
    [
        new SimCardDefinition(
            stableId: 0,
            id: "spearman_card",
            spawns:
            [
                new SimCardSpawnEntry(unitArchetypeId: 0, count: 5),
            ]),
        new SimCardDefinition(
            stableId: 1,
            id: "archer_card",
            spawns:
            [
                new SimCardSpawnEntry(unitArchetypeId: 1, count: 4),
            ]),
    ];
}
