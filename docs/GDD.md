# Game Design Document — working title **Ember & Blossom**

> Living document. Engine: Godot 4.7 (.NET / C#). Last updated 2026-11-17.

---

## 1. Elevator pitch
You wake in a tiny shack on the edge of a cliff above a sea of clouds. Waterfalls thunder around you and a volcano glows on the far horizon. Build that shack into a keep and grow a farm and an economy. Turn raw wood, stone and ore into steel, then carry that steel down the cliffs to drive back the bandits and beasts threatening the valley. You can play in first or third person at any time, alone or in co-op with up to three friends (see §16).

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
  - **Built (vertical slice):** Wheat Plot and Turnip Plot buildings (4 wood + 1 stone). E plants a seed from the stockpile. Wheat ripens in half a day, turnips in 0.4 days, measured on the game clock, so sleeping through the night counts. E on a ripe plot harvests it (3 wheat + 2 seeds back, or 2 turnips + 1 seed). Wheat seeds come from wild grain tufts (hit them) on the plateau and in the vale; turnip seeds come from the trader. No watering yet.
  - **Cooking (built):** the campfire is also a cooking station: Bread (3 wheat, heals 35) and Roast Turnip (2 turnips, heals 25). **F** eats the best food in the stockpile for the health you're missing. Timed buffs come later.
- **Village of Ashby** (~10 min walk): a trader buys surplus and sells seeds and blueprints. A quest board. Later it sends **workers** who run your stations automatically, so the economy keeps going while you're away.
  - **Built (vertical slice):** Ashby sits at the south end of the vale below the plateau: five houses around a well, lamps, a notice board, and **Marta the Trader**. It's a sanctuary (28 m): wolves and bandits won't enter or hunt inside.
  - Marta sells wheat seeds (2), turnip seeds (3) and iron nails (5), and buys wolf pelts (8), wheat (1), turnips (2), bread and roast turnips (4). Prices live on each item (`BuyPrice` / `SellPrice` in its `.tres`), so new goods just need a price.
- **Coin** comes from selling goods and bandit bounties. It's spent on blueprints, workers and land claims.

## 5. Building
- **Placement:** grid snap (0.5 m), rotation in 45° steps (free rotation later), a ghost preview showing green for valid and red for invalid.
- **Claim radius:** you can only build near your home. Upgrading the house tier widens the claim. Later, outposts make new claims.
- **House tiers:** Shack → Cottage → Longhouse → Keep. Each tier unlocks building types and worker slots.
  - **Cottage (built):** pay at the **Home Plans** board by the shack: 40 wood, 20 stone, 6 iron nails, 3 wolf pelts and the Cottage Blueprint (from the bandit captain). The shack is replaced by a timber-and-plaster cottage you can walk into, with two beds. Your land claim grows from 30 m to 42 m. The cottage's footprint is kept clear of new buildings while you still live in the shack; anything already there is packed up and refunded when it goes up. The tier is saved and sent to every player.
- **Stations:** Workbench, Sawhorse, Kiln, Smelter, Forge, Anvil, Cookfire, Loom, Storage Chest, Well, Farm Plot.
- **Defense:** palisade → stone wall → gatehouse → watchtower. Bandit raids target bases above a "wealth" threshold.

## 6. Weapons & combat
- **Sword tiers:** Wooden → Stone Club → Bronze → Iron → Steel → Ember-forged.
- **Moves:** light attack (LMB), heavy attack (Q or middle mouse), **block** (hold RMB), **parry** (raise the block just before a hit lands). Planned: dodge roll. All of them use **stamina**.
- **Block / parry (built):**
  - A raised guard stops hits from the front (about ±70° of where you look). A blocked hit does 20% damage and costs stamina (8 + 0.8 × the hit). Run out of stamina and the guard breaks (you can't raise it for 1.2 s).
  - Guard raised **0.3 s or less** before the blow lands = **parry**: no damage, some stamina back, and the attacker reels (wolves 1.6 s, the captain 1.2 s). In PVP, a parried player can't swing for 1 s.
  - While guarding you walk at half speed, can't sprint or attack, and stamina regenerates slowly.
  - Everything that hurts a player (enemies and PVP) goes through one server function, `Net.ServerDamagePlayer`, so the rules are the same everywhere.
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

### Wolves (built)
- **When:** at nightfall a pack appears at the plateau's edge, away from every player. A second pack comes later in the night (2 per night). At dawn they give up and slink off.
- **Pack size:** 2 for one player, scaled up for co-op by `Difficulty.PackSize` (3 / 4 / 5 for 2 / 3 / 4 players). Wolves are small animals, so their health (40) and bite (8) never scale.
- **Behaviour:**
  - The pack roams around an anchor point that creeps toward your base, so wolves come to you.
  - A wolf that spots a player within 22 m alerts its packmates, and they all chase. They give up beyond 34 m.
  - The bite is telegraphed: a crouch, then a lunge.
  - Hitting a wolf staggers it and pulls its aggro onto you.
  - Run speed is 7.2 m/s, so a sprinting player can just outrun them.
- **Wolves never enter a blossom sanctuary's light** (9 m). They'll bite you out of bed if your bed isn't safe.
- **Drop:** a Wolf Pelt into the shared stockpile, for leather gear in tier 1.
- **Server option:** `enemies = false` turns hostile creatures off (a peaceful building server).
- Greybox body made of boxes; a real model and animations come in the art pass. There's no navmesh yet: wolves steer around props with a feeler ray and treat the cliffs as walls.

### Bandit camp (built)
- **Where:** off the road to Ashby, east of the track, about halfway down the vale (38, 0, 140). Tents, a fire, a stake fence with its mouth toward the road.
- **Who:** grunts (60 HP, 10 damage, a slow 0.55 s swing) and one **Bandit Captain** (160 HP, 18 damage, a heavier 0.75 s chop). They hold the camp day and night, chase anyone within 15 m, and won't follow more than 30 m from camp.
- **Scaling (§16):** grunts are small enemies, so the camp gets more of them (2 / 3 / 4 / 5 for 1–4 players). The captain is a big monster: one of them, with scaled health and damage.
- **Loot:** 6 coins per grunt and 25 for the captain, into the shared stockpile. The captain also carries the **Cottage Blueprint** while your home is still a shack.
- **Respawn:** a cleared camp is re-occupied after 3 in-game days, when nobody is standing in it. The cleared state is saved.
- Bodies are the KayKit Knight with a red tint; the captain keeps his helmet, cape and spiked shield.

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
- **Concept art per zone:** see [art/README.md](art/README.md). It covers 10 generated target images (dawn and night cliffs, Ashby, Falls Valley, the volcano, house tiers, the forge, raids, the hero and weapons, and the cloud glide), each with notes on what to take from it.
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
| Light / Heavy attack | LMB / Q or middle mouse |
| Block (time it to parry) | RMB (hold) |
| Interact | E |
| Eat | F |
| Weapon slots | 1–5 |
| Build mode (cycle / rotate / place / exit) | B (wheel / R / LMB / RMB or B) |
| Save / Load (host only) | F5 / F9 |
| Player list | Tab (hold) |
| Help | F1 |
| Release mouse / close menu | Esc |

## 14. Roadmap
1. **Greybox v0 (done):** movement, 1P/3P, gathering, build mode, workbench crafting, training dummy, day/night, save/load.
2. **Multiplayer foundation (done):** host/join, dedicated server, shared base, sleep vote, PVP toggle, difficulty scaling hooks (§16). Every later feature is built multiplayer-first.
3. **Vertical slice (done):** wolves with AI, block/parry, farm plots and cooking, Ashby village with a trader, one bandit camp, house tier 2 (cottage). Next: playtest it on two real PCs and tune the numbers.
4. **Art pass:** toon/foliage shaders, sculpted rock kit, character model and animations, audio.
5. **Falls Valley:** climbing, glider, updrafts, the second sanctuary.
6. **Volcano:** heat system, lava hazards, Ember Ore, the Sacred Forge.
7. **Cities and workers:** automation and the regional economy.

## 15. Code architecture notes
- **Enemies:** `Wildlife` (Main/Wildlife) spawns and replicates every hostile (wolf packs, the bandit camp); the server runs their AI, and clients show positions sent 15 times a second. `Enemy.cs` is the shared brain (prowl around a group anchor, chase, telegraphed windup, strike, stagger, flee). A new enemy type subclasses it with its numbers, body and animations (see `Wolf.cs`, `Bandit.cs`) and registers its kind in `Enemy.Create`. Strikes on players go through `Net.ServerDamagePlayer` (block / parry).
- **Building state:** a placed building with state (a farm plot's crop) implements `INetState`. The server changes it and calls `Net.BroadcastBuildingState`; the state is also in the join snapshot and the save.
- **World:** `HomeCliffs` builds the plateau; `HomeCliffs.Ashby.cs` (same class) builds the vale, Ashby, the bandit camp and the home tiers (rebuilt on `GameState.HomeTierChanged`).
- **Components:** `Health` (anything that can be damaged), `Harvestable` (resource nodes), and the `IInteractable` interface (stations, beds, signs, shrines).
- **Data-driven:** items, recipes and buildings are Godot `Resource` files in `data/`. Adding content is mostly adding `.tres` files.
- **`GameState` autoload:** the shared stockpile, time of day, placed buildings, the player registry, save/load.
- **`Net` autoload:** session mode (offline / host / client / dedicated), the connection handshake, and every gameplay RPC. Gameplay code asks `Net` to do things ("request build", "request craft") and never mutates shared state directly.
- **Authority rule:** the server owns shared state (stockpile, buildings, time, all `Health` values, sleeping, saving). Each client owns only its own player's movement and camera. Single-player is just a server with one local player, so there is one code path.
- **Deterministic node names:** anything the network refers to (resource nodes, buildings, stations, players) has a stable name, so paths match on every machine.
- **Physics layers:** 1 World (terrain), 2 Props (trees, rocks, buildings, dummies), 3 Player.

## 16. Multiplayer
**Target:** drop-in co-op for **up to 4 players** (the design and balance target). A server option lets a dedicated server allow more.

### Ways to play
| Mode | How | Who owns the world and save file |
|---|---|---|
| Single player | Main menu → Single Player | You (a private server with one player) |
| Host co-op | Main menu → Host; friends Join by IP:port | The host's machine |
| Dedicated server | `--headless -- --server`, settings in `server.cfg` | The server machine; it runs with no player of its own |

Friends join by address for now (the host forwards the port, or everyone uses a LAN or a virtual LAN such as Tailscale). Steam lobbies and invites come later; the network layer won't change.

### What is shared and what is personal
| Shared by everyone | Personal to each player |
|---|---|
| The base: buildings, land claim, stations | Position, health, stamina |
| The **stockpile**: every gathered and crafted item | The weapon in your hand (taken out of the stockpile; it returns when you swap or leave) |
| Time of day, the day count | Respawn shrine |
| Resource nodes (a felled tree is felled for all) | Camera view (1st/3rd person) |

- **Greybox simplification:** gathered items go straight into the shared stockpile wherever you are. Later, carried items (a personal pack) must be deposited at a chest at the base, which makes trips home and raids on carriers meaningful.
- Players are saved by name: rejoining puts you back where you logged out, with your respawn shrine.

### Sleeping
- Using a bed puts you to sleep. You can get up with E or by moving.
- **The night is skipped only when every connected player is asleep.** The HUD shows "Sleeping 2/3". Then everyone wakes at dawn fully healed and the server saves.
- Bedrolls are cheap to build, so each player can have their own bed.
- A dedicated server pauses the clock while nobody is connected (the `pause_when_empty` option).

### Difficulty scaling (with N connected players)
- **Small animals** (wolves, boars, harpies) come in **larger packs**: pack size × (1 + 0.5 × (N − 1)), rounded.
- **Big monsters** (bandit captains, elites, bosses) get **more health and damage** instead of more copies: health × (1 + 0.35 × (N − 1)), damage × (1 + 0.15 × (N − 1)).
- Each factor is a server option. `Difficulty.cs` holds the formulas. Wolves and the bandit camp spawn through it (grunts = small, captain = big). Scaling is decided when a group spawns, so a camp manned for 1 player stays that size if a friend joins later.

### PVP
- **Off by default.** It's a server option and the host's checkbox. When on, player sword hits damage other players. A downed player respawns at their shrine and loses nothing (for now).
- PVP never damages the shared base.

### Saving
- Only the host or server saves, to `user://saves/<world>.json`. Clients never write the world.
- Saves happen on sleep, on F5 (host), on a dedicated-server autosave timer, and when the last player leaves.

### Server options (`server.cfg`)
`name`, `port` (24570), `max_players` (4; more is allowed but untested beyond 4), `password`, `world`, `pvp`, `autosave_minutes`, `pause_when_empty`, `enemies`, plus the three difficulty factors.

