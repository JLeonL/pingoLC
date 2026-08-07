# Changelog

## 2.0.0

- Adds movement for Pingo using the indoor NavMesh.
- Pingo now patrols interior rooms, looks for visible players, and chases a selected target.
- After the sound overlap loop resets, Pingo gives the chased player a 30 second cooldown before targeting them again.
- Keeps Pingo as a harmless enemy and prevents it from acting as an outside enemy.
- Adds NetworkTransform support so movement is synchronized for multiplayer clients.

## 1.0.2

- Plays Pingo's audio locally on every client so non-host players can hear it in multiplayer.
- Keeps the synchronized enemy spawn from 1.0.1 while avoiding audio-only host ownership.

## 1.0.1

- Registers Pingo as a Netcode network prefab on host and clients before joining a lobby.
- Fixes Pingo only being visible for the host in multiplayer sessions.

## 1.0.0

- First public release.
- Adds Pingo as a stationary, non-lethal indoor enemy.
- Adds scan node support so Pingo is detected as an enemy.
- Adds Luigi model AssetBundle and `pingo.mp3` audio.
- Adds progressive nearby audio volume and looping overlap ramp.
- Disables debug and forced landing spawns by default for release.
- Sets default spawn weight to 175.
