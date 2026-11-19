# GadgetTool

An external gadget setup tool for **Sonic Racing CrossWorlds** (PC, Steam). It lets you
build, copy, paste, export and import gadget setups for every slot and every local player
(1 to 4, offline), without launching the game.

> **Status: idea stage, nothing is coded yet.** The save format is not known yet; the first
> step is to analyse real save files. See [notes/](notes/) for the plan and the resume point.

## Planned features

- Read the save and show every gadget plate, for local players 1 to 4.
- Edit a plate: only gadgets you have already unlocked, respecting each gadget's slot cost
  and the plate's slot count.
- Copy and paste a plate to any slot or any player.
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
