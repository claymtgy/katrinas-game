# Katrina's Game — *Ember & Blossom* (vertical slice)

A medieval cliffside base-building, crafting and sword-combat game in Godot 4.7 (C#), single player or co-op for up to 4 players (more on a dedicated server).
Design doc: [docs/GDD.md](docs/GDD.md).

**In the vertical slice:** gather, build and craft at home; wolves at night; block and parry (hold RMB, time it); follow the road south to the Village of Ashby and trade with Marta; clear the bandit camp for coins and the cottage blueprint; farm wheat and turnips, cook at the campfire, eat with F; upgrade the shack to a cottage at the plans board.

## Requirements
- .NET 8 SDK
- Godot 4.7.2 **.NET** build (`Godot_v4.7.2-stable_mono_win64`)

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
Single player (gather → build → craft → fight → save/load → sleep → wolves → block/parry → bandit camp → Ashby trade → farming and cooking → cottage), with screenshots:
```
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --shots=C:/temp/shots
```
Co-op: start a host, then a client in a second terminal. Each side prints `[NETTEST]` results. The test covers the shared stockpile, replicated building, the sleep vote, PVP, wolves, bandit scaling for 2 players, the client's guard, trading and farming, the cottage upgrade, and leaving.
```
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest-mp=host --pvp --name=Hosty
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest-mp=client --name=Joiny
```
The client test also runs against a dedicated server.

## Layout
- `scripts/core`: GameState autoload (shared stockpile, time, players, save/load, input map), item/recipe/building definitions
- `scripts/net`: Net autoload (sessions, handshake, every gameplay RPC), server config, co-op difficulty scaling, the co-op test
- `scripts/player`: movement, 1st/3rd-person camera rig, combat, interaction
- `scripts/build`, `scripts/crafting`, `scripts/ui`: build mode, workbench, HUD, main menu
- `scripts/enemies`: Wildlife (night packs, bandit camp, replication), the shared Enemy brain, Wolf and Bandit
- `scripts/world`: Home Cliffs generator (plateau; `HomeCliffs.Ashby.cs`: vale, Ashby, bandit camp, shack/cottage), day/night, harvestables, shrine, bed, campfire (cooking), farm plots, trader, home plans board
- `data/`: `.tres` content (add items, recipes and buildings here, no code needed)
- `scenes/`: main menu, Main, Player, props, buildings
