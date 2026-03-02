# Phase F: Client Subscription Contract

This document defines the implementation contract for client sync during battle.

The server is authoritative for all simulation state. Clients only subscribe, render, and send allowed commands.

## 1) Scope and objectives

- Lock the client contract around `BattleSnapshot` plus minimal `Battle` metadata.
- Define what the client treats as authoritative vs. interpolated/derived.
- Define reducer calls that are allowed from clients during battle.
- Keep subscriptions narrow (no `SubscribeToAllTables` in battle flow).

## 2) Public table contract

### Base (long-lived) subscriptions

Client keeps these active from connect until disconnect:

- `Match`
- `MatchPlayer`
- `MatchRound`
- `MatchPlayerCard` (shop/loadout UX)
- `PlayerBattleView` (battle assignment for each player; local player is filtered in client logic)

### Active-battle (switchable) subscriptions

Client keeps these active only while an active battle exists for the local player:

- `Battle` filtered by active `battle_id`
- `BattleSnapshot` filtered by active `battle_id`

When `PlayerBattleView` changes to a new battle:

1. Unsubscribe prior battle-scoped subscription handle.
2. Subscribe to the new battle-scoped queries.
3. Reset local interpolation buffers for the new battle.

## 3) Authoritative vs interpolated data

### Authoritative (must come from server rows)

- `Battle`: lifecycle/status and metadata (`Status`, `CurrentTick`, winner fields, config fields, `StaticContentHash`, `LastDigest`).
- `BattleSnapshot.Tick` and `BattleSnapshot.Digest`.
- `BattleSnapshot.Snapshot` arrays:
  - position/velocity/health/team/state/archetype
  - `AttackDamageBonus`
  - status state (`StatusPermanentMask`, `StatusBleedTicks`, `StatusBurnTicks`, `StatusSlowTicks`, `StatusStunTicks`, `StatusArmorBreakTicks`)

### Interpolated/derived (client-only presentation)

- Render transform interpolation between two neighboring authoritative snapshots.
- Facing/animation blending derived from snapshot velocity/state.
- UI smoothing (health bar lerp, hit flash timing) that never writes back to authoritative state.

### Not allowed on client

- Running authoritative combat logic.
- Advancing battle ticks locally.
- Inventing local status/health/outcome changes not present in snapshots.

## 4) Allowed reducer calls

### Lobby/shop phase

- `SetMatchPlayerCard`
- `SetMatchPlayerCardWithModifiers`
- `ClearMatchPlayerCard`
- `StartRound`

### During battle

- `SetUnitTarget` is the primary battle input reducer.
- `QueueUnitStatus` is optional and should be treated as debug/admin-only unless explicitly productized.

### Not allowed

- Client-driven tick reducers (`TickBattle`) or direct state mutation reducers.

## 5) Godot client implementation plan

Implement this in `Client/Game/SpacetimeSync.cs` (or a dedicated battle sync service) using two `SubscriptionHandle`s:

- `_baseSubscription`
- `_activeBattleSubscription`

Recommended flow:

1. Connect and register row callbacks first.
2. Create `_baseSubscription` with base queries.
3. Listen to `PlayerBattleView` rows for local player identity.
4. On active battle change, rotate `_activeBattleSubscription` to new `battle_id` queries.
5. On battle end (`IsActive == false` or `Battle.Status == completed`), unsubscribe active battle handle and keep base subscription.
6. On disconnect, unsubscribe any active handles and clear client interpolation caches.

## 6) SQL subscription templates

Use `SubscriptionBuilder().Subscribe(string[] querySqls)` with explicit queries.

Base:

- `SELECT * FROM Match`
- `SELECT * FROM MatchPlayer`
- `SELECT * FROM MatchRound`
- `SELECT * FROM MatchPlayerCard`
- `SELECT * FROM PlayerBattleView`

Active battle (`{battleId}` substituted):

- `SELECT * FROM Battle WHERE battle_id = {battleId}`
- `SELECT * FROM BattleSnapshot WHERE battle_id = {battleId}`

If SQL naming differs in generated bindings, keep the same intent and use generated schema names.

## 7) Snapshot consumption contract

- Assume snapshots are emitted every `Battle.SnapshotEveryNTicks` ticks.
- Keep a small ordered buffer keyed by snapshot tick.
- Render by interpolating between nearest snapshots around render target time.
- If only one snapshot exists, render it directly (no prediction that changes gameplay state).
- Verify content compatibility before battle render:
  - `Match.StaticContentHash` must match local baked content hash.
  - On mismatch: block battle render/input and show a desync/version error.

## 8) Acceptance checklist for Phase F

- Client battle flow no longer uses `SubscribeToAllTables()`.
- Battle rendering path reads from `BattleSnapshot` + minimal `Battle` metadata only.
- Clear separation exists between authoritative state and interpolated presentation.
- During battle, client issues only approved reducers (`SetUnitTarget`, and optionally debug-only `QueueUnitStatus` if exposed).
- Subscription handles are explicitly managed when battle assignment changes.
