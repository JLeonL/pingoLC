# Changelog

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
