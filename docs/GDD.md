# Game Design Document — working title **Ember & Blossom**

> Living document. Engine: Godot 4.7 (.NET / C#). Last updated 2026-11-12.

---

## 1. Elevator pitch
You wake in a tiny shack on the edge of a cliff above a sea of clouds. Waterfalls thunder around you and a volcano glows on the far horizon. Build that shack into a keep and grow a farm and an economy. Turn raw wood, stone and ore into steel, then carry that steel down the cliffs to drive back the bandits and beasts threatening the valley. You can play in first or third person at any time.

## 2. Pillars
1. **Home grows with you.** The base is the main progression track: shack → cottage → longhouse → keep. Every upgrade is visible from the cliff.
2. **Earned steel.** Better weapons come from a real production chain. You can't buy power; you build the forge that makes it.
3. **A vertical world.** Cliffs, overhangs, waterfalls and spires. Height is both danger and opportunity.
4. **Sacred calm vs. creeping danger.** Cherry-blossom sanctuaries are safe and glowing. Everything past their light gets harsher, and the volcano is the harshest of all.

## 3. Core gameplay loop
```
 Gather ──► Build ──► Refine ──► Craft ──► Venture out ──► Return
   ▲                                                     │
   └──────────── loot, coin, blueprints, workers ◄───────┘
```
- **Minute to minute:** chop, mine and harvest, place buildings, fight.
- **Day to day (in-game, ~10 real minutes):** work and build by day. Night brings mist and wolves, which pushes you to light, walls and shelter. Sleeping in your bed skips to dawn and saves.
- **Session arc:** a goal such as "forge an iron sword" → find ore → build a smelter → clear a bandit camp for a blueprint → craft → push into the next zone.

## 4. Economy & resources
| Tier | Raw (gathered) | Refined (station) | Goods |
|---|---|---|---|
| 0 | Wood, Stone, Fiber | — | Wooden Sword, Stone Club, Campfire |
| 1 | Clay, Crops (wheat, turnip) | Planks (Sawhorse), Bricks (Kiln), Food (Cookfire) | Cottage, farm plots, leather gear |
| 2 | Copper + Tin ore | Charcoal (Kiln), Bronze Ingot (Smelter) | Bronze Sword, tools |
| 3 | Iron ore | Iron Ingot (Smelter + Bellows) | Iron Sword, chain armor, Longhouse |
| 4 | Coal + Iron | Steel (Blast Forge) | Steel Sword, Keep |
| 5 | Ember Ore (volcano only) | Ember Steel (Sacred Forge) | Ember-forged blades, heat-ward armor |

- **Farming:** till a plot → plant seeds → water it (a well or rain) → harvest after N in-game days. Food gives timed buffs (stamina regen, max health).
- **Village of Ashby** (~10 min walk): a trader buys surplus and sells seeds and blueprints. A quest board. Later it sends **workers** who run your stations automatically, so the economy keeps going while you're away.
- **Coin** comes from selling goods and bandit bounties. It's spent on blueprints, workers and land claims.

## 5. Building
- **Placement:** grid snap (0.5 m), rotation in 45° steps (free rotation later), a ghost preview showing green for valid and red for invalid.
- **Claim radius:** you can only build near your home. Upgrading the house tier widens the claim. Later, outposts make new claims.
- **House tiers:** Shack → Cottage → Longhouse → Keep. Each tier unlocks building types and worker slots.
- **Stations:** Workbench, Sawhorse, Kiln, Smelter, Forge, Anvil, Cookfire, Loom, Storage Chest, Well, Farm Plot.
- **Defense:** palisade → stone wall → gatehouse → watchtower. Bandit raids target bases above a "wealth" threshold.

## 6. Weapons & combat
- **Sword tiers:** Wooden → Stone Club → Bronze → Iron → Steel → Ember-forged.
- **Moves:** light attack (LMB), heavy attack (RMB). Planned: block (hold), parry (block timed right before a hit), dodge roll. All of them use **stamina**.
- **Enemies:**
  - Start: wolves (packs at night), bandit scouts.
  - Mid: bandit camps (a leader drops a blueprint), boars, cliff harpies.
  - Late: fire-touched creatures, magma golems (volcano).
- **Raids:** bandits sometimes attack your base. Walls and towers matter.

## 7. Camera — first / third person
- Toggle with **V**, at any time.
- **First person:** precise building placement, close-up swordplay, the body hidden and the weapon held in view.
- **Third person:** over-the-shoulder spring arm, the body visible. Better for crowd fights and for admiring your base.
- Both views share one controller, one attack system and one interaction raycast. Only the camera rig and the weapon's attach point change.

## 8. Traversal (verticality)
- **Now:** walk, sprint (stamina), jump, fall damage.
- **Planned:**
  - **Climbing** marked rock faces (lighter/rougher texture). It drains stamina, and you fall if you run out.
  - **Ledge grab** and mantle.
  - **Glider cloth** (mid-game, crafted at the loom) for cliff dives.
  - **Waterfall updrafts:** mist columns at waterfall bases lift a glider, which makes waterfalls natural elevators.
  - **Swimming** in pools. Waterfalls can't be climbed without the late-game Grapple Hook.
  - **Fast travel** between attuned blossom sanctuaries.

## 9. Hazards and progression
| Zone | Hazards | Counter |
|---|---|---|
| Home Cliffs (start) | Falls, night mist (slow stamina regen), wolves | Light, shelter, food |
| Falls Valley | Slippery wet rock, river currents, harpies | Grip boots, glider |
| Forest Road / Ashby | Bandit camps, raids | Walls, better swords |
| Volcano (late) | **Heat meter** (rises near lava and drains health when full), lava flows, ash storms, emissive lava falls | Heat-ward cloak, blossom charms, cool springs |

## 10. Sacred cherry-blossom sanctuaries
- Glowing cherry trees on cliff edges. **The first stands beside your shack.**
- Functions: **respawn point**, **fast travel** node, **safe zone** (enemies won't enter its light), and an altar for **blessings** (permanent upgrades bought with Blossom Petals from elite enemies and exploration).
- Each new sanctuary you light up opens the next region. That's the main progression gate, which ties exploration to power.
- Late game: blossom charms push back volcano heat, letting the calm side and the dangerous side of the world interact.

## 11. World map (modular)
```
                [Volcano — late]
                      │
 [Spire Cliffs] ── [Falls Valley] ── [Ashby Village] ── [Forest Road] ── [Cities…]
                      │
                [HOME CLIFFS — start]
```
- Each zone is a **separate scene**, joined by road or trigger connectors and loaded in the background. New zones (cities, mines, the volcano) are added by dropping in a scene and a connector, with no core code changes.
- **Home Cliffs layout:** a plateau of about 120 × 120 m. The shack and the sacred tree are on the north cliff edge, over a cloud sea. High rock walls stand to the east and west, with waterfalls. A road leads south toward Ashby, about a 10-minute walk.

## 12. Art & tech direction (from the visual brief)
- **Style:** painterly, angular hand-sculpted shapes, very little surface noise, strong color blocking. Think Ori × BotW × Journey.
- **Palette:** shadows in obsidian, slate and twilight indigo. Highlights in magma orange, sunset gold and blush pink.
- **Reference image** (`docs/art/reference.jpg`). This is the target for the art pass and the game's "hero shot":
  - A small hero stands on a **dark, chunky, faceted rock ledge** at the left, seen from behind, looking out.
  - A **gnarled cherry tree** grows off a black-rock overhang on the right. Its pink canopy is **lit from inside**, made of clustered blossom clumps (not one blob), with **petals and embers drifting** across the frame.
  - A **sea of billowing cumulus clouds** fills the lower half. The clouds are lit warm pink and orange from below by **thin lava falls** pouring from cracks in the black rock.
  - The sky runs from a **burning orange and gold horizon** (sun just set) up to **deep indigo with stars**. Sky and clouds carry most of the color; the rocks stay almost silhouettes, with warm rim light.
  - Takeaways for the art pass: (1) volumetric-looking cloud puffs instead of flat planes, (2) darker rocks with emissive lava cracks, (3) a canopy built from many small blossom clusters plus point lights, (4) a stars layer in the night sky, (5) an orange "sunset band" fog color at the horizon, and (6) the default start time set to golden hour, framed like this shot.
- **Godot pipeline:**
  - Forward+ renderer, **ACES** tonemapping, **HDR glow/bloom** on emissives (lava, blossoms, waterfalls).
  - **Exponential height fog** that blends the horizon into the cloud sea. Volumetric fog in the hero areas.
  - Low sunset **directional backlight**, plus warm **omni lights** from lava and fires.
  - **Foliage:** a backlit/SSS-style shader with smoothed vertex normals for soft canopies (greybox uses StandardMaterial3D's *backlight*).
  - **Clouds and steam:** a depth-fade shader for soft intersections with rock.
  - **Particles:** drifting petals, waterfall mist, embers and ash.
  - Later: a toon ramp shader for rock and characters.

## 13. Controls (current greybox)
| Action | Key |
|---|---|
| Move / Sprint / Jump | WASD / Shift / Space |
| Look | Mouse |
| Toggle 1st / 3rd person | V |
| Light / Heavy attack | LMB / RMB |
| Interact | E |
| Weapon slots | 1–5 |
| Build mode (cycle / rotate / place / exit) | B (wheel / R / LMB / RMB or B) |
| Save / Load | F5 / F9 |
| Help | F1 |
| Release mouse / close menu | Esc |

## 14. Roadmap
1. **Greybox v0 (now):** movement, 1P/3P, gathering, build mode, workbench crafting, training dummy, day/night, save/load.
2. **Vertical slice:** wolves with AI, block/parry, farm plots, Ashby village with a trader, one bandit camp, house tier 2.
3. **Art pass:** toon/foliage shaders, sculpted rock kit, character model and animations, audio.
4. **Falls Valley:** climbing, glider, updrafts, the second sanctuary.
5. **Volcano:** heat system, lava hazards, Ember Ore, the Sacred Forge.
6. **Cities and workers:** automation and the regional economy.

## 15. Code architecture notes
- **Components:** `Health` (anything that can be damaged), `Harvestable` (resource nodes), and the `IInteractable` interface (stations, beds, signs, shrines).
- **Data-driven:** items, recipes and buildings are Godot `Resource` files in `data/`. Adding content is mostly adding `.tres` files.
- **`GameState` autoload:** inventory, time of day, equipped weapon, placed buildings, save/load.
- **Physics layers:** 1 World (terrain), 2 Props (trees, rocks, buildings, dummies), 3 Player.
