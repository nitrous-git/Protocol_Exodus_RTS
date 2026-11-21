# Protocol Exodus RTS — Enemy AI Architecture

**Status:** Design reference / staged implementation plan  
**Unity project:** `https://github.com/nitrous-git/Protocol_Exodus_RTS` (`main`)  
**Java reference:** `https://github.com/nitrous-git/RTS-Prototype/tree/AI_Behavior01/src` (`AI_Behavior01`)

> This is a concise reference for MCP implementation tasks, **not** an instruction to implement every phase at once. Read the repository `AGENTS.md` first. Inspect live code before making changes; code and Unity assets may have evolved.

## 1. Design goal

Build a continuous, compelling, *StarCraft-like* enemy faction: it develops an economy and base, produces units, defends itself, assembles coordinated forces, conducts offensives, responds to losses, and continues acting for the entire match.

**Core rule:** Keep **strategic intent** separate from **mission/group coordination**, **individual tactical choices**, and the **existing RTS gameplay mechanics**.

```text
AIFactionController / Faction Strategy
  Economy, infrastructure, production, reserves, strategic priorities
          |
          v
Mission Director (future A2–A4)
  Recruit forces, choose operations/targets, track outcome
          |
          v
Squad / Group Mission (future A2)
  Staging, formation, travel, engagement, losses, return/reassignment
          |
          v
Individual Combat Unit AI (A1)
  Mission intent, local threats, target choice, pursuit, retreat
          |
          v
Existing gameplay systems (do not duplicate)
  Unit commands/states, sensors, weapons, movement, destination allocation,
  health, construction, production, resource gathering, resource/supply rules
```

No permanent scripted attack sequence. An optional opening build order may use timings, but later behavior should depend on actual game state and remain active throughout the match.

## 2. Java `AI_Behavior01`: what it does and what to learn

### Strategic scheduling

- `src/Faction/AIController.java` owns an `EnemyAI/AIManager.java` for each AI faction.
- `AIManager.scheduleActions()` schedules: CC at **1.2s**; workers at **10–12s**; Barracks at **24s**; combat units at **30–32s**; 1-unit wave at **70s**; more units at **74–77s**; Supply Depot at **90s**; 4-unit wave at **120s**; more units at **145–149s**; 5-unit wave at **180s**.
- `EnemyAI/State/{BuildState,TrainState,WaveAttackState}.java` issue those actions.
- Valuable: clear separation between faction-level commands and independent unit behavior.
- Shortcomings: hardcoded wall-clock schedule ends after 180s; no continuous goals/recovery; state replacement can bypass a prior state’s exit; `TrainState.isComplete()` always returns false; failed commands do not replan; the build search uses hardcoded dimensions and potentially invalid fallback.

### Individual combat

- `src/Unit/AIComponent/CombatUnitAIComponent.java` selects the highest-scoring eligible behavior (small **Utility AI**).
- `BuildingAttackAIState`: target enemy buildings with `ACQUIRE -> MOVE_TO -> ENGAGE` phases; reduced priority when damaged/low HP.
- `UnitAttackAIState`: retaliation if damaged within **1.2 seconds** and health >= **30%**.
- `RetreatToBaseAIState`: priority overrides other behaviors below **30% HP**.
- `CombatUnitMission.java`: currently `NONE` or `WAVE_ATTACK` against a target faction; `CombatUnitRole.java`: `DEFENDER` / `ATTACKER`.
- Valuable: mission intent coexists with local tactical reactions; behavior choice is lightweight and extendable.
- Shortcomings: no retreat completion/rejoin policy, no hysteresis, possible behavior churn, no reliable mission outcome, simplistic threat model; building targeting is not strategic knowledge/fog-aware.

### Waves and economy

- `WaveAttackState.java` recruits available `ATTACKER` units, preferring those close to base (18-cell preference) and avoiding units with active missions; selects a rival faction using its nearest live building.
- **Important:** the wave is a set of individual mission assignments, **not** a true coordinated squad. No staging, group readiness, synchronized traversal, or group retreat.
- `WorkerUnit.java` automatically finds resource nodes for AI, gathers, delivers, and selects replacements; `BuildingManager` and building subclasses drive construction/production.
- Shortcomings: economy does not decide strategy; no resource/supply-aware replanning, production replacement, or reactive base defense.

