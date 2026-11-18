# Game Design Document — working title **Ember & Blossom**

> Living document. Engine: Godot 4.7 (.NET / C#). Last updated 2026-11-18.

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
  - **Built (vertical slice):** Wheat Plot and Turnip Plot buildings (4 wood + 1 stone). E plants a seed. Wheat ripens in half a day, turnips in 0.4 days, measured on the game clock, so sleeping through the night counts. E on a ripe plot harvests it into the stockpile (3 wheat + 2 seeds back, or 2 turnips + 1 seed). Wheat seeds come from wild grain tufts (hit them) on the plateau and in the vale; turnip seeds come from the trader.
  - **Watering (built):** a crop only grows while its soil is wet. E at a **well** (Ashby's, or a Well you build: 10 stone + 4 wood) or at the **pool** under the west waterfall puts 4 Water in your pack; E on a dry plot uses one. One watering keeps the soil wet for 0.6 game days, enough for one crop. Dry soil is pale and the crop turns yellowish. **Rain** showers roll in every 4–9 minutes, last 1–2 minutes and water every plot. The server decides the weather and sends it to everyone. While it rains the sky greys, the haze thickens and the grass darkens.
  - **Cooking (built):** the campfire is also a cooking station: Bread (3 wheat, heals 35) and Roast Turnip (2 turnips, heals 25). **F** eats from your pack (and from the stockpile at home): the smallest meal that heals what you're missing.
  - **Food buffs (built):** each meal also gives a timed buff (4 real minutes): Bread gives **stamina regen +50%**, Roast Turnip gives **+25 max health**. At full health, F eats only a meal whose buff you don't already have. Buffs show above the health bar, end on defeat, and are mirrored to every player. The values live on the item (`BuffKind`, `BuffAmount`, `BuffSeconds` in its `.tres`).
- **Village of Ashby** (~10 min walk): a trader buys surplus and sells seeds and blueprints. A quest board. Later it sends **workers** who run your stations automatically, so the economy keeps going while you're away.
  - **Built (vertical slice):** Ashby sits at the south end of the vale below the plateau: five houses around a well, lamps, a notice board, and **Marta the Trader**. It's a sanctuary (28 m): wolves and bandits won't enter or hunt inside.
  - Marta sells wheat seeds (2), turnip seeds (3) and iron nails (5), and buys wolf pelts (8), wheat (1), turnips (2), bread and roast turnips (4). Prices live on each item (`BuyPrice` / `SellPrice` in its `.tres`), so new goods just need a price.
- **Coin** comes from selling goods and bandit bounties. It's spent on blueprints, workers and land claims.

## 5. Building
- **Placement:** grid snap (0.5 m), rotation in 45° steps (free rotation later), a ghost preview showing green for valid and red for invalid.
- **Claim radius:** you can only build near your home. Upgrading the house tier widens the claim. Later, outposts make new claims.
- **House tiers:** Shack → Cottage → Longhouse → Keep. Each tier unlocks building types and worker slots.
  - **Cottage (built):** pay at the **Home Plans** board by the shack: 40 wood, 20 stone, 6 iron nails, 3 wolf pelts and the Cottage Blueprint (from the bandit captain). The shack is replaced by a timber-and-plaster cottage you can walk into, with two beds. Your land claim grows from 30 m to 42 m. The cottage's footprint is kept clear of new buildings while you still live in the shack; anything already there is packed up and refunded when it goes up. The tier is saved and sent to every player.
- **Stations:** Workbench, Sawhorse, Kiln, Smelter, Forge, Anvil, Cookfire, Loom, Storage Chest, Well, Farm Plot. Built so far: Workbench, Campfire (cooking), Palisade, Bedroll, the farm plots, **Well** and **Storage Chest** (8 wood; one already stands by the shack).
- **Defense:** palisade → stone wall → gatehouse → watchtower. Bandit raids target bases above a "wealth" threshold.

## 6. Weapons & combat
- **Sword tiers:** Wooden → Stone Club → Bronze → Iron → Steel → Ember-forged.
- **Moves:** light attack (LMB), heavy attack (Q or middle mouse), **block** (hold RMB), **parry** (raise the block just before a hit lands), **dodge roll** (Ctrl or C). All of them use **stamina**.
- **Dodge roll (built):** a 0.38 s burst (10.5 m/s, easing off) in the direction you're moving, or backwards if you're standing still. It costs 22 stamina, with a short cooldown after. For the first 0.32 s nothing can hit you (`Net.ServerDamagePlayer` returns *Dodged*, the same rule for enemies and PVP). Rolling drops your guard, and you can't attack mid-roll. In third person it plays the Rogue's Dodge animations (forward / back / left / right); in first person the camera dips and tilts.
- **Block / parry (built):**
  - A raised guard stops hits from the front (about ±70° of where you look). A blocked hit does 20% damage and costs stamina (8 + 0.8 × the hit). Run out of stamina and the guard breaks (you can't raise it for 1.2 s).
  - Guard raised **0.3 s or less** before the blow lands = **parry**: no damage, some stamina back, and the attacker reels (wolves 1.6 s, the captain 1.2 s). In PVP, a parried player can't swing for 1 s.
  - While guarding you walk at half speed, can't sprint or attack, and stamina regenerates slowly.
  - Everything that hurts a player (enemies and PVP) goes through one server function, `Net.ServerDamagePlayer`, so the rules are the same everywhere.
- **Classes (built):** pick one in the character creator. You can change class only at home (inside the land claim plus 6 m), so it's a choice, not a mid-fight swap. Each class has passives and one ability on **R** (or mouse button 4), with its own stamina cost and cooldown (the HUD shows it next to the bars).

  | Class | Passives | Ability |
  |---|---|---|
  | **Warrior** | +30 max health; heavy attacks ×1.25; blocking costs 30% less stamina | **Whirlwind** (30 stamina, 7 s): a spin that hits everything within 3.2 m for 1.3 × your light damage |
  | **Rogue** | 10% faster; light attacks ×1.2; dodge costs 40% less and its i-frames last 35% longer | **Vanish** (25 stamina, 14 s): unseen for 4 s. Enemies lose you, and your next strike does double damage and ends it |
  | **Wizard** | −20 max health; stamina comes back 25% faster | **Firebolt** (16 stamina, 0.9 s): a fireball that does 22 damage and 10 splash within 2.5 m |
  | **Healer** | food heals 50% more; slowly regenerates health (1.5/s after a few seconds out of harm) | **Mend** (30 stamina, 10 s): heals you and every player within 9 m by 30 |
  | **Archer** | 4% faster | **Shoot** (8 stamina, 0.7 s): an arrow (46 m/s, slight drop) that does 16 damage, ×1.5 beyond 15 m |

  - **How it works:** classes are data (`PlayerClasses`). `PlayerAbility` checks cooldown and stamina on your own machine and plays the animation at once; the server (`Net.S_Ability`) re-checks the cooldown and does the real thing. Arrows and firebolts are simulated on the server (`Projectile`) and every screen flies a cosmetic copy. Projectiles skip trees and rocks, and follow the PVP rules for other players.
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

### Character customisation (built)
- Open the creator with **Customise character…** on the main menu, or with **Character…** in the Esc menu at any time in game. It shows a live 3D preview (drag to turn).
- **Body:** Wanderer (the hooded Rogue) or Knight. Both share the same skeleton and animations, so either body can use any move.
- **Colours:** skin (7 tones plus a pale green), hair, outfit (the Knight's cape and cloth trim), and trim (the Wanderer's trousers and gloves, the Knight's armour). Each has 10–11 presets, and "–" keeps the model's own colour.
- **Gear:** cape on or off, and a helmet for the Knight. **Height:** 90–110 %. **Randomise** rolls a whole look.
- **How it works:** the KayKit models take every colour from an 8 × 4 grid of gradient swatches in one texture. The character shader recolours the swatches each slot uses (`Appearance.Slots`), keeping each swatch's light-to-dark gradient, so a recoloured part still shades like the original. Each painted player gets its own materials; unpainted models still share theirs.
- **Where it lives:** your look belongs to you, not to the world. It's kept in `user://settings.cfg` (`[look]`), sent in the join hello (protocol 4), and echoed to everyone. Changing it in game rebuilds your body on every screen, and your held weapon moves to the new hand. The server cleans up looks it receives: unknown bodies, bad colours and out-of-range heights fall back to defaults.

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
- **Body:** a low-poly faceted wolf built in code (tapered 5- and 6-sided segments, hind legs with a backward hock, bushy tail, glowing eyes), with the character toon shader and outline. It is animated procedurally: trot, gallop, idle (breathing, looking around, sniffing) and the night howl.
- **Pathing (built):** enemies follow a navmesh. The server bakes it at startup from the world's static colliders (terrain, rocks, trees, the shack, Ashby, the camp, placed buildings) and bakes again 1.5 s after a building goes up or comes down or the home is upgraded. Paths are re-asked twice a second; within 2.5 m of their goal enemies go straight at it, and a short feeler ray still nudges them around moving things. See `WorldNav` and `Enemy.Steer`.

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
- **Art pass, round 1 (done):**
  - The ground is a painterly terrain shader: grass in broad patches of three tones with brush flecks, dark rock on the sides, a warm golden-hour rim, and darker when wet.
  - Grass, leaves and flowers use a foliage shader: alpha-cut, double-sided, toon-lit, swaying in the wind (gustier in rain).
  - The pool and stream use a stylized water shader: ripple bands, a sky-coloured sheen at grazing angles, raindrop rings.
  - Cloud clusters have a crown of rounder billows over a flat base, and shaded undersides.
  - Wolves have a faceted low-poly body instead of boxes.
  - New props: a stone well, a storage chest, a rain particle layer around the camera.
- **Art pass, round 2 (done):**
  - **Characters** (the hero, Marta, bandits, wolves) use their own toon shader: a hard two-band light ramp with a small highlight band, shadows lifted with dusk violet instead of going black, and a warm rim on the edge facing the sun. They also get an ink outline (an inverted hull, constant width in metres, dimmer at night) so they read against busy scenery. See `StyleMaterials.ApplyCharacter` / `CharacterFlat`.
  - **Wolf animation:** knees and hocks bend, so paws lift as the leg swings forward. The trot turns into a gallop above about 5 m/s (front pair, then hind pair, the body rocking). Standing wolves breathe, look around and sniff the ground. At night an idle wolf sometimes throws its head back and howls (cosmetic, decided on each machine). The bite lunge, stagger and death tweens are unchanged.
  - **The volcano** is a sculpted cone built in code: faceted and ridged with gullies, a concave profile (gentle foot, steep summit), a lower shoulder peak, and a crater breached toward the home cliffs. Four lava rivers pour out of the breach and fan down the flank. It is drawn without fog, with haze painted into its colours, so it keeps its facets 1 km away.
  - **Ashby** already uses the village model kit (plaster and brick walls, tiled roofs, doors, chimneys), so it needed no greybox replacement.
  - **Audio:** see §12a.
- **§12a Audio (built):**
  - Every sound is synthesized by `tools/make_audio.py` (numpy; noise and oscillators, no recordings and no licences) into `assets/audio/*.wav`. To replace one with a real recording, keep the file name.
  - **Ambience bed** (`Ambience`, mixed for each player): wind (stronger high up and in rain), rain (follows the weather), birdsong by day, crickets at night.
  - **Positional loops** (`Sfx.Loop`): every waterfall roars, louder for the big ones; campfires crackle; the volcano rumbles far off.
  - **Effects** (`Sfx.At` / `Sfx.Ui`): footsteps, light and heavy swings (timed to the strike), hits, block thud and parry clang, dodge whoosh, chopping and mining, cutting grain, wolf snarl, yelp and howl, eating, a soft pop when something enters your pack, building, crafting, drawing water, and the chest.
  - Effects play on every machine from events everyone already sees (attack and guard RPCs, health changes, building spawns), so nothing new is sent over the network. A dedicated server plays nothing. For 2.5 s after a world loads, effects are held back so a join snapshot doesn't play every building at once.
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
| Dodge roll | Ctrl or C |
| Look | Mouse |
| Toggle 1st / 3rd person | V |
| Light / Heavy attack | LMB / Q or middle mouse |
| Block (time it to parry) | RMB (hold) |
| Class ability | R or mouse button 4 |
| Interact (stations, beds, plots, wells, chests, trader, signs) | E |
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
3. **Vertical slice (done):** wolves with AI, block/parry, farm plots and cooking, Ashby village with a trader, one bandit camp, house tier 2 (cottage). Follow-ups done: watering and rain, food buffs, dodge roll, navmesh pathing for enemies, carried packs with storage chests. Next: playtest it on two real PCs and tune the numbers.
4. **Art pass (done, see §12):** terrain, foliage and water shaders, volumetric clouds, faceted and animated wolves, the character toon ramp with outlines, the sculpted volcano, and synthesized audio (ambience and effects). Later polish: real recorded audio, music, a sculpted rock kit for the cliffs.
5. **Falls Valley:** climbing, glider, updrafts, the second sanctuary.
6. **Volcano:** heat system, lava hazards, Ember Ore, the Sacred Forge.
7. **Cities and workers:** automation and the regional economy.

## 15. Code architecture notes
- **Enemies:** `Wildlife` (Main/Wildlife) spawns and replicates every hostile (wolf packs, the bandit camp); the server runs their AI, and clients show positions sent 15 times a second. `Enemy.cs` is the shared brain (prowl around a group anchor, chase, telegraphed windup, strike, stagger, flee). A new enemy type subclasses it with its numbers, body and animations (see `Wolf.cs`, `Bandit.cs`) and registers its kind in `Enemy.Create`. Strikes on players go through `Net.ServerDamagePlayer` (block / parry).
- **Building state:** a placed building with state (a farm plot's crop) implements `INetState`. The server changes it and calls `Net.BroadcastBuildingState`; the state is also in the join snapshot and the save.
- **World:** `HomeCliffs` builds the plateau; `HomeCliffs.Ashby.cs` (same class) builds the vale, Ashby, the bandit camp and the home tiers (rebuilt on `GameState.HomeTierChanged`).
- **Components:** `Health` (anything that can be damaged), `Harvestable` (resource nodes), and the `IInteractable` interface (stations, beds, signs, shrines, wells, chests).
- **Supplies (`Supplies.cs`):** the one place that decides where items go and come from. `Give` puts gathered things and loot in a player's pack (capped at 40; coins and the blueprint always fit). `TryConsume` pays from the pack first, then from the stockpile if the player is at home. `Deposit` empties a pack at a chest. Server code never adds gathered items to `GameState.Inventory` directly.
- **Audio (`scripts/audio`):** `Sfx` plays one-shots and loops by name from `assets/audio`, and `Ambience` mixes the background bed. Each machine plays sounds from events it already receives.
- **Weather (`Weather.cs`):** the server rolls the showers and calls `Net.ServerSetRain` (in the join snapshot too), and waters the plots while it rains. Every screen draws the rain and reads `Weather.Wetness` (sky, fog, the `wetness` shader global).
- **Navigation (`WorldNav.cs`):** server-only `NavigationRegion3D` that parses the `nav_source` group (HomeCliffs and everything under it).
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
| The **stockpile**: everything brought home, crafted or harvested | Your **pack**: what you've gathered, looted or bought and not yet stored (40 items; coins weigh nothing) |
| | The weapon in your hand (taken out of the stockpile; it returns when you swap or leave) |
| | Food buffs |
| Time of day, the day count | Respawn shrine |
| Resource nodes (a felled tree is felled for all) | Camera view (1st/3rd person) |
| | Your look and class (body, colours, gear, height): kept in your own settings, not the world save |

- **Packs (built):**
  - Chopping, mining, cutting grain, wolf pelts, bandit coins and the blueprint, water from a well, and anything bought from Marta go into **your own pack**. Empty it into the shared stockpile at a **storage chest** (one by the shack; build more).
  - Spending takes from your pack first, then from the stockpile, but only at home (inside the land claim plus 6 m). That covers building, crafting, cooking, planting, watering, eating and the cottage.
  - **Trading uses your pack only.** Carry goods and coins to Ashby and back.
  - A full pack (40) refuses more and warns you.
  - Being defeated drops **half of each stack** in your pack. Packs are saved with the player and mirrored only to their owner.
- Players are saved by name: rejoining puts you back where you logged out, with your respawn shrine and your pack.

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

