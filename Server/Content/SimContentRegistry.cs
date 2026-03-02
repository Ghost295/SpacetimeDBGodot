using System;
using System.Collections.Generic;

public static class SimContentRegistry
{
    private static readonly Dictionary<int, SimUnitArchetype> UnitsByStableId = BuildUnitMap();
    private static readonly Dictionary<int, SimCardDefinition> CardsByStableId = BuildCardMap();

    public static string StaticContentHash => SimContentGeneratedData.StaticContentHash;

    public static SimUnitArchetype GetUnitByStableId(int stableId)
    {
        if (!UnitsByStableId.TryGetValue(stableId, out var unit))
        {
            throw new InvalidOperationException($"Unknown unit archetype stable id '{stableId}'.");
        }

        return unit;
    }

    public static bool TryGetUnitByStableId(int stableId, out SimUnitArchetype unit)
    {
        return UnitsByStableId.TryGetValue(stableId, out unit!);
    }

    public static SimCardDefinition GetCardByStableId(int stableId)
    {
        if (!CardsByStableId.TryGetValue(stableId, out var card))
        {
            throw new InvalidOperationException($"Unknown card stable id '{stableId}'.");
        }

        return card;
    }

    public static bool TryGetCardByStableId(int stableId, out SimCardDefinition card)
    {
        return CardsByStableId.TryGetValue(stableId, out card!);
    }

    public static IReadOnlyCollection<SimUnitArchetype> AllUnits => UnitsByStableId.Values;
    public static IReadOnlyCollection<SimCardDefinition> AllCards => CardsByStableId.Values;

    private static Dictionary<int, SimUnitArchetype> BuildUnitMap()
    {
        var map = new Dictionary<int, SimUnitArchetype>(SimContentGeneratedData.Units.Length);
        for (var i = 0; i < SimContentGeneratedData.Units.Length; i++)
        {
            var unit = SimContentGeneratedData.Units[i];
            map[unit.StableId] = unit;
        }

        return map;
    }

    private static Dictionary<int, SimCardDefinition> BuildCardMap()
    {
        var map = new Dictionary<int, SimCardDefinition>(SimContentGeneratedData.Cards.Length);
        for (var i = 0; i < SimContentGeneratedData.Cards.Length; i++)
        {
            var card = SimContentGeneratedData.Cards[i];
            map[card.StableId] = card;
        }

        return map;
    }
}