**Port concepts, not Java implementation details.** Unity has better navigation, formation, footprint, queue, and combat systems.

## 3. Unity integration map (current audited code)

| Responsibility | Existing code / API |
| --- | --- |
| AI faction bootstrap | `Assets/Script/Faction/AIFactionController.cs` (`Tick()` currently placeholder) |
| Faction ownership, diplomacy | `Faction.cs`, `FactionManager.cs`, `IFactionController.cs` |
| Simulation and pause | `Core/GameLoop.cs`, `MatchWorld.cs`, `GameBuilder.cs`, `GameContext.cs` |
| Unit runtime and commands | `Unit/UnitBase.cs`, `CombatUnit.cs`, `WorkerUnit.cs`, `Command/CommandContext.cs`, `CommandType.cs` |
| Sensing and combat | `Unit/Component/UnitSensor.cs`, `UnitWeapon.cs`, `Unit/State/AttackState.cs`, `CombatIdleState.cs`, `MoveState.cs` |
| Group navigation | `Pathfinding/Destination/FormationMovementGroup.cs`, `AttackPositionAllocator.cs`, `DestinationAllocationSystem.cs`, `Pathfinding/Shared/MovementGroup.cs` |
| Player command composition | `Command/CommandIssuer.cs` (currently tied to selection/player controller) |
| Construction | `Manager/BuildingManager.cs` — `CanConstruct(...)`, `Construct(...)`; building footprints come from `BuildingDefinition` |
| Production/supply | `Building/Component/UnitProductionComponent.cs` — `CanEnqueue`, `TryEnqueue`; `Manager/UnitManager.cs` population reservation |
| Workers/economy | `Unit/State/WorkerGatherState.cs`, `WorkerDeliverState.cs`, `Manager/ResourceManager.cs`, `Resource/ResourceNodeRepository.cs` |
| HP and damage source | `Shared/Health.cs`, `Shared/DamageInfo.cs` |
| Data-driven rosters | `Faction/FactionDefinition.cs`, `Unit/UnitDefinition.cs`, `Building/BuildingDefinition.cs`, `Assets/Data/` |

**Known integration concerns**

1. `AIFactionController.Tick()` is empty and `IFactionController.Tick()` presently lacks `deltaTime`. Prefer simulation time for scheduling (respect pause), without starting unrelated `MonoBehaviour.Update` loops.
2. `CombatUnit` already handles passive/attack-move awareness; AI commands must **not** fight its existing states, overwrite orders every tick, or change human-player behavior.
3. `AttackState` requests its approach when entered; dynamic pursuit of moving targets and bounded repathing need attention. Preserve the player’s existing explicit group attack-deployment behavior.
4. `CommandIssuer` depends on selected units and a player issuing faction. Later group AI should factor out shared command composition, **not** modify `GameContext.SelectedUnits` to emulate human input.
5. Unity production and construction enforce actual resources, supply, queue limits, footprint, and completion; AI must use these APIs, not grant itself free buildings/units.
6. `GameBuilder` currently sets up initial combat units for an AI faction but does not automatically build its initial Command Center. Base initialization/opening logic belongs in A3/A4, not A1.
7. Worker gather/deliver actions exist, but worker **assignment** by an AI faction is still missing (A3).
8. `GameContext` exposes global world objects; this is acceptable for an initial test, but plan a faction knowledge/visibility layer before implementing fog-aware strategy.
9. Avoid `Faction.Name` string comparisons for allegiance; use faction ownership and `Faction.IsEnemy(...)`.

## 4. Intended responsibilities

