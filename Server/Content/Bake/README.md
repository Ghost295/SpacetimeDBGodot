# Content Bake Pipeline

This folder contains an external deterministic bake step for server content.

## Goal

Generate `Server/Content/Generated/SimContentGeneratedData.generated.cs` from a canonical JSON file so the SpacetimeDB module does not depend on Godot `Resource` loading.

## Inputs

- JSON file matching `content.template.json` shape:
  - `units` with deterministic stats and authored combat payload:
    - base movement/combat stats (`max_health`, `max_speed`, `collision_radius`, `separation_radius`, `attack_damage`, `attack_range`, `attack_cooldown_ticks`)
    - class/stats (`unit_class`, `area_damage`, `armor`, `siege_damage`)
    - `abilities` entries with:
      - `type`
      - `attack_damage`, `attack_range`, `attack_speed`
      - nested `bonuses` entries (`target_class`, `amount`, `is_multiplier`)
      - optional nested `statuses` entries (`kind`, `is_permanent`, `time_left`) applied on hit
    - `statuses` entries (`kind`, `is_permanent`, `time_left`)
  - `cards` with deterministic metadata and spawn entries:
    - `price_gold`
    - `card_size_x`, `card_size_y`
    - `spawns` entries with `unit_archetype_id`, `base_count`, `growth_multiplier`
    - `modifiers` entries with `stable_id`, `extra_attack_damage`
      - runtime card modifier payload must reference one of these baked `stable_id` values
  - optional card combat bonus fields:
    - `base_attack_damage_bonus`
    - `attack_damage_bonus_per_level`

## Generate

Run from repo root:

```powershell
python Server/Content/Bake/bake_content.py `
  --input Server/Content/Bake/content.template.json `
  --output Server/Content/Generated/SimContentGeneratedData.generated.cs
```

## Determinism guarantees

- Input is canonicalized (sorted and compacted) before hash generation.
- `StaticContentHash` is SHA-256 (hex) of canonical JSON.
- Output ordering is stable:
  - Units sorted by `stable_id`
- Unit abilities sorted by `(type, attack_range, attack_damage, attack_speed, bonuses.Length, statuses.Length)`
  - Unit ability bonuses sorted by `(target_class, is_multiplier, amount)`
  - Unit ability statuses sorted by `(kind, is_permanent, time_left)`
  - Unit statuses sorted by `(kind, is_permanent, time_left)`
  - Cards sorted by `stable_id`
  - Card spawns sorted by `(unit_archetype_id, base_count, growth_multiplier)`

## Current deterministic combat usage

- Unit combat profile currently consumes:
  - ability selection by range (`abilities`)
  - typed ability bonuses (`bonuses`)
  - non-basic unit damage fields (`area_damage`, `siege_damage`)
  - target mitigation (`armor`)
- Status behavior currently applied in server combat:
  - `Bleed` -> deterministic damage-over-time per tick
  - `Burn` -> deterministic damage-over-time per tick
  - `Slow` -> reduced max move speed
  - `Stun` -> no movement and no attacks
  - `ArmorBreak` -> partial target armor mitigation
- Unit-ability statuses are applied deterministically when attacks land.
- Non-permanent statuses are converted to runtime tick counters at battle start and decremented each simulation tick.
- Other status kinds are baked but currently treated as data-only for future expansion.
