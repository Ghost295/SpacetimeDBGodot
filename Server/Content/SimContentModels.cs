using System;

public enum SimUnitClass : byte
{
    LightInfantry = 0,
    HeavyInfantry = 1,
    Archer = 2,
    LightCavalry = 3,
    HeavyCavalry = 4,
    Siege = 5,
    Support = 6,
    Caster = 7,
}

public enum SimAbilityType : byte
{
    None = 0,
    Spear = 1,
    Sword = 2,
    Mangonel = 3,
    Crossbow = 4,
}

public enum SimBonusTargetClass : byte
{
    Any = 0,
    Infantry = 1,
    Archer = 2,
    Cavalry = 3,
    Friendly = 4,
    Enemy = 5,
}

public enum SimStatusEffectKind : byte
{
    None = 0,
    Bleed = 1,
    Burn = 2,
    Slow = 3,
    Stun = 4,
    ArmorBreak = 5,
}

public readonly struct SimUnitBonusDefinition
{
    public SimUnitBonusDefinition(SimBonusTargetClass targetClass, Fix64 amount, bool isMultiplier)
    {
        TargetClass = targetClass;
        Amount = amount;
        IsMultiplier = isMultiplier;
    }

    public SimBonusTargetClass TargetClass { get; }
    public Fix64 Amount { get; }
    public bool IsMultiplier { get; }
}

public readonly struct SimUnitAbilityDefinition
{
    public SimUnitAbilityDefinition(
        SimAbilityType type,
        Fix64 attackDamage,
        Fix64 attackRange,
        Fix64 attackSpeed,
        SimUnitBonusDefinition[] bonuses,
        SimUnitStatusEffectDefinition[] statuses)
    {
        Type = type;
        AttackDamage = attackDamage;
        AttackRange = attackRange;
        AttackSpeed = attackSpeed;
        Bonuses = bonuses ?? [];
        Statuses = statuses ?? [];
    }

    public SimAbilityType Type { get; }
    public Fix64 AttackDamage { get; }
    public Fix64 AttackRange { get; }
    public Fix64 AttackSpeed { get; }
    public SimUnitBonusDefinition[] Bonuses { get; }
    public SimUnitStatusEffectDefinition[] Statuses { get; }
}

public readonly struct SimUnitStatusEffectDefinition
{
    public SimUnitStatusEffectDefinition(SimStatusEffectKind kind, bool isPermanent, Fix64 timeLeft)
    {
        Kind = kind;
        IsPermanent = isPermanent;
        TimeLeft = timeLeft;
    }

    public SimStatusEffectKind Kind { get; }
    public bool IsPermanent { get; }
    public Fix64 TimeLeft { get; }
}

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
        : this(
            stableId,
            id,
            SimUnitClass.LightInfantry,
            maxHealth,
            maxSpeed,
            collisionRadius,
            separationRadius,
            attackDamage,
            attackRange,
            attackCooldownTicks,
            Fix64.Zero,
            Fix64.Zero,
            Fix64.Zero,
            [],
            [])
    {
    }

    public SimUnitArchetype(
        int stableId,
        string id,
        SimUnitClass unitClass,
        Fix64 maxHealth,
        Fix64 maxSpeed,
        Fix64 collisionRadius,
        Fix64 separationRadius,
        Fix64 attackDamage,
        Fix64 attackRange,
        int attackCooldownTicks,
        Fix64 areaDamage,
        Fix64 armor,
        Fix64 siegeDamage,
        SimUnitAbilityDefinition[] abilities,
        SimUnitStatusEffectDefinition[] statuses)
    {
        StableId = stableId;
        Id = id ?? string.Empty;
        UnitClass = unitClass;
        MaxHealth = maxHealth;
        MaxSpeed = maxSpeed;
        CollisionRadius = collisionRadius;
        SeparationRadius = separationRadius;
        AttackDamage = attackDamage;
        AttackRange = attackRange;
        AttackCooldownTicks = Math.Max(1, attackCooldownTicks);
        AreaDamage = areaDamage;
        Armor = armor;
        SiegeDamage = siegeDamage;
        Abilities = abilities ?? [];
        Statuses = statuses ?? [];
    }

    public int StableId { get; }
    public string Id { get; }
    public SimUnitClass UnitClass { get; }
    public Fix64 MaxHealth { get; }
    public Fix64 MaxSpeed { get; }
    public Fix64 CollisionRadius { get; }
    public Fix64 SeparationRadius { get; }
    public Fix64 AttackDamage { get; }
    public Fix64 AttackRange { get; }
    public int AttackCooldownTicks { get; }
    public Fix64 AreaDamage { get; }
    public Fix64 Armor { get; }
    public Fix64 SiegeDamage { get; }
    public SimUnitAbilityDefinition[] Abilities { get; }
    public SimUnitStatusEffectDefinition[] Statuses { get; }
}

public readonly struct SimCardSpawnEntry
{
    public SimCardSpawnEntry(int unitArchetypeId, int baseCount, Fix64 growthMultiplier)
    {
        UnitArchetypeId = unitArchetypeId;
        BaseCount = Math.Max(0, baseCount);
        GrowthMultiplier = growthMultiplier;
    }

    public int UnitArchetypeId { get; }
    public int BaseCount { get; }
    public Fix64 GrowthMultiplier { get; }
}

public readonly struct SimCardModifierDefinition
{
    public SimCardModifierDefinition(int modifierStableId, Fix64 extraAttackDamage)
    {
        ModifierStableId = modifierStableId;
        ExtraAttackDamage = extraAttackDamage;
    }

    public int ModifierStableId { get; }
    public Fix64 ExtraAttackDamage { get; }
}

public sealed class SimCardDefinition
{
    public SimCardDefinition(
        int stableId,
        string id,
        int priceGold,
        int cardSizeX,
        int cardSizeY,
        Fix64 baseAttackDamageBonus,
        Fix64 attackDamageBonusPerLevel,
        SimCardSpawnEntry[] spawns,
        SimCardModifierDefinition[] modifiers)
    {
        StableId = stableId;
        Id = id ?? string.Empty;
        PriceGold = Math.Max(0, priceGold);
        CardSizeX = Math.Max(1, cardSizeX);
        CardSizeY = Math.Max(1, cardSizeY);
        BaseAttackDamageBonus = baseAttackDamageBonus;
        AttackDamageBonusPerLevel = attackDamageBonusPerLevel;
        Spawns = spawns ?? [];
        Modifiers = modifiers ?? [];
    }

    public int StableId { get; }
    public string Id { get; }
    public int PriceGold { get; }
    public int CardSizeX { get; }
    public int CardSizeY { get; }
    public Fix64 BaseAttackDamageBonus { get; }
    public Fix64 AttackDamageBonusPerLevel { get; }
    public SimCardSpawnEntry[] Spawns { get; }
    public SimCardModifierDefinition[] Modifiers { get; }
}
