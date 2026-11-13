# Katrina's Game — *Ember & Blossom* (greybox v0)

A medieval cliffside base-building, crafting and sword-combat game in Godot 4.7 (C#).
Design doc: [docs/GDD.md](docs/GDD.md).

## Requirements
- .NET 8 SDK
- Godot 4.7.2 **.NET** build (`Godot_v4.7.2-stable_mono_win64`)

## Run
Open `project.godot` in the Godot .NET editor and press **F5**. Or from a terminal:
```
dotnet build
<path-to>/Godot_v4.7.2-stable_mono_win64.exe --path .
```

## Scripted smoke test
Plays through the whole loop (gather → build → craft → fight → save/load), takes screenshots and quits:
```
<path-to>/Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --autotest --shots=C:/temp/shots
```

## Layout
- `scripts/core`: GameState autoload (inventory, time, save/load, input map), item/recipe/building definitions
- `scripts/player`: movement, 1st/3rd-person camera rig, combat, interaction
- `scripts/build`, `scripts/crafting`, `scripts/ui`: build mode, workbench, HUD
- `scripts/world`: Home Cliffs greybox generator, day/night, harvestables, shrine, bed, campfire
- `data/`: `.tres` content (add items, recipes and buildings here, no code needed)
- `scenes/`: Main, Player, props, buildings
