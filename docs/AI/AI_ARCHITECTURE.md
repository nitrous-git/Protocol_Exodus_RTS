# Protocol Exodus RTS — Enemy AI Architecture

The `AI_Behavior` branch implements A1–A4. Each AI faction owns its controllers and runtime state. The same `GameLoop` and `MatchWorld` simulation tick drives faction AI, units, construction, and production. Pausing a match stops their timers.

## Ownership

| Layer | Owner | Responsibility |
| --- | --- | --- |
| A1 individual tactics | `CombatUnitAI` | Execute assigned combat or guard intent. Handle local retaliation, pursuit, and critical-health retreat through existing unit commands and combat systems. |
| A2 squad missions | `AIMissionDirector`, `AISquadMission` | Recruit eligible units, reserve members, find a valid objective, stage and move squads, guard an area, and release survivors. Report assault completion and casualties to A4. |
| A3 economy and production | `AIEconomyManager`, `AIBuildingPlacement` | Assign workers, choose legal sites, construct infrastructure and queue units. Keep existing prices, supply reservations, deployment checks, and retry intervals. |
| A4 faction strategy | `AIFactionController`, `AIFactionStrategy` | Read economy, military and local-threat snapshots about every 1.5 simulation seconds. Send per-faction goals to A3 and authorize A2 defense and assaults. |

A4 replaces scheduled A2 wave dispatch and A3's standalone goals while enabled. A2 retains its assembly, eligibility, reservation, and withdrawal rules. A3 retains control of worker orders, placement and production. The Inspector's A2 and A3 policy settings apply to their standalone runs. Their execution settings also apply when A4 is enabled.

## Opening and continuous play

The Java prototype establishes headquarters and workers, adds Barracks and infantry, then uses 70-, 120-, and 180-second attack opportunities. In Standard Match, A3 pays to construct the AI's Command Center, and a working economy opens Barracks construction around 24 seconds. Infantry waits for an operational producer. A3 supplies Depots according to projected supply demand, and A4 raises the army goal around 145 seconds to prepare for the larger assault. [Java reference](https://github.com/nitrous-git/RTS-Prototype/blob/AI_Behavior01/src/EnemyAI/AIManager.java)

The three attack windows allow a one- or two-unit probe, up to four units in the first organized assault, and up to five in the larger assault with Medium defaults. The probe returns through A2's withdrawal path after its time limit or first objective kill. All three windows require operational infrastructure, gathering, eligible recruits beyond the home reserve, a valid objective and usable staging. A blocked window expires after 30 seconds by default. After the opening, Medium can send the next ready assault after a 9–15-second cooldown. A failed or costly assault has a 30-second recovery. Defense pressure and unit readiness can still delay a launch.

A4 maintains a two-unit guard with Medium defaults. Units finish their Barracks deployment before recruitment. At rest, guards use safe posts around a Barracks rally point within the home defense area. A4 reinforces and shifts the existing mission toward threatened structures, then returns it to baseline after a stand-down delay. The next offensive waits while local defense takes priority. Active assaults remain under A2 control. Failed or costly operations trigger a longer recovery, and aborting a mission manually creates a cooldown before A4 can dispatch another.

The economy keeps producing during assaults and military recovery. Headquarters and minimum workforce recovery take priority after severe losses. Barracks replacement uses normal construction, and an optional second Barracks requires sufficient income, army demand and spending reserves. Medium caps the army target at 16 and Barracks at two by default. Blocked production exits and invalid sites use existing A3 retries. If critical infrastructure is unaffordable and income is lost, the AI waits with the assets it still has.

## Match setup and diagnostics

- **Standard Match** is the default in `SampleScene`. Each faction starts with three workers, 550 minerals, zero gas, ten supply, and an empty base. The player places their Command Center using the existing build menu, and A3 places the AI's. Both pay the normal 200-mineral Terran cost.
- **Development Sandbox** uses the existing `startingCombatCount`, spacing, and faction-definition stockpile. Enable **AI Strategy → Enable Strategy AI** to exercise A4 from an empty economy or leave it off to test A1–A3 independently.
- **AI Strategy** groups shared opening timing and a **Medium Profile**. The profile holds reserves, threat radius, attack sizes, cooldown and recovery, production targets, and expansion thresholds. A future difficulty selection can pass a different profile to each faction while keeping the same opening and base-building grace period.
- In Play Mode, **AI Strategy → Log Strategy Status** reports the opening or strategic mode, goals, workforce, supply, military assignments, local pressure, and the next attack gate. The existing **AI Missions** launch, defend and cancel commands and **AI Economy** reevaluation and status commands remain available.

For a 5–10 minute Standard Match check, place the player's Command Center and start the workers gathering. Use the strategy log around the opening windows and after 180 seconds. Approach the AI's base with combat units, remove a defender, and defeat a launched assault. Watch for guard replenishment, uninterrupted production where affordable, and a delayed follow-up assault. Destroy a Barracks or the Command Center to exercise paid recovery. In Sandbox with strategy disabled, check that the manual commands and standalone wave and production settings still apply.

A5 covers fog-of-war knowledge and scouting, counter-compositions, difficulty profiles, harassment and flanking, multi-base expansion, advanced defensive placement, and broader victory conditions.
