public static class SimContentGeneratedData
{
    public const string StaticContentHash = "dc4624efa9604cff99e83c307b407c753f63f3ca0a21eeca0401c95dcbf53658";

    public static readonly SimUnitArchetype[] Units =
    [
        new SimUnitArchetype(
            stableId: 0,
            id: "spearman",
            unitClass: (SimUnitClass)0,
            maxHealth: Fix64.FromRaw(515396075520L),
            maxSpeed: Fix64.FromRaw(42949672960L),
            collisionRadius: Fix64.FromRaw(3435973837L),
            separationRadius: Fix64.FromRaw(12884901888L),
            attackDamage: Fix64.FromRaw(51539607552L),
            attackRange: Fix64.FromRaw(10737418240L),
            attackCooldownTicks: 18,
            areaDamage: Fix64.FromRaw(0L),
            armor: Fix64.FromRaw(4294967296L),
            siegeDamage: Fix64.FromRaw(0L),
            abilities:
            [
                new SimUnitAbilityDefinition(
                    type: (SimAbilityType)1,
                    attackDamage: Fix64.FromRaw(51539607552L),
                    attackRange: Fix64.FromRaw(10737418240L),
                    attackSpeed: Fix64.FromRaw(4772185884L),
                    bonuses:
                    [
                        new SimUnitBonusDefinition(targetClass: (SimBonusTargetClass)3, amount: Fix64.FromRaw(12884901888L), isMultiplier: false),
                    ],
                    statuses:
                    [
                    ]),
            ],
            statuses:
            [
            ]),
        new SimUnitArchetype(
            stableId: 1,
            id: "archer",
            unitClass: (SimUnitClass)2,
            maxHealth: Fix64.FromRaw(343597383680L),
            maxSpeed: Fix64.FromRaw(17179869184L),
            collisionRadius: Fix64.FromRaw(2147483648L),
            separationRadius: Fix64.FromRaw(8589934592L),
            attackDamage: Fix64.FromRaw(38654705664L),
            attackRange: Fix64.FromRaw(34359738368L),
            attackCooldownTicks: 24,
            areaDamage: Fix64.FromRaw(0L),
            armor: Fix64.FromRaw(0L),
            siegeDamage: Fix64.FromRaw(0L),
            abilities:
            [
                new SimUnitAbilityDefinition(
                    type: (SimAbilityType)4,
                    attackDamage: Fix64.FromRaw(38654705664L),
                    attackRange: Fix64.FromRaw(34359738368L),
                    attackSpeed: Fix64.FromRaw(3579139412L),
                    bonuses:
                    [
                        new SimUnitBonusDefinition(targetClass: (SimBonusTargetClass)1, amount: Fix64.FromRaw(8589934592L), isMultiplier: false),
                    ],
                    statuses:
                    [
                    ]),
            ],
            statuses:
            [
            ]),
    ];

    public static readonly SimCardDefinition[] Cards =
    [
        new SimCardDefinition(
            stableId: 0,
            id: "spearman_card",
            priceGold: 3,
            cardSizeX: 1,
            cardSizeY: 1,
            baseAttackDamageBonus: Fix64.FromRaw(0L),
            attackDamageBonusPerLevel: Fix64.FromRaw(8589934592L),
            spawns:
            [
                new SimCardSpawnEntry(unitArchetypeId: 0, baseCount: 10, growthMultiplier: Fix64.FromRaw(0L)),
            ],
            modifiers:
            [
                new SimCardModifierDefinition(modifierStableId: 0, extraAttackDamage: Fix64.FromRaw(0L)),
            ]),
        new SimCardDefinition(
            stableId: 1,
            id: "archer_card",
            priceGold: 3,
            cardSizeX: 1,
            cardSizeY: 1,
            baseAttackDamageBonus: Fix64.FromRaw(0L),
            attackDamageBonusPerLevel: Fix64.FromRaw(4294967296L),
            spawns:
            [
                new SimCardSpawnEntry(unitArchetypeId: 1, baseCount: 10, growthMultiplier: Fix64.FromRaw(0L)),
            ],
            modifiers:
            [
                new SimCardModifierDefinition(modifierStableId: 0, extraAttackDamage: Fix64.FromRaw(0L)),
            ]),
    ];
}
