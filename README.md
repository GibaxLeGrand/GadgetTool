# GadgetTool

An external gadget setup tool for **Sonic Racing CrossWorlds** (PC, Steam). It lets you
build, copy, paste, export and import gadget setups for every slot and every local player
(1 to 4, offline), without launching the game.

> **Status: early work, not usable yet.** The core (rules, copy/paste, share codes,
> export/import, safe saving) is written and tested on made-up data. The game's save format
> is not known yet; the next step is to analyse real save files. See [notes/](notes/) for
> the plan and the resume point.

## Build

Needs the .NET 10 SDK.

```
dotnet build GadgetTool.slnx
dotnet test GadgetTool.slnx
```

## Planned features

- Read the save and show every gadget preset, for local players 1 to 4.
- Edit a preset: only gadgets you have already unlocked, respecting each gadget's slot cost,
  the plate's rows and the Super Slot (1-slot gadgets only).
- Copy and paste a preset to any other preset, of any player, or to many at once.
- Export and import setups as JSON or as a short text code you can paste in a chat.

## Safety rules the tool will follow

- Writes only while the game is closed, and backs up the save before every write.
- Never unlocks anything: it only rearranges what the save already holds.
- Refuses to write a save whose version it does not know (the game is still being updated).

## No game files in this repository

Gadget names, slot costs and icons will be read at runtime from your own installation of
the game. This repository holds no save files and no SEGA assets.

## License

GPL-3.0, see [LICENSE](LICENSE).

Unofficial fan project, not affiliated with or endorsed by SEGA or Sonic Team. Sonic Racing
CrossWorlds is a trademark of SEGA.
