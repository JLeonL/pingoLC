# Pingo Enemy

Pingo adds a new indoor enemy to Lethal Company.

Developed by JLeonL.

Pingo is harmless: it does not chase, move, or kill players. It stands still, can be scanned as an enemy, and becomes increasingly annoying as players stay near it.

## Features

- Stationary non-lethal enemy.
- Scannable as `Pingo`.
- High spawn weight by default.
- Spatial `pingo.mp3` audio.
- Volume increases while players stay nearby.
- After enough nearby time, the sound interval ramps down until sounds overlap, then loops back so it does not stay permanently at the fastest rate.

## Configuration

Config file:

```text
BepInEx/config/JLeonL.PingoEnemy.cfg
```

Important defaults:

```ini
[Spawning]
SpawnWeight = 175

[Testing]
EnableDebugSpawnKey = false
ForceSpawnAfterLanding = false
ForceTestSpawnInFrontOfPlayer = true
```

`ForceSpawnAfterLanding` is disabled for release, so Pingo will no longer appear automatically when the ship lands. It spawns naturally through LethalLib.

Enable `EnableDebugSpawnKey` only if you want the host to press `F6` to spawn Pingo during testing.

## Installation

Install through Thunderstore Mod Manager or r2modman.

Required dependencies:

- BepInExPack
- LethalLib

## Notes

All players in the lobby should install the mod and dependencies.