- **Faction Strategy (A3–A4):** evaluate economy, supply, structures, worker counts, production capacity, army strength, threat, and replenishment. Economy and military decisions must be able to progress concurrently. Timed openings are optional profiles, not the sole AI loop.
- **Mission Director (A2):** own mission membership and lifecycle (`Form -> Assemble -> Advance -> Engage -> Evaluate -> Complete/Withdraw/Fail`), prevent double assignment, return surviving units to an available pool.
- **Squad (A2):** shared objective/staging and movement; use current formation/navigation systems rather than separate pathfinding.
- **Individual AI (A1):** own personal mission context and local tactical decisions; react to threats, approach/attack objectives, bound pursuit, retreat, complete/cancel cleanly. Do not replace existing `UnitWeapon`, `UnitSensor`, or combat unit state machines.

### Desired A1 tactical principles

- Preserve `DEFENDER`/`ATTACKER` as a minimal role concept. An attacker may temporarily defend itself without losing its objective; a defender should respect a configurable base/guard leash.
- A mission can identify a target faction/building or a guard anchor. Favor a **minimal, explicit API** that A2 can call later; avoid building squad logic in A1.
- Priority order (conceptual): critical retreat > immediate local self-defense > mission objective > normal idle awareness. Use scoring or deterministic priorities; do not reproduce Java’s exact integer scores unnecessarily.
- Defaults to tune: critical HP around 30%, recent-damage window around 1.2s, decision cadence around 0.1–0.2s. Add hysteresis/cooldowns so behaviors do not oscillate.
- Retreat must end cleanly **even without healing**: reach a safe owned/guard position, mark state/mission appropriately, and stop automatic retreat loops. No indefinite return-to-base oscillation.
- Preserve `AttackMove`, `Move`, `HoldPosition`, explicit attack, formation, reservations, and weapon animation behavior for human-controlled units.
- Handle destroyed targets, unreachable destinations, dead units, missing buildings, mission replacement and cancellation; avoid commands every frame, repeated allocations, and noisy logs.

## 5. Development sequence

| Stage | Scope | Playable outcome |
| --- | --- | --- |
| **A1 — Individual Combat Intelligence** | Java-inspired per-unit Utility AI, role/mission API, defense, objective targeting, bounded pursuit, retreat, cleanup | A single AI unit can execute an objective, react, recover, and stop correctly |
| **A2 — Squad Missions** | Mission director, recruitment, staging, coordinated movement, assaults/defense, losses and operation outcomes | Coherent offensive/defensive groups |
| **A3 — AI Economy and Production** | Base initialization, worker assignment, gathering, construction, production/supply and replacements | A functioning self-sustaining AI faction |
| **A4 — Continuous Faction Brain** | Situation-driven prioritization, attack scheduling, reserves, defense, recovery; optional opening profile | The AI develops and fights indefinitely |
| **A5 — Strategic Polish** | Scouting knowledge, harassment, reinforcement, compositions, defensive placement, personalities/difficulty | Varied, convincingly reactive opponents |

**Scope discipline:** Work on the assigned stage only. A1 should define the smallest extensible contract for A2 but must **not** implement squad director, economy, production, turrets, opening schedules, or new UI.

## 6. Engineering and testing conventions

1. Read `AGENTS.md`; preserve the existing `Assets/Script/` naming and Unity `.meta` / GUID references.
2. Use faction ownership to decide which units receive AI. AI is not a new combat-unit subtype or a duplication of combat mechanics.
3. Prefer plain C# runtime planners/controllers owned by existing simulation tick paths. Keep complexity modest and configuration inspectable.
4. Avoid player-selection dependencies, per-frame global scans, repeated path requests, repeated command issuance, and uncontrolled logging; stagger/throttle tactical decisions as appropriate.
5. Preserve baseline player behavior. If the shared attack/chase code must be corrected, make the smallest safe change and verify group attacks/attack-move semantics are unaffected.
6. Test first with **one Terran AI combat unit versus a player target**; then moving targets, building targets, low HP, invalid targets, path failures, and both attacker/defender roles.
7. Prefer focused compile checks and brief manual Play Mode test instructions. The user handles interactive gameplay verification; no extensive test suite required.
8. MCP handoff: report **behavior implemented, files changed, unresolved limitations, and concise manual verification steps**. Do not claim unrun checks passed.
