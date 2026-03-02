#!/usr/bin/env python3
"""
Deterministic content baker for SimContentGeneratedData.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, List


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Bake server content to generated C#.")
    parser.add_argument("--input", required=True, help="Input JSON file path.")
    parser.add_argument("--output", required=True, help="Output .generated.cs file path.")
    return parser.parse_args()


def load_json(path: Path) -> Dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("Top-level JSON must be an object.")
    if "units" not in data or "cards" not in data:
        raise ValueError("JSON must contain 'units' and 'cards'.")
    if not isinstance(data["units"], list) or not isinstance(data["cards"], list):
        raise ValueError("'units' and 'cards' must be arrays.")
    return data


def to_bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        normalized = value.strip().lower()
        return normalized in ("1", "true", "yes", "y", "on")
    return bool(value)


def to_csharp_bool(value: Any) -> str:
    return "true" if to_bool(value) else "false"


def canonicalize(data: Dict[str, Any]) -> Dict[str, Any]:
    units = sorted(data["units"], key=lambda x: int(x["stable_id"]))
    for unit in units:
        unit.setdefault("unit_class", 0)
        unit.setdefault("area_damage", 0)
        unit.setdefault("armor", 0)
        unit.setdefault("siege_damage", 0)

        statuses = unit.get("statuses", [])
        for status in statuses:
            status.setdefault("kind", 0)
            status.setdefault("is_permanent", False)
            status.setdefault("time_left", 0)
        unit["statuses"] = sorted(
            statuses,
            key=lambda x: (
                int(x.get("kind", 0)),
                to_bool(x.get("is_permanent", False)),
                float(x.get("time_left", 0)),
            ),
        )

        abilities = unit.get("abilities", [])
        for ability in abilities:
            ability.setdefault("type", 0)
            ability.setdefault("attack_damage", 0)
            ability.setdefault("attack_range", 0)
            ability.setdefault("attack_speed", 1)

            bonuses = ability.get("bonuses", [])
            for bonus in bonuses:
                bonus.setdefault("target_class", 0)
                bonus.setdefault("amount", 0)
                bonus.setdefault("is_multiplier", False)
            ability["bonuses"] = sorted(
                bonuses,
                key=lambda x: (
                    int(x.get("target_class", 0)),
                    to_bool(x.get("is_multiplier", False)),
                    float(x.get("amount", 0)),
                ),
            )

            ability_statuses = ability.get("statuses", [])
            for status in ability_statuses:
                status.setdefault("kind", 0)
                status.setdefault("is_permanent", False)
                status.setdefault("time_left", 0)
            ability["statuses"] = sorted(
                ability_statuses,
                key=lambda x: (
                    int(x.get("kind", 0)),
                    to_bool(x.get("is_permanent", False)),
                    float(x.get("time_left", 0)),
                ),
            )

        unit["abilities"] = sorted(
            abilities,
            key=lambda x: (
                int(x.get("type", 0)),
                float(x.get("attack_range", 0)),
                float(x.get("attack_damage", 0)),
                float(x.get("attack_speed", 1)),
                len(x.get("bonuses", [])),
                len(x.get("statuses", [])),
            ),
        )

    cards = sorted(data["cards"], key=lambda x: int(x["stable_id"]))
    for card in cards:
        card.setdefault("price_gold", 0)
        card.setdefault("card_size_x", 1)
        card.setdefault("card_size_y", 1)
        card.setdefault("base_attack_damage_bonus", 0)
        card.setdefault("attack_damage_bonus_per_level", 0)

        modifiers = card.get("modifiers", [])
        card["modifiers"] = sorted(modifiers, key=lambda x: int(x["stable_id"]))

        spawns = card.get("spawns", [])
        for spawn in spawns:
            spawn.setdefault("growth_multiplier", 0)
        card["spawns"] = sorted(
            spawns,
            key=lambda x: (
                int(x["unit_archetype_id"]),
                int(x["base_count"]),
                float(x.get("growth_multiplier", 0)),
            ),
        )
    return {"units": units, "cards": cards}


def build_hash(canonical: Dict[str, Any]) -> str:
    canonical_json = json.dumps(canonical, separators=(",", ":"), sort_keys=True)
    return hashlib.sha256(canonical_json.encode("utf-8")).hexdigest()


def to_fix64_expr(value: float) -> str:
    scaled = int(round(float(value) * (1 << 32)))
    return f"Fix64.FromRaw({scaled}L)"


def format_unit_abilities(abilities: List[Dict[str, Any]]) -> str:
    lines: List[str] = []
    for ability in abilities:
        statuses_text = format_ability_statuses(ability.get("statuses", []))
        lines.extend(
            [
                "                new SimUnitAbilityDefinition(",
                f"                    type: (SimAbilityType){int(ability.get('type', 0))},",
                f"                    attackDamage: {to_fix64_expr(ability.get('attack_damage', 0))},",
                f"                    attackRange: {to_fix64_expr(ability.get('attack_range', 0))},",
                f"                    attackSpeed: {to_fix64_expr(ability.get('attack_speed', 1))},",
                "                    bonuses:",
                "                    [",
            ]
        )
        for bonus in ability.get("bonuses", []):
            lines.append(
                "                        new SimUnitBonusDefinition("
                f"targetClass: (SimBonusTargetClass){int(bonus.get('target_class', 0))}, "
                f"amount: {to_fix64_expr(bonus.get('amount', 0))}, "
                f"isMultiplier: {to_csharp_bool(bonus.get('is_multiplier', False))}),"
            )
        lines.extend(["                    ],", "                    statuses:", "                    ["])
        if statuses_text:
            lines.append(statuses_text)
        lines.extend(["                    ]),"])
    return "\n".join(lines)


def format_ability_statuses(statuses: List[Dict[str, Any]]) -> str:
    lines: List[str] = []
    for status in statuses:
        lines.extend(
            [
                "                        new SimUnitStatusEffectDefinition(",
                f"                            kind: (SimStatusEffectKind){int(status.get('kind', 0))},",
                f"                            isPermanent: {to_csharp_bool(status.get('is_permanent', False))},",
                f"                            timeLeft: {to_fix64_expr(status.get('time_left', 0))}),",
            ]
        )
    return "\n".join(lines)


def format_unit_statuses(statuses: List[Dict[str, Any]]) -> str:
    lines: List[str] = []
    for status in statuses:
        lines.extend(
            [
                "                new SimUnitStatusEffectDefinition(",
                f"                    kind: (SimStatusEffectKind){int(status.get('kind', 0))},",
                f"                    isPermanent: {to_csharp_bool(status.get('is_permanent', False))},",
                f"                    timeLeft: {to_fix64_expr(status.get('time_left', 0))}),",
            ]
        )
    return "\n".join(lines)


def format_units(units: List[Dict[str, Any]]) -> str:
    lines: List[str] = []
    for unit in units:
        abilities_text = format_unit_abilities(unit.get("abilities", []))
        statuses_text = format_unit_statuses(unit.get("statuses", []))
        lines.extend(
            [
                "        new SimUnitArchetype(",
                f"            stableId: {int(unit['stable_id'])},",
                f"            id: \"{unit['id']}\",",
                f"            unitClass: (SimUnitClass){int(unit.get('unit_class', 0))},",
                f"            maxHealth: {to_fix64_expr(unit['max_health'])},",
                f"            maxSpeed: {to_fix64_expr(unit['max_speed'])},",
                f"            collisionRadius: {to_fix64_expr(unit['collision_radius'])},",
                f"            separationRadius: {to_fix64_expr(unit['separation_radius'])},",
                f"            attackDamage: {to_fix64_expr(unit['attack_damage'])},",
                f"            attackRange: {to_fix64_expr(unit['attack_range'])},",
                f"            attackCooldownTicks: {int(unit['attack_cooldown_ticks'])},",
                f"            areaDamage: {to_fix64_expr(unit.get('area_damage', 0))},",
                f"            armor: {to_fix64_expr(unit.get('armor', 0))},",
                f"            siegeDamage: {to_fix64_expr(unit.get('siege_damage', 0))},",
                "            abilities:",
                "            [",
            ]
        )
        if abilities_text:
            lines.append(abilities_text)
        lines.extend(["            ],", "            statuses:", "            ["])
        if statuses_text:
            lines.append(statuses_text)
        lines.extend(["            ]),"])
    return "\n".join(lines)


def format_cards(cards: List[Dict[str, Any]]) -> str:
    lines: List[str] = []
    for card in cards:
        lines.extend(
            [
                "        new SimCardDefinition(",
                f"            stableId: {int(card['stable_id'])},",
                f"            id: \"{card['id']}\",",
                f"            priceGold: {int(card['price_gold'])},",
                f"            cardSizeX: {int(card['card_size_x'])},",
                f"            cardSizeY: {int(card['card_size_y'])},",
                "            baseAttackDamageBonus: "
                f"{to_fix64_expr(card.get('base_attack_damage_bonus', 0))},",
                "            attackDamageBonusPerLevel: "
                f"{to_fix64_expr(card.get('attack_damage_bonus_per_level', 0))},",
                "            spawns:",
                "            [",
            ]
        )
        for spawn in card.get("spawns", []):
            lines.append(
                "                new SimCardSpawnEntry("
                f"unitArchetypeId: {int(spawn['unit_archetype_id'])}, "
                f"baseCount: {int(spawn['base_count'])}, "
                f"growthMultiplier: {to_fix64_expr(spawn.get('growth_multiplier', 0))}),"
            )
        lines.extend(["            ],", "            modifiers:", "            ["])
        for modifier in card.get("modifiers", []):
            lines.append(
                "                new SimCardModifierDefinition("
                f"modifierStableId: {int(modifier['stable_id'])}, "
                f"extraAttackDamage: {to_fix64_expr(modifier.get('extra_attack_damage', 0))}),"
            )
        lines.extend(["            ]),"])
    return "\n".join(lines)


def generate_source(canonical: Dict[str, Any], static_hash: str) -> str:
    units_text = format_units(canonical["units"])
    cards_text = format_cards(canonical["cards"])
    return f"""public static class SimContentGeneratedData
{{
    public const string StaticContentHash = "{static_hash}";

    public static readonly SimUnitArchetype[] Units =
    [
{units_text}
    ];

    public static readonly SimCardDefinition[] Cards =
    [
{cards_text}
    ];
}}
"""


def main() -> None:
    args = parse_args()
    input_path = Path(args.input)
    output_path = Path(args.output)

    data = load_json(input_path)
    canonical = canonicalize(data)
    static_hash = build_hash(canonical)
    source = generate_source(canonical, static_hash)
    output_path.write_text(source, encoding="utf-8", newline="\n")
    print(f"Wrote {output_path} with hash {static_hash}")


if __name__ == "__main__":
    main()
