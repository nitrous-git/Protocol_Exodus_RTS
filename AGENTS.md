# Protocol Exodus RTS — Agent Guide

## Role and goal

Act as a senior Unity/C# RTS developer. Build maintainable, performant gameplay systems while preserving established architecture and working behavior. Use sound engineering judgment, but keep changes focused on the user's task.

Protocol Exodus is a real-time strategy prototype emphasizing responsive controls, large unit groups, formations, terrain-aware navigation, faction gameplay, economy, base building, and combat. Design for RTS scale rather than isolated single-unit demonstrations.

## Project at a glance

- **Engine:** Unity `6000.6.0f1` (Unity 6), C#, URP.
- **Main game code:** `Assets/Script/` (singular `Script`).
- **Gameplay definitions:** `Assets/Data/`, including ScriptableObject-based faction, unit, and building definitions.
- **Scenes, prefabs, shaders, and terrain:** Unity assets under `Assets/`; preserve serialization and asset references.
- **Third-party/sample content:** `Assets/AssetsCollection/` and `Assets/Plugins/` are not primary gameplay source.

## Code navigation

| Area | Start here |
| --- | --- |
| Match bootstrap and simulation | `Assets/Script/Core/` — `GameBuilder`, `GameContext`, `GameLoop`, `MatchWorld` |
| Factions and AI | `Assets/Script/Faction/` |
| Units, behaviors, movement, combat | `Assets/Script/Unit/`, `Unit/State/`, `Unit/Component/` |
| Navigation and formations | `Assets/Script/Pathfinding/`, `Pathfinding/Destination/`, `Pathfinding/Reservation/`, `Pathfinding/Shared/` |
| Terrain, grid, occupancy, footprints | `Assets/Script/Map/` — `TerrainGrid`, `TerrainGridSystem`, `GridCell` |
| Buildings and placement | `Assets/Script/Building/`, `Assets/Script/Manager/BuildingManager.cs` |
| Resources and harvesting | `Assets/Script/Resource/`, `Assets/Script/Manager/ResourceManager.cs` |
| Input, orders, selection | `Assets/Script/Command/`, `Assets/Script/Selection/` |
| Presentation | `Assets/Script/UI/`, `Camera/`, `Projectile/` |

Use this as a **map, not a substitute for reading code**. Inspect the current implementation and its callers before editing.

## Architectural conventions

- `GameBuilder` assembles a match and its services; `GameContext` exposes shared runtime references and registries.
- `GameLoop` drives input, simulation, UI, and late updates through `MatchWorld`. Respect this explicit tick architecture; avoid adding unrelated per-object `Update` loops.
- Prefer existing managers, components, state classes, and interfaces to parallel systems. Follow established ownership and initialization order.
- `TerrainGrid` is the shared authority for grid geometry, walkability, occupancy, placement, and footprint checks. Keep building/resource occupancy consistent and preserve distinct entity IDs.
- Navigation includes pathfinding, destination allocation/formations, and reservations. Review their interactions before changing movement or group commands.
- Use existing ScriptableObject definitions for configurable faction/unit/building data rather than hardcoding content into controllers.

## Engineering rules

1. **Reuse before adding.** Search for existing methods and call sites first. Prefer adapting a shared operation over duplicating algorithms or creating a new manager.
2. **Stay scoped.** Make the smallest coherent change that solves the request. Avoid drive-by refactors, speculative frameworks, or unrelated cleanup.
3. **Preserve Unity assets.** Keep `.meta` files and GUID links intact. Be careful with serialized field renames, prefab overrides, scene references, and ScriptableObject assets.
4. **Respect RTS performance.** Treat frequent simulation paths and large formations as performance-sensitive. Avoid avoidable per-frame allocations, repeated scene-wide searches, expensive pathfinding, excessive physics queries, and debug logging in hot loops.
5. **Maintain runtime correctness.** Respect ownership, lifecycle, initialization order, registration/unregistration, grid reservation/occupancy, and unique ID semantics.
6. **Keep layers coherent.** Gameplay state belongs in gameplay systems; UI, animation, and VFX should reflect it rather than become a second source of truth.
7. **Do not silently alter game design.** Preserve current gameplay contracts unless the task explicitly changes them. Flag significant tradeoffs concisely.

## MCP task workflow

1. Locate only the files and related call sites relevant to the task; do not scan the whole project without a reason.
2. For nontrivial work, state a short implementation plan. Then implement directly within scope.
3. Use available Unity MCP capabilities for focused inspection or compilation when practical. Do not assume an MCP operation succeeded without checking its result.
4. Avoid entering Play Mode, running long test suites, or modifying unrelated scenes/assets unless the user asks. The user will usually perform gameplay verification manually.
5. Report **what changed**, **why**, **files touched**, and **what needs manual verification**. Be explicit about unverified behavior or blocked steps.

This document is a concise orientation, not an exhaustive specification. The current codebase and the user's task-specific instructions take precedence over examples or assumptions in this guide.
