# Katrina's Game — *Ember & Blossom* (vertical slice)

A medieval cliffside base-building, crafting and sword-combat game in Godot 4.7 (C#), single player or co-op for up to 4 players (more on a dedicated server).
Design doc: [docs/GDD.md](docs/GDD.md).

**In the vertical slice:** gather, build and craft at home. What you gather goes into your pack, and you empty it into the chest by the shack. Wolves hunt at night and path around obstacles. Fight with block and parry (hold RMB, time it) and dodge roll (Ctrl or C). Follow the road south to the Village of Ashby and trade with Marta, carrying your goods and coins in your pack. Clear the bandit camp for coins and the cottage blueprint. Farm wheat and turnips: water them from a well or the pool, or let the rain do it. Cook at the campfire and eat with F for healing and timed buffs. Upgrade the shack to a cottage at the plans board. East through a pass lies Falls Valley: climb the pale rock (walk into it), craft a glider from harpy feathers and ride the waterfall's updraft, mind the river's current, and pray at the second blossom on the mesa. From day 3, bandits raid your base at dusk: walls slow them, watchtowers shoot them, and killing them gets back what they stole. Make armour at the workbench (leather, grip boots, feather and traveller's cloaks, iron helm, studded jerkin, charms), wear it from the character sheet (I), and enchant it at a Rune Altar with rune shards from raiders and harpies. The whole UI is a medieval kit: oak boards in ornate gold frames, icon slots and parchment notes. A goal tracker, a gold marker and a journal (J) show what to do next; Marta in Ashby has errands for coin, and one-time tips explain things as they come up. Play with keyboard and mouse or a controller (Xbox or DualSense): the prompts follow whichever you use. Pick a class (Warrior, Rogue, Wizard, Healer or Archer), each with its own passives and an ability on R. Customise your character (Wanderer or Knight, skin, hair, outfit and trim colours, cape, helmet, height) from the main menu or the Esc menu; everyone you play with sees it.

## Requirements
- .NET 8 SDK
- Godot 4.7.2 **.NET** build (`Godot_v4.7.2-stable_mono_win64`)
- Optional: Python 3 with numpy, only to regenerate the sound set (`python tools/make_audio.py`). The generated `.wav` files are already in `assets/audio`.

## Run
Open `project.godot` in the Godot .NET editor and press **F5**. That opens the main menu: Single Player, Host Game, or Join Game. Or from a terminal:
```
dotnet build
<path-to>/Godot_v4.7.2-stable_mono_win64.exe --path .
```

## Multiplayer
Design: [GDD §16](docs/GDD.md#16-multiplayer). The host or server owns the world and the save. Friends join by `ip:port`. The default port is **24570** (UDP); the host must forward it, or everyone can use a virtual LAN such as Tailscale.

Command-line shortcuts (everything after `--`):
```
... --path . -- --host --name=Kat [--pvp] [--max-players=4] [--port=24570] [--world=home]
... --path . -- --join=192.168.1.20 --name=Sam [--password=pw]
```

**Dedicated server** (no window, no player of its own):
```
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . -- --server [--config=path/to/server.cfg]
```
The first run writes a default `server.cfg` (`user://server.cfg` unless `--config` is given) with `name`, `port`, `max_players` (4; higher is allowed), `password`, `world`, `pvp`, `autosave_minutes`, `pause_when_empty`, `enemies` (night wolf packs and the bandit camp), and the difficulty-scaling factors. Type `status`, `save`, `say <message>`, `wolves` (spawn a pack near a player) or `quit` in the server console.

Saves are written to `user://saves/<world>.json`. On Windows, `user://` is `%APPDATA%\Godot\app_userdata\Katrina's Game`.

## Scripted tests
Single player (gather → build → craft → fight → save/load → sleep → packs and the chest → navmesh → dodge roll → wolves and the howl → block/parry → bandit camp → Ashby trade → farming, watering, rain, cooking and buffs → cottage → character customisation → classes and abilities → Falls Valley (climbing, the glider, updrafts, the river, harpies) → raids and the watchtower → armour, enchanting and the UI kit → the volcano and the character close-up → audio), with screenshots. Add `--audio-driver Dummy` before `--` to run it silently:
```
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --shots=C:/temp/shots
```
Co-op: start a host, then a client in a second terminal. Each side prints `[NETTEST]` results. The test covers the client's look (sent on joining and changed mid-game), the client's pack and storing it in the shared stockpile, replicated building, the dodge roll, the sleep vote, PVP, wolves, bandit scaling for 2 players, the client's guard, trading from the pack and farming, the cottage upgrade, rain, and leaving.
```
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest-mp=host --pvp --name=Hosty
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest-mp=client --name=Joiny
```
The client test also runs against a dedicated server.

## Layout
- `scripts/core`: GameState autoload (shared stockpile, time, players, save/load, input map), Supplies (packs vs. stockpile), item/recipe/building definitions
- `scripts/net`: Net autoload (sessions, handshake, every gameplay RPC), server config, co-op difficulty scaling, the co-op test
- `scripts/player`: movement, 1st/3rd-person camera rig, combat, interaction, the player's body and look (CharacterModel, Appearance)
- `scripts/build`, `scripts/crafting`, `scripts/ui`: build mode, workbench, HUD, main menu, character creator
- `scripts/enemies`: Wildlife (night packs, bandit camp, replication), the shared Enemy brain, Wolf and Bandit
- `scripts/audio`: sound effects and loops by name (Sfx), the ambience bed (wind, rain, birds, crickets)
- `scripts/art`: model paths, the stylized materials and shaders (terrain, foliage, water, characters and outline)
- `tools/make_audio.py`: synthesizes `assets/audio/*.wav`
- `scripts/world`: Home Cliffs generator (plateau; `HomeCliffs.Ashby.cs`: vale, Ashby, bandit camp, shack/cottage; `HomeCliffs.Volcano.cs`: the sculpted volcano), day/night, weather (rain), the enemy navmesh (WorldNav), harvestables, shrine, bed, campfire (cooking), farm plots, wells and the pool, storage chests, trader, home plans board
- `data/`: `.tres` content (add items, recipes and buildings here, no code needed)
- `scenes/`: main menu, Main, Player, props, buildings
