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
  - Marta sells wheat seeds (2), turnip seeds (3), iron nails (5) and wolf pelts (18), and buys wolf pelts (8), wheat (1), turnips (2), bread and roast turnips (4). Prices live on each item (`BuyPrice` / `SellPrice` in its `.tres`), so new goods just need a price.
- **Tools and mining (built):** trees want an **axe**, rocks and ore want a **pickaxe**. With the right tool a hit does the node's full damage × the tool's power (×1.5 on a heavy swing). Anything else, a sword or bare hands, is capped at 8 a hit, so it still works but slowly. **Iron ore** can't be mined without a pickaxe at all.
  - Stone Axe (3 wood + 2 stone) and Stone Pickaxe (3 wood + 3 stone) at the workbench, power 1. Iron Axe and Iron Pickaxe (2 iron ingots + 2 wood), power 2.
  - **Iron ore** veins (rust-red rocks with orange nuggets) sit in the vale, in Falls Valley and on the north ridge; they come back after 5 minutes. **Smelt** 2 ore + 2 wood into an iron ingot at the campfire or the hearth.
  - This is an early, small iron tier ahead of the full iron region (§11 Step 2), so the starting sword has something to grow into before the captain.
  - Tools go in the hotbar like weapons; in first person each one is a real model held in a gloved hand (`HeldModels`), and an empty hand shows a fist.
- **Coin** comes from selling goods and bandit bounties. It's spent on blueprints, workers and land claims.

## 5. Building
- **Placement:** grid snap (0.5 m), rotation in 45° steps (free rotation later), a ghost preview showing green for valid and red for invalid.
- **Claim radius:** you can only build near your home. Upgrading the house tier widens the claim. Later, outposts make new claims.
- **House tiers:** Shack → Cottage → Longhouse → Keep. Each tier unlocks building types and worker slots.
  - **Cottage (built):** pay at the **Home Plans** board by the shack: 40 wood, 20 stone, 6 iron nails, 3 wolf pelts and the Cottage Blueprint (from the bandit captain). The shack is replaced by a timber-and-plaster cottage you can walk into, with two beds. Your land claim grows from 30 m to 42 m. The cottage's footprint is kept clear of new buildings while you still live in the shack; anything already there is packed up and refunded when it goes up. The tier is saved and sent to every player.
- **Stations:** Workbench, Sawhorse, Kiln, Smelter, Forge, Anvil, Cookfire, Loom, Storage Chest, Well, Farm Plot. Built so far: Workbench, Campfire (cooking and smelting), the **Hearth** in the shack and the cottage (rest to heal 50, cook, smelt; the shack's chimney stands on its mantel), Palisade, Bedroll, the farm plots, **Well** and **Storage Chest** (8 wood; one already stands by the shack).
- **Defense:** palisade → stone wall → gatehouse → watchtower. Bandit raids target bases above a "wealth" threshold.

## 6. Weapons & combat
- **Sword tiers:** Wooden → Stone Club → Bronze → Iron → Steel → Ember-forged.
  - **Built:** Wooden Sword (15, 0.45 s), Stone Club (24, 0.7 s), **Iron Sword** (32, 0.45 s, reach 2.2; 3 iron ingots + 2 wood).
  - **Armour on enemies (built):** `Health.Armor` takes a flat amount off each hit, never below 20% of it. Grunts have 5, the captain 14, so a wooden sword barely scratches the captain (about 4 a hit) while the iron sword does about 18.
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
  | **Archer** | 4% faster | **Shoot** (8 stamina, 0.7 s): an arrow (46 m/s, slight drop) that does 16 damage, ×1.5 beyond 15 m. Carries a recurve bow (built in code, `BowView`): held up on the left in first person with an arrow nocked, in the left hand in third; each shot draws, snaps and re-nocks |

  - **How it works:** classes are data (`PlayerClasses`). `PlayerAbility` checks cooldown and stamina on your own machine and plays the animation at once; the server (`Net.S_Ability`) re-checks the cooldown and does the real thing. Arrows and firebolts are simulated on the server (`Projectile`) and every screen flies a cosmetic copy. Projectiles skip trees and rocks, and follow the PVP rules for other players.

### Levels and abilities (draft, 2026-11-18)
- **Why:** gear and the house are the main power curve (pillar 2), but they move in big steps, one tier per region. Levels fill the gaps between them: something improves every session, and each class gets more to do than one button.
- **The rule: levels give options, gear gives power.** At level 20 a character's stats are about as much better as one armour tier, no more. Most of what a level brings is a **new ability** or a **talent choice**, not bigger numbers. A level-20 player in leather still struggles in the volcano.
- **One level per character, not per class.** You still change class at home (see Classes above). Your level comes with you, and the new class gets its own abilities and talents up to that level. Trying another class never means starting again.

**Earning XP.** Most XP comes from **firsts and milestones**, so it follows progress through the regions and grinding wolves doesn't help much:

| Source | XP |
|---|---|
| First kill of an enemy type | 50 |
| Repeat kills | wolf 4, harpy 5, bandit 6, raider 8 (later regions more) |
| A boss (the bandit captain, then each region's boss) | 200, rising by 100 a region |
| Lighting (first prayer at) a new sanctuary | 150 |
| Beating off a raid | 100 |
| Journal goals and errands | main goal 60, side goal 40, errand 30 |
| First craft of each item | 20 |
| A home upgrade (Cottage, Longhouse, Keep) | 250 |

- **Curve:** level *n* needs about 120 × *n*^1.4 XP. Tune it so the levels land roughly where the table below says.
- **Target levels:** about 5 by the end of today's content (Home Cliffs, Ashby, Falls Valley), 9 after the Hollow Mine, 13 after the Spire Cliffs, 17 after the Cinder Foothills, and the cap of **20** in the volcano.

**What a level gives.**
- **Every level:** +2 max health and +1 max stamina (+38 and +19 at level 20).
- **Levels 5 and 12: a new ability**, so each class has three. The new abilities get their own keys (see *Controls* below).
- **Levels 3, 8, 15 and 20: a talent:** pick one of two class passives. You can change talents for free at home, as with class.
- **Level 10 and 20:** a small cosmetic: a glow on your blossom charm slot, then a petal trail for your cape. Shown to everyone.

**New abilities:**

| Class | Level 1 (built) | Level 5 | Level 12 |
|---|---|---|---|
| **Warrior** | Whirlwind | **Charge:** rush 8 m forward. The first enemy hit is knocked down for 1.2 s | **War Cry:** you and players within 9 m deal +20% damage for 8 s, and enemies within 5 m stagger |
| **Rogue** | Vanish | **Shadowstep:** appear behind an enemy within 12 m. Your next strike within 2 s is a critical hit | **Venom:** for 10 s your hits poison (4 damage a second for 5 s, stacking up to 3) |
| **Wizard** | Firebolt | **Frost Nova:** enemies within 5 m are slowed 50% for 3 s. In heat zones it also cools you and players nearby (empties 25% of the heat meter) | **Meteor:** pick a spot within 25 m. After 1 s it lands for 60 damage within 4 m |
| **Healer** | Mend | **Blossom Ward:** a 5 m circle of petals for 8 s. Enemies won't enter it, like a sanctuary | **Rekindle:** a downed player within 12 m gets up at once with half their health, with no need to stand over them (see §16, *Downed and revived*) |
| **Archer** | Shoot | **Volley:** 5 arrows in a 30° fan, 10 damage each | **Mark:** tag an enemy for 10 s. Everyone deals +25% damage to it and it shows through walls |

**Talents (built for levels 3 and 8):** pick one of each pair on the **Talents** tab of the character sheet (I). The first pick at a level works anywhere; changing it later is free but only at home. Picks are kept per class, so trying another class and coming back keeps them.

| Class | Level 3 | Level 8 |
|---|---|---|
| **Warrior** | *Bulwark:* a clean parry gives back 10 more stamina · *Brute:* heavy attacks hit 15% harder | *Iron Skin:* +5 armour, whatever you wear · *Cleave:* Whirlwind reaches 1 m further |
| **Rogue** | *Light-footed:* no fall damage from drops under 8 m · *Pickpocket:* bandits you defeat drop 50% more coin | *Ambush:* Vanish lasts 2 s longer · *Fleet:* sprinting costs 30% less stamina |
| **Wizard** | *Kindling:* Firebolt's burst scorches 1 m further · *Focus:* Firebolt costs 4 less stamina | *Pyromancer:* Firebolt's direct hit does 6 more damage · *Glacial:* Frost Nova's slow lasts 2 s longer |
| **Healer** | *Herbalist:* cooking makes an extra meal 1 time in 3 · *Steady Hand:* Mend's cooldown is 2 s shorter | *Lifebloom:* Mend heals 15 more · *Lifeline:* revive a downed friend twice as fast, and they get up with half their health |
| **Archer** | *Long Draw:* the long-shot bonus starts at 10 m, not 15 · *Quick Nock:* Shoot's cooldown is 0.15 s shorter | *Piercing:* your arrows go straight through armour · *Fleetfoot:* 6% faster on your feet |

- Later talents should connect to region hazards where they can, as Frost Nova does with heat: for example a climbing talent for the spires, or a light-radius talent for the mine.

**Co-op (§16).**
- Everyone within 60 m gets **full XP** for a kill. Nothing is split, so playing together is never slower.
- World milestones (a sanctuary lit, a raid beaten off, a home upgrade, a boss) give XP to **every player online** at the time.
- **Catch-up:** a player below the highest level online earns **double XP**, so a friend who joins late soon gets close.
- Enemy scaling (`Difficulty`) still counts players only, never levels.
- **Where levels show:** only on each player's **nameplate** over their head ("Mira · 7"). They aren't on the player list (Tab) or anywhere else, so other players can see them without it turning into a leaderboard.

**Saving and code.**
- Level, XP and talents are saved with the character in the world save, like the pack and worn gear (§16). Class stays in your own settings, as now.
- **Built:** `Levels` holds the curve and the XP table. A player's progress is one number, their total XP, and the level is worked out from it. The server awards XP (`Net.Xp.cs`: `ServerAwardXp`, `ServerXpForKill`, `ServerXpForCraft`) and sends every machine the new total (`Net.C_Xp`). The total is part of each player's info in the snapshot (protocol 9). Each character's "firsts" (first wolf, first glider) are saved with it, so a first is paid once per character. Journal goals count as world milestones: everyone online gets them.
- **Built:** the level-5 abilities are data in `PlayerClasses` (`AbilityDef`, with its own stamina, cooldown and `UnlockLevel`), and `PlayerAbility` has one node per slot (`Ability` on R, `Ability2` on T). The server re-checks the level. Numbers chosen for them: Charge 25 stamina / 9 s (strikes for a light attack's damage); Shadowstep 20 / 10 s (a critical hit is double damage); Frost Nova 24 / 12 s; Blossom Ward 35 / 24 s (it's a temporary sanctuary, so enemies also stop hunting anyone inside it); Volley 22 / 6 s.
- **Built:** talents are data in `Talents` (`TalentDef`; `Has`, `Unpicked`, and `Tune`, which applies cost and cooldown talents to an `AbilityDef`). A player's picks are a map from "class:level" to talent id, saved with the character, sent in each player's info (`"talents"`) and by `Net.C_Talents` (`Net.Talents.cs`; the server checks class, level and home in `S_Talent`). Protocol 10. Reaching level 3 or 8 tells you to open the Talents tab.
- Still to build: the level-12 abilities on the third slot (Rekindle among them), and the level-15 and 20 talents.
- The HUD has an XP bar under the stamina bar with the level beside it, a "+50 XP" note for anything but a repeat kill, and a "Level up" banner (built). The character sheet (I) has three tabs: Equipment, Talents and Journal (the pad's bumpers cycle them).

**Controls.**
- Keyboard: **R** (or mouse button 4) for the first ability, as now, then **T** for the level-5 ability and **G** for the level-12 ability.
- Pad: **Y / △** for the first ability, as now. **Hold LB / L1 and press Y / △** for the level-5 ability, and **hold LB / L1 and press X / □** for the level-12 ability. Tapping LB alone still eats. Eating moves to when LB is *released* without another button pressed, so holding it for an ability doesn't also eat.
- The level-5 key is built (in the controls table, §13); the level-12 one (G, LB + X) comes with those abilities.

**Blessings vs. levels (decided).** Blessings (§10) stay separate. They're bought at sanctuaries with Blossom Petals and give small stat boosts that every class shares (max health, max stamina, pack size). Levels give abilities and talents. Neither replaces the other.
- **Enemies:**
  - Start: wolves (packs at night), bandit scouts.
  - Mid: bandit camps (a leader drops a blueprint), boars, cliff harpies.
  - Late: fire-touched creatures, magma golems (volcano).
- **Raids (built):** bandits sometimes march on your base at dusk. Walls and towers matter.
  - **When:** from day 3, once you have at least 3 buildings. Each dusk there's a 45% chance, at least 2 days apart, and one is certain after 4 days without. A horn sounds and everyone gets a warning.
  - **Who:** raiders, bandits carrying torches (55 health, 10 damage). There are 3 for one player, and more with more players (`Difficulty.PackSize`). They gather on the vale road and march north.
  - **What they do:** they fight players who come within 11 m. Otherwise they go for the nearest building, chests first, because they want the stockpile. Each blow on a chest steals up to 4 items (12 coins) from the stockpile. Buildings take damage and are wrecked when it runs out, with no refund. Hit points: palisade 150, watchtower 300, well 140, storage chest 120, workbench 90, campfire 60, plots 50, bedroll 40.
  - **Walls:** raiders prefer anything but walls. When a wall blocks their way (they stop making progress), they hack it down.
  - **Watchtower (built):** 16 wood and 6 stone, on a 2.4 m footprint. The server looses an arrow every 1.3 s at the nearest hostile it can see within 26 m: raiders, wolves or harpies. It leads moving targets and allows for drop. Tower arrows never hurt players, even with PVP on. Its kills' loot goes into the stockpile.
  - **Ending a raid:** killing a raider returns what it stole to the stockpile, plus its coins. When all of them are dead the raid is beaten off. Survivors slink off in the morning with their loot.
  - **Repairs:** at dawn every damaged building is patched up for free.
  - Code: `Wildlife` (`MaybeRaid`, `StartRaid`, `RaidStrike`), `Bandit` (raider objective), `Watchtower`.

### Armour, gear and enchantments (built: the system and tiers 0–1; later tiers planned)
- **Why:** gear is the long-term thing you work toward between base upgrades. Every zone's hazard has an answer you can make (wet rock → grip boots, long falls → the feather cloak, heat → heat-ward armour), so exploring feeds crafting, and crafting opens up exploring.
- **Slots (6):** Head, Body, Hands, Feet, Cloak, Charm. Weapons stay on the hotbar (1–5) as before.
- **Personal:** armour is crafted into the crafter's pack, stays there when you empty your pack at a chest, and is never lost on defeat. What you wear is saved with your character in the world save, like your pack.
- **Wearing it:** press **I** (inventory) to open your character sheet. Click a piece in your pack to put it on (it swaps with what's in that slot), and click a slot to take it off. Worn pieces don't count toward pack load.
- **Armour value:** each piece has armour points. Total armour **A** cuts damage from every hit (enemies, PVP, raiders, harpies; not falls) by **A / (A + 40)**: 13 armour (a leather set) takes 25% off, 26 takes 39% off, 60 (late game) takes 60% off. Blocking and parrying work as before, on what's left.
- **Weight:** metal pieces slow stamina regeneration a little (−5% each), so a Rogue or Wizard may prefer leather. No durability: gear never wears out or needs repairing (it's a cosy co-op game, not a chore list).
- **Looks:** body armour recolours your outfit and trim to its own colours. Head armour puts on a helmet (the Knight's own helm; on the Rogue body, a cap), and a cloak puts your cape on in its colour. Everyone sees it.

**Built pieces (crafted at the workbench):**

| Piece | Slot | Armour | Special | Recipe |
|---|---|---|---|---|
| Leather Cap | Head | 3 | — | 2 wolf pelts |
| Leather Jerkin | Body | 6 | — | 4 wolf pelts, 2 wood |
| Leather Gloves | Hands | 2 | — | 1 wolf pelt |
| Leather Boots | Feet | 2 | — | 2 wolf pelts |
| Grip Boots | Feet | 2 | Climbing costs 35% less stamina; wet rock is no worse than dry | 2 wolf pelts, 3 harpy feathers, 2 stone |
| Traveller's Cloak | Cloak | 1 | Stamina comes back 15% faster | 3 wolf pelts, 2 wheat |
| Feather Cloak | Cloak | 1 | Fall damage −60% | 1 wolf pelt, 6 harpy feathers |
| Iron Helm | Head | 6 | Heavy (−5% stamina regen) | 8 iron nails, 1 wolf pelt |
| Studded Jerkin | Body | 10 | Heavy (−5% stamina regen) | 4 wolf pelts, 10 iron nails |
| Wolf-tooth Charm | Charm | 0 | Melee damage +10% | 2 wolf pelts, 15 coins |
| Blossom Charm | Charm | 0 | +15 max health | Bought from Marta in Ashby (60 coins) |

**Enchantments (built):**
- Built at a **Rune Altar** (build mode: 14 stone, 4 wood). Each enchantment costs **2 rune shards + 10 coins**.
- **Rune shards** drop from raiders (1 each), the bandit captain (3) and sometimes harpies (1 in 3). Marta sells them for 30 coins and buys them for 8.
- Each piece holds **one** enchantment. Enchanting it again replaces the old one. An enchanted piece is a separate item: "Leather Cap of Thorns" (id `leather_cap@thorns`). That keeps packs, saving and the network unchanged.
- Enchantments add up across pieces:

| Enchantment | Effect (per piece) |
|---|---|
| of Warding | +4 armour |
| of Vigor | +12 max health |
| of Swiftness | +5% move speed |
| of the Feather | −30% fall damage |
| of Thorns | melee attackers take 20% of the damage they dealt you |
| of Leeching | heal 6% of the melee damage you deal |

**Planned (later tiers):**
- **Tier 2, bronze:** Bronze Cap and Scale Shirt (the smelter).
- **Tier 3, iron:** a Chain Mail set and a **Shield** for a new Off-hand slot: blocks cost less stamina and the parry window grows.
- **Tier 4, steel:** Plate (armour 14 for the body, heavy).
- **Tier 5, ember:** **Heat-ward** armour and cloak: the volcano's heat meter rises more slowly.
- **Furs** against the night mist (if it starts to sting), from boar or bear hides.
- **Set bonuses**, such as 4 leather pieces giving +10% stamina regeneration.
- **Dyes** at a loom, to recolour armour.
- Unique **named pieces** from bosses (the bandit captain's helm, the Ember Warden's crown).
- Code: `Gear` (slots, stats, enchantments), `ItemDef` (armour fields), `Net.S_Equip` and `S_Enchant`, `Hud` character sheet, `CharacterModel` (looks).

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
- **Where it lives:** your look belongs to you, not to the world. It's kept in `user://settings.cfg` (`[look]`), sent in the join hello (since protocol 4), and echoed to everyone. Changing it in game rebuilds your body on every screen, and your held weapon moves to the new hand. The server cleans up looks it receives: unknown bodies, bad colours and out-of-range heights fall back to defaults.

## 8. Traversal (verticality)
- **Built:** walk, sprint (stamina), jump, fall damage, dodge roll, climbing, the glider, waterfall updrafts, river currents.
- **Fall damage (built):** drops up to 4.5 m are safe; each metre beyond costs 6 health (8 m: 21, 15 m: 63, 25 m: 123), worked out from the landing speed. Gliding and water break a fall; the feather cloak and "of the Feather" take some off. A hard landing rumbles the controller.
  - Code: `PlayerController.FallDamage`; the owner reports it and the server applies it (`Net.S_FallDamage`).
- **Climbing (built):**
  - Climbable rock is pale and rough, with dark handholds. Walk into it (hold W) to grab on.
  - On the face, W and S move you up and down and A and D move you sideways. Space leaps off.
  - It costs stamina: 7 a second while moving, 2.5 while hanging still, and stamina doesn't come back while you climb. **Wet rock** (rain) costs 1.6 times as much. At zero your grip fails and you fall.
  - At the top you haul yourself over the edge (mantle).
  - You can't attack or use abilities while climbing.
  - Animation: KayKit has no climbing clip, so `ClimbAnimation` builds two from the skeleton at load: hand over hand with the opposite knee drawn up (played backwards on the way down), and a breathing hang.
  - Code: `PlayerController.ClimbStep`. Climbable bodies are in the `climbable` group.
- **Glider (built):**
  - Crafted at the workbench from 6 wood, 2 wolf pelts and 4 harpy feathers.
  - It's personal gear: it goes into the crafter's pack, stays there when you empty your pack at a chest, and isn't lost on defeat.
  - Press **Space in the air** to open it, and Space again to fold it. It also folds when you land.
  - It sinks at 2.2 m/s and flies at 9 m/s in the direction you look. W speeds up, S slows down, A and D bank.
  - Look down to dive: the further down you look, the faster you sink (up to 14 m/s) and the faster you fly (up to +40%).
  - Gliding costs 5 stamina a second, and stamina doesn't come back while you glide. Riding an updraft is free. Out of stamina, the glider folds and you fall. You need 5 stamina to open it.
  - Landing under the glider does no fall damage, even out of a dive.
  - Without a glider, a long fall shows a hint once.
- **Waterfall updrafts (built):** mist columns at waterfall bases (the Great Falls in Falls Valley, and the pool below the home west falls) lift a glider at 8 m/s up to their top (36 m and 26 m). Inside a column you hang and turn instead of racing through it.
- **River currents (built):** wading in the Falls Valley river slows you and carries you downstream at 3.2 m/s, toward the cliff edge.
- **Swimming (built):** the home pool and the Great Falls plunge pool are sunk 2.8 m into the ground (`HomeCliffs.Pool`; `GameState.Waters`). Water deeper than 1.2 m at your feet and you swim:
  - You float with your head and shoulders out. WASD strokes at 3 m/s (sprint 4.8 m/s for 12 stamina a second).
  - Look down as you swim forward to dive, look up to rise, or hold **Space** to come up. Let go and you bob back to the surface.
  - With your head under, you hold your breath: 9 stamina a second, and no regeneration. Out of breath, you drown (12 damage a second) until you surface. The screen turns blue-green under water.
  - Swim forward into an edge to haul yourself out.
  - Water breaks a fall: dive in from the cliffs or the glider with no damage. A glider folds when you hit the water.
  - No attacks, guard, rolls or class abilities while swimming.
  - Waterfalls can't be climbed without the late-game Grapple Hook. Rivers stay wading depth (currents, above).
- **Co-op:** everyone sees climbing, gliding and swimming. The movement state carries a mode (normal, climb, glide or swim), (protocol 5; swim from protocol 8). Protocol 6 added worn armour (`S_Wear`, `S_TakeOff`, `S_Enchant`, `C_Worn`; what's worn is part of each player's info in the snapshot). Protocol 7 added the shared journal (`C_Journal`, `S_Errand`; `"journal"` in the snapshot).
- **Grip boots (built):** less drain on wet rock (§6).

### Roads and trails (built)
- Unbroken dirt roads join every area: the yard → through the gap in the plateau wall → the vale → Ashby, a footpath from the road to the bandit camp, and a road east from the home road through the pass, over the stepping stones and on to the foot of the mesa's climbing face.
- Each trail is a polyline of road strips with a patch over every bend (`HomeCliffs.Trails`). Trees and rocks the scatter dropped on a trail are moved off it.
- Walking or running on a road, track, yard or square is **1.2× faster** (`HomeCliffs.OnPath`, `PathSpeedMult`).

## 9. Hazards and progression
| Zone | Hazards | Counter |
|---|---|---|
| Home Cliffs (start) | Falls, night mist (slow stamina regen), wolves | Light, shelter, food |
| Falls Valley (built) | Slippery wet rock (rain), river currents toward the cliff, harpies | Stamina, the glider, arrows and parries, grip boots |
| Forest Road / Ashby | Bandit camps, raids | Walls, better swords |
| Hollow Mine (planned, tier 2) | Darkness, tight tunnels, cave crawlers, flooded galleries | Torches and the lantern, bronze gear |
| Spire Cliffs (planned, tier 3) | Wind gusts that push gliders, long falls between spires, loose rock, storm harpies | Feather cloak, grip boots, the grapple hook, a shield |
| Cinder Foothills (planned, tier 4) | Steam vents, hot ground (a mild **heat meter**), ash-sick beasts, the bandit warlord's fort | Cool springs, steel gear, a first heat-ward charm |
| Volcano (late) | **Heat meter** (rises near lava and drains health when full), lava flows, ash storms, emissive lava falls | Heat-ward cloak, blossom charms, cool springs |

### Wolves (built)
- **When:** at nightfall a pack appears at the plateau's edge, away from every player. A second pack comes later in the night (2 per night). At dawn they give up and slink off.
- **Pack size:** 2 for one player, scaled up for co-op by `Difficulty.PackSize` (3 / 4 / 5 for 2 / 3 / 4 players). Health and bite never scale with players.
- **Dire wolves:** drawn 1.6× the modelled size (shoulder about player height). 170 HP, armour 6 (a wooden sword does about 9 a hit), a 24-damage bite every 1.1 s with 3.2 m reach. Four bites finish a fresh level-1 player, so early on the answer is to run (a sprint on a road, 9.6 m/s, outpaces their 7.2), get indoors, or parry. With the iron sword (26 a hit) a single wolf is a fair fight. A repeat kill is worth 15 XP and each kill leaves **2 pelts**.
- **Surviving the early nights (built):**
  - The **first night is quiet**: no packs, just a warning toast ("Tomorrow night the wolves will hunt: stay near a fire after dark").
  - **Fire keeps wolves off:** they won't hunt anyone within 7 m of a campfire or hearth, and they keep outside that ring. The hearth covers the whole shack, so going home at night is always safe, and a campfire (cheap) makes a safe camp anywhere.
  - Marta **sells wolf pelts** (18 coins, buys at 8), so the leather gear, glider and cottage can be bought with farm money instead of fought for.
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

### Falls Valley (built)
- **Where:** east of the home plateau, through a pass in the east wall (around z 34–51). A sign at the pass explains it.
- **Layout:**
  - Tall cliffs on the east (44–62 m) and to the south. The valley's north end drops into the cloud sea.
  - The **Great Falls** pour off the east cliff into a plunge pool, and its mist is an updraft.
  - A river runs west from the pool, then north off the cliff, where it spills as a second waterfall. Stepping stones cross it on the way from the pass.
  - The **mesa**: 28 × 28 m and 16 m high, with pale climbing faces on its west and south sides. The **Falls Blossom** sanctuary sits on top: pray there to set your respawn point and heal, and enemies keep out.
  - A **harpy roost**: a stick nest on a rock spire.
  - Trees, rocks and foliage, so you can gather there.
- The valley is built last, from its own random numbers, so the home plateau and the vale come out exactly as before.
- **Harpies (built):**
  - Small flying predators: 32 health and a 9-damage claw. There are 3 per roost for one player, more with more players. They never get tougher.
  - They circle about 11 m over the roost. When they spot you (26 m), they wheel overhead, out of reach, and swoop talons-first with a screech. After the strike they beat back up and come round again every 2.4 s.
  - Arrows and firebolts reach them anywhere. A sword only reaches them during the swoop.
  - **Parry the swoop** and the harpy tumbles to the ground, stunned for 1.6 s from when it lands. Finish it there.
  - Each drops **2 harpy feathers** (for the glider; Marta buys them for 5). A dead harpy falls out of the sky.
  - A cleared roost fills again the next day. Roost state is saved.
  - The body is a low-poly faceted raptor built in code (`Harpy`), with flapping two-part wings, talons and glowing eyes.

### Bandit camp (built)
- **Where:** off the road to Ashby, east of the track, about halfway down the vale (38, 0, 140). Tents, a fire, a stake fence with its mouth toward the road.
- **Who:** grunts (60 HP, armour 5, 10 damage, a slow 0.55 s swing) and one **Bandit Captain** (240 HP, armour 14, 34 damage, a heavier 0.75 s chop with a greatsword that reaches 4.4 m; he swings from 3.2 m). He's meant for after the iron sword, not day one. They hold the camp day and night, chase anyone within 15 m, and won't follow more than 30 m from camp.
- **Scaling (§16):** grunts are small enemies, so the camp gets more of them (2 / 3 / 4 / 5 for 1–4 players). The captain is a big monster: one of them, with scaled health and damage.
- **Loot:** 6 coins per grunt and 25 for the captain, into the shared stockpile. The captain also carries the **Cottage Blueprint** while your home is still a shack.
- **Respawn:** a cleared camp is re-occupied after 3 in-game days, when nobody is standing in it. The cleared state is saved.
- Bodies are the KayKit Knight with a red tint; the captain keeps his helmet, cape and spiked shield.

## 10. Sacred cherry-blossom sanctuaries
- Glowing cherry trees on cliff edges. **The first stands beside your shack.**
- Functions: **respawn point**, **fast travel** node, **safe zone** (enemies won't enter its light), and an altar for **blessings** (permanent upgrades bought with Blossom Petals from elite enemies and exploration).
- **Fast travel (built):** praying at a sanctuary **attunes** it for the whole world (the journal fact `prayed:<name>`, so co-op players share it and it's saved). The home blossom starts attuned. Praying opens the list of the other attuned sanctuaries, with their distances. Picking one takes you there, heals you and sets your respawn point there. Travel is personal (friends stay put), and you can't travel within 8 s of being hurt. Code: `BlossomShrine` (`All`, `Attuned`, `ServerTravel`), `Net.RequestTravel` / `S_Travel` (protocol 8), and the travel list on the `Hud` board.
- Each new sanctuary you light up opens the next region. That's the main progression gate, which ties exploration to power.
- Late game: blossom charms push back volcano heat, letting the calm side and the dangerous side of the world interact.

## 11. World map (modular)
```
                [Volcano — late, tier 5]
                      │
                [Cinder Foothills — tier 4]
                      │
 [Spire Cliffs] ── [Falls Valley] ── [Ashby Village] ── [Forest Road] ── [Cities…]
   tier 3             │                                      │
                [HOME CLIFFS — start]                  [Hollow Mine — tier 2]
```
- Each zone is a **separate scene**, joined by road or trigger connectors and loaded in the background. New zones (cities, mines, the volcano) are added by dropping in a scene and a connector, with no core code changes.
- **Home Cliffs layout:** a plateau of about 120 × 120 m. The shack and the sacred tree are on the north cliff edge, over a cloud sea. High rock walls stand to the east and west, with waterfalls. A road leads south toward Ashby, about a 10-minute walk. A pass through the east wall leads to **Falls Valley** (see §9).
- For now every zone is part of one generated scene (`HomeCliffs` and its partial files: `.Ashby`, `.Falls`, `.Volcano`). Splitting them into streamed scenes comes later.

### Region progression (draft, 2026-11-18)
- **The problem:** three regions are playable (Home Cliffs, the Ashby vale with the bandit camp, Falls Valley), and they cover economy tiers 0–1 only. There are no ores, no smelter and no metal items yet: iron nails are only bought from Marta, swords stop at the stone club, and the home stops at the cottage. Going straight to the volcano would jump from tier 1 to tier 5 and leave pillar 2 (*earned steel*) empty.
- **The rule:** each mid-game region brings **one metal tier**, **one new station**, **one blossom sanctuary** (§10), **one boss** whose drop opens the next step, and **one hazard** whose counter you craft from that tier or the one before it. The house tier follows the metals: Longhouse at tier 3, Keep at tier 4 (§5).
- **Order:** Hollow Mine → Spire Cliffs → Cinder Foothills → Volcano. Cities and workers can come in alongside, from the Longhouse on (it is the first tier with worker slots).

| Step | Region | Metal tier | New stations | Sanctuary | Boss → drop | House |
|---|---|---|---|---|---|---|
| 0 | (fill in tier 1) | Clay, planks, bricks | Sawhorse, Kiln | — | — | Cottage (built) |
| 1 | Hollow Mine | Bronze (copper + tin, charcoal) | Smelter, Anvil | Mine Blossom, in a sunlit shaft | Crawler Mother → Bellows Plans | Stone walls |
| 2 | Spire Cliffs | Iron | Bellows (smelter upgrade) | Spire Blossom, on the tallest spire | Storm Harpy Queen → Longhouse Blueprint | **Longhouse** |
| 3 | Cinder Foothills | Steel (coal + iron) | Blast Forge | Ashen Blossom, by a cool spring | Bandit Warlord → Keep Blueprint, Ember Pass key | **Keep** |
| 4 | Volcano / Ember Wastes | Ember steel | Sacred Forge | (the last sanctuary, at the crater rim) | Ember Warden → the Warden's crown | — |

#### Step 0: fill in tier 1 (no new region)
- **Clay** from the riverbanks in the vale and Falls Valley. **Sawhorse** (wood → planks) and **Kiln** (clay → bricks; wood → charcoal).
- Move the Cottage's iron nails onto a recipe later (nails from iron ingots at the anvil), but keep Marta selling them so the cottage stays reachable before the mine.
- Small, but it gives tiers 2–4 the refined goods they're priced in.

#### Step 1: Hollow Mine (bronze)
- **Where:** off the Forest Road past Ashby, an old mine dug into the valley wall. The road is the trip there (a short forest walk with wild boars, whose hides make the **furs** planned in §6); the mine is the region.
- **The hook:** the first place where **darkness** is the hazard. Torches can be placed on walls, and a crafted **Lantern** (Charm or a held tool) lights a circle round you. Night mist already teaches "light is safety"; the mine makes it the whole level.
- **Layout:** an upper gallery with copper and tin veins near the entrance, then deeper flooded galleries (once swimming exists, they hide side rooms). A collapsed shaft lets daylight in: the **Mine Blossom** grows there.
- **Enemies:** **cave crawlers**, low-poly faceted spiders built in code like the wolf and the harpy. They hate light: a torch or lantern makes them back off and only lunge from the dark edge, so lighting the mine is how you clear it. The **Crawler Mother** sits at the bottom.
- **Gear:** Bronze Sword, Bronze Cap and Scale Shirt (§6, tier 2), a bronze pickaxe.
- **Hands off:** the Crawler Mother drops the **Bellows Plans**, which you need for iron. The mine also has one locked iron gate with a note that points to the spires.

#### Early surface iron (built) and how it fits
- Small **surface veins** at home and in Falls Valley give enough iron for iron tools and the iron sword, the weapon that makes dire wolves and the bandit captain fair fights. Each vein holds a few ore and takes 5 minutes to come back, so iron stays scarce.
- The intended early path: wooden sword → stone pickaxe → surface iron → **iron sword** → the bandit captain (blueprint) → the cottage. Leather comes from pelts (wolves or Marta).
- Bronze (Hollow Mine) stays a sidegrade in damage, worth it for **armour** (bronze scale) and tools that mine faster. The Spire Cliffs keep the rich iron seams: enough for **chain mail, the shield and the iron helm in bulk**, plus the Bellows and the Longhouse. So the cliffs are where iron *armour* comes from, while the iron *sword* arrives early.

#### Step 2: Spire Cliffs (iron)
- **Where:** past Falls Valley, a field of tall rock spires over the cloud sea.
- **The hook:** the climbing and glider test. Iron shows as rust-red seams on the spire faces, so you mine it while hanging on. **Wind gusts** shove gliders sideways (shown by streaks in the air), and updrafts from cracks in the rock link one spire to the next.
- **The grapple hook** (already named in §8 as late game) is found or crafted here. It shoots onto marked anchor points, which keeps the routes between spires fair without lots of extra stamina.
- **Enemies:** **storm harpies** (a tougher roost type that dives in pairs) and goats that knock you off a ledge if you don't dodge. The **Storm Harpy Queen** nests on the tallest spire.
- **Gear:** Iron Sword, Chain Mail and the **Shield** in a new Off-hand slot (§6, tier 3). Grip boots and the feather cloak pay off here.
- **Hands off:** the Queen drops the **Longhouse Blueprint**. The Longhouse widens the land claim and opens the first **worker** slots (§14 step 12), so it's also where the economy starts running on its own.

#### Step 3: Cinder Foothills (steel)
- **Where:** between Falls Valley and the volcano, the burnt slopes at the volcano's foot.
- **The hook:** a gentle version of the volcano's **heat meter**. Steam vents and hot ground fill it slowly, and cool springs empty it. It teaches the endgame's system while it can't kill you quickly yet.
- **Coal** seams in the burnt ground, which with iron make **steel** at the **Blast Forge**.
- **Enemies:** ash-sick wolves and boars (darker, glowing, a bit tougher), and the **Bandit Warlord's fort**: the bandits from the vale were his outriders, which ties the raids on your base (§9) into one story. Clearing the fort stops raids for a while.
- **Gear:** Steel Sword, Plate (§6, tier 4), and the first heat-ward piece: an **Ember-glass Charm** that slows the heat meter a little.
- **Hands off:** the Warlord drops the **Keep Blueprint** and the key to the **Ember Pass**, the only way up to the volcano.

#### Step 4: Volcano (ember steel)
- As planned in §9 and §14: the full heat meter, lava flows, ash storms, Ember Ore, the **Sacred Forge**, and the Ember Warden. It now starts with players in steel and plate who already know how the heat meter works.

#### Open questions
- **Tool tiers:** does mining a vein need a pickaxe of the right tier (bronze for iron, iron for coal), or does any tool work and the region gate is enough? Tool tiers make the chain stronger but add a chore.
- **Forest Road:** does it get a camp, a toll gate or a small hamlet, or stay a short walk to the mine?
- **Fast travel** (§8, planned) becomes more important with every region. Build it before or with step 2.
- **Swimming** (§8, planned) is what the flooded galleries in step 1 would use. Make those rooms optional until it exists.

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

### UI (built: the medieval kit)
- **Look:** oak-plank boards in an **ornate gold frame** (corner filigree and garnets), walnut plank buttons with gold rims, recessed leather item slots, parchment tooltips and notes, a deep red **title plate**, and a gold flourish rule between sections. Text is a classic serif (Palatino Linotype, which ships with Windows; drop `title.ttf` / `body.ttf` into `assets/ui/fonts` to bundle another), parchment-coloured with a dark outline over the world.
- **Art:** painted from code by `tools/make_ui.py` (numpy only): anti-aliased signed-distance shapes, value-noise wood grain, and gold shaded from a height field like embossed metal. It writes the 9-slice kit and a 64 px **icon for every item**. Re-run it after adding items. Enchanted pieces show their base icon with a sparkle.
- **Theme:** one Godot `Theme` built in `UiTheme` and given to the HUD, the main menu and the character creator. It has type variations for titles, headings, small text, plates, parchment, insets and slots.
- **HUD:**
  - Framed health and stamina bars; a pale trail shows recent damage.
  - Class and ability above the bars; the day and time on a plate.
  - Pack and stockpile as icon grids (the stockpile fades when you're away from home).
  - A five-slot weapon hotbar with key numbers; the held weapon glows.
  - Key-cap prompts ("E" Sleep), parchment toasts that fade in and out, and a build-mode plate.
- **Boards:** crafting, trading and enchanting share one framed board. Each row shows the item's icon, a note, its cost as icons (red when you're short) and a button. The **Character & Equipment** board (I) has a paper doll of the six armour slots plus a main-hand slot, a **Weapons & tools** grid (click one to hold it, fists included), a parchment stat card and the pack. There are also the Esc menu and the help board (F1; a small "F1 Controls & help" plate shows at the start instead of the old wall of text). Boards pop in with a quick scale and fade.

### Guidance (built: goals, journal, marker, tips, errands)
- **Goals** walk a new player through the game, in order: gather wood and stone → empty the pack into the chest → build a workbench → craft a weapon → sleep through a night → follow the road to Ashby → trade with Marta → clear the bandit camp → grow a crop → craft and wear armour → build a watchtower → cross into Falls Valley → climb the mesa to the Falls Blossom → enchant armour → build the cottage. Side goals: a glider, the harpy roost, a full set of armour, three errands.
- A goal ticks off however it happens, even out of order. The server checks what it can see (buildings, players, the stockpile, packs, worn gear) and facts that other systems report (`Journal.Fact("deposited")`, `"slept"`, `"traded"`, `"crafted:glider"`, `"prayed:FallsShrine"`, and so on).
- The world owns the journal: co-op players share it, and it's saved with the world (`"journal"`). It goes out in the join snapshot and in `C_Journal` (protocol 7).
- **On screen:**
  - The goal you're on sits on a parchment note under the clock, with progress such as "Wood 3/8 · Stone 1/4". Its how-to line shows for 30 s after the goal changes.
  - A gold diamond marks where the goal is, with its distance. When it's off screen, an arrow on the edge points the way.
  - A "Goal complete" banner shows when one is done.
  - The **Journal** tab of the character sheet (J; on a controller, D-pad ▼ then RB) lists the goal now, what comes after, what's done, the side goals and Marta's errand.
- **Errands:** Marta in Ashby always has one going, such as "Bring 15 Wood for 12 coins". You hand it over from your pack on her trade board, and then she asks for something else.
- **Tips:** one-time notes, shown once each and remembered in `settings.cfg` (`[tips]`): when you're badly hurt, the first enemy close by, a full pack, a failing grip, dusk, raid season, rain, armour or a glider in your pack, and rune shards.

## 13. Controls
Keyboard and mouse, or a controller (Xbox, PlayStation / DualSense, or anything SDL knows). Prompts, the help board and the HUD show the keys or buttons of whichever you used last ("E", "X" or "□").

| Action | Keyboard / mouse | Xbox | PlayStation |
|---|---|---|---|
| Move / Sprint / Jump | WASD / Shift / Space | L-stick / click L-stick (stays on until you stop) / A | L-stick / L3 / ✕ |
| Dodge roll | Ctrl or C | B | ○ |
| Look | Mouse | R-stick | R-stick |
| Toggle 1st / 3rd person | V | click R-stick | R3 |
| Light / Heavy attack | LMB / Q or middle mouse | RT / RB | R2 / R1 |
| Block (time it to parry) | RMB (hold) | LT (hold) | L2 (hold) |
| Class ability | R or mouse button 4 | Y | △ |
| Level-5 ability | T or mouse button 5 | hold LB + Y | hold L1 + △ |
| Climb (walk into pale rock) / leap off | W into the rock / Space | stick into the rock / A | stick into the rock / ✕ |
| Open / fold the glider (in the air) | Space | A | ✕ |
| Interact (stations, beds, plots, wells, chests, trader, signs) | E | X | □ |
| Eat | F | tap LB | tap L1 |
| Weapons | 1–5 | D-pad ◀ ▶ | D-pad ◀ ▶ |
| Build mode (cycle / rotate / place / exit) | B (wheel / R / LMB / RMB or B) | D-pad ▲ (D-pad ◀ ▶ / RB / RT / LT) | D-pad ▲ (D-pad ◀ ▶ / R1 / R2 / L2) |
| Character sheet / journal | I / J | D-pad ▼, then LB / RB for the tabs | D-pad ▼, then L1 / R1 |
| Help | F1 | View | Create or touchpad |
| Menu / close a menu | Esc | Menu / B | Options / ○ |
| Save / Load (host only) | F5 / F9 | — | — |
| Player list | Tab (hold) | — | — |

On a controller, menus get the focus: move with the D-pad or the left stick, choose with A / ✕, go back with B / ○. The right stick scrolls the journal. The pad rumbles when you're hit, when you land a blow, on a parry and when a goal is done. A held trigger counts as one press (`Gamepad.Pressed`), so holding RT is one swing. For a moment after a menu closes, the button that closed it doesn't also jump or roll.

## 14. Roadmap
1. **Greybox v0 (done):** movement, 1P/3P, gathering, build mode, workbench crafting, training dummy, day/night, save/load.
2. **Multiplayer foundation (done):** host/join, dedicated server, shared base, sleep vote, PVP toggle, difficulty scaling hooks (§16). Every later feature is built multiplayer-first.
3. **Vertical slice (done):** wolves with AI, block/parry, farm plots and cooking, Ashby village with a trader, one bandit camp, house tier 2 (cottage). Follow-ups done: watering and rain, food buffs, dodge roll, navmesh pathing for enemies, carried packs with storage chests. Next: playtest it on two real PCs and tune the numbers.
4. **Art pass (done, see §12):** terrain, foliage and water shaders, volumetric clouds, faceted and animated wolves, the character toon ramp with outlines, the sculpted volcano, and synthesized audio (ambience and effects). Later polish: real recorded audio, music, a sculpted rock kit for the cliffs.
5. **Falls Valley (done):** climbing (with wet rock), the glider, updrafts, river currents, harpies, and the second sanctuary on the mesa. Also done: bandit raids on the base, with the watchtower. Follow-ups done: grip boots, swimming (the home and Great Falls pools), and fast travel between sanctuaries.
6. **Levels and abilities:** (in progress: XP, the level curve, saving, the XP bar, the level-up banner, levels on nameplates, the level-5 abilities, the second ability key, talents at levels 3 and 8 and the downed state are built; the level-12 abilities are next) XP and the level curve up to 20, the XP bar and level-up banner, talents at levels 3 and 8, the level-5 ability for all five classes, the second ability key, levels on nameplates, and the **downed state** (§16), which co-op needs anyway and the Healer's Rekindle builds on. See §6, *Levels and abilities*. It comes before the new regions so their enemies and bosses can be tuned for the levels players will have.
7. **Tier 1 fill-in:** clay, the Sawhorse and the Kiln (planks, bricks, charcoal). See §11, *Region progression*.
8. **Hollow Mine (bronze):** the Forest Road, darkness with torches and the lantern, cave crawlers, copper and tin, the Smelter and Anvil, bronze gear, the Mine Blossom.
9. **Spire Cliffs (iron):** wind gusts, the grapple hook, storm harpies, the Bellows, iron gear and the shield, the Longhouse. Fast travel between sanctuaries lands here at the latest. Also the level-12 abilities and the third ability key, since players reach level 12 here.
10. **Cinder Foothills (steel):** the heat meter (gentle), coal, the Blast Forge, the Bandit Warlord's fort, plate, the Keep, and the level-15 talents.
11. **Volcano:** the full heat system, lava hazards, Ember Ore, the Sacred Forge, the Ember Warden, and the level-20 talents at the level cap.
12. **Cities and workers:** automation and the regional economy. Workers can start earlier, with the Longhouse's worker slots (step 9).

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
| | Your glider (personal gear: it stays in your pack) |

- **Packs (built):**
  - Chopping, mining, cutting grain, wolf pelts, bandit coins and the blueprint, water from a well, and anything bought from Marta go into **your own pack**. Empty it into the shared stockpile at a **storage chest** (one by the shack; build more).
  - Spending takes from your pack first, then from the stockpile, but only at home (inside the land claim plus 6 m). That covers building, crafting, cooking, planting, watering, eating and the cottage.
  - **Trading uses your pack only.** Carry goods and coins to Ashby and back.
  - A full pack (40) refuses more and warns you.
  - Being defeated drops **half of each stack** in your pack. Packs are saved with the player and mirrored only to their owner.
- Players are saved by name: rejoining puts you back where you logged out, with your respawn shrine and your pack.

### Downed and revived (built, except Rekindle)
- **Why:** in co-op a lost fight should be something a friend can save. It also gives the Healer's **Rekindle** (§6, level 12) something to do.
- **Going down:** at 0 health you're **downed** instead of defeated at once, as long as another player who isn't downed is within 60 m. Playing alone, or with nobody near, you're defeated at once, as now.
- **While downed:**
  - You lie on the ground and can crawl slowly (1 m/s). You can't attack, block, roll, use abilities or eat.
  - A **30 s** timer runs down. When it runs out you're defeated as now: half of each stack in your pack drops and you respawn at your shrine. You can also **give up** early (hold Jump) to respawn at once.
  - **Enemies leave downed players alone** and turn to someone else. A blow already swinging still lands but can't hurt you further. PVP hits can't hurt a downed player either.
  - Food buffs stay while you're downed and end only on defeat.
- **Reviving:** any player can stand next to you and **hold Interact (E / X / □) for 3 s**. You get up with 30% health and lose nothing. Taking a hit or moving away breaks the hold. The Healer's **Rekindle** revives from 12 m, at once, with half health.
- **Everyone down:** if a downed player has nobody left within 60 m who isn't down (everyone else is down, asleep, or far away), they're defeated at once. Nobody waits out a timer with no one left to help.
- **Straight to defeat:** a fall into the cloud sea, or into lava later on, skips being downed, because nobody could reach you.
- **Seeing it:**
  - For the downed player: the screen greys and darkens at the edges, and the timer counts down in the middle with "Hold Jump to give up".
  - For everyone else: a marker over the downed player shows through walls and terrain, with their name and the timer. Within reach the prompt reads "Hold E to revive *name*".
  - A downed pose: KayKit's lying-down clips if it has them, otherwise built from the skeleton, as the climb is.
- **Code (built):** `Health` has two server hooks: `BeforeLethal` (a killing blow leaves the player on 1 health and downs them, if `Net.HasHelper`) and `Absorb` (no hit lands while downed). `Net.Downed.cs` runs it on the server: `ServerTryDown`, the 30 s timer, the revive hold (`S_Revive`, a hit on the helper or stepping away breaks it), give up (`S_GiveUp`), and defeat, which goes through the normal death path. Every machine hears `C_Downed` and `C_ReviveProgress` (protocol 10). Downed players lie in KayKit's death pose and crawl; `Enemy` targeting skips them; the `Interactor` finds a downed friend within 2.6 m and holds the revive. Still to build: Rekindle (level 12).

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
- **Off by default.** It's a server option and the host's checkbox. When on, player sword hits damage other players. A player beaten in PVP is downed like any other (see *Downed and revived*), and anyone can revive them.
- PVP never damages the shared base.

### Saving
- Only the host or server saves, to `user://saves/<world>.json`. Clients never write the world.
- Saves happen on sleep, on F5 (host), on a dedicated-server autosave timer, and when the last player leaves.

### Server options (`server.cfg`)
`name`, `port` (24570), `max_players` (4; more is allowed but untested beyond 4), `password`, `world`, `pvp`, `autosave_minutes`, `pause_when_empty`, `enemies`, plus the three difficulty factors.

