# Agent memory

The agent's memory for GadgetTool, copied here so any machine can resume. Kept current;
decisions are added with their date. The repository is public: technical content only.

## Working with Gibax

- Project owner, GitHub `GibaxLeGrand`. Write "Gibax" in the repository.
- Talk to him in French. Everything committed is in English.
- Some of his machines do not have the game installed: save and game-file analysis can run
  anywhere once samples exist, but anything that needs the game happens on a machine that
  has it.
- **No launch without his explicit OK**: the game, the tool's exe, anything that writes a
  real save.
- **Push**: ask before pushing commits touching `src/`, `tests/` or `.github/workflows/`;
  docs and notes are pushed freely.
- Repository identity (local git config): `user.name GibaxLeGrand`, email
  `48137851+GibaxLeGrand@users.noreply.github.com`. Commits end with the Co-Authored-By line.

## The project in one paragraph

Sonic Racing CrossWorlds (Steam AppID 2486820, Unreal Engine 5) has gadget plates: passive
gadgets that each take 1 or more slots on a plate. Building and copying setups is slow in the
game's menus, especially for local players 2 to 4. GadgetTool edits the setups straight in the
save, outside the game: per slot, per player (1 to 4 offline), with copy, paste, export and
import.

## Decisions (2026-11-19)

- Only rearrange what the save already holds: gadgets already unlocked, valid slot costs,
  never unlock anything. A tampered save could cause trouble online.
- Write only while the game is closed; back up the save before every write (Steam Cloud can
  overwrite a save, or be overwritten by one, if the game or Steam is mid-sync).
- Save parser is versioned: refuse to write a version it does not know. Update 2.00 (Year 2,
  3 November 2026) shows the game keeps changing.
- Gadget data (internal IDs used in the save, slot costs, localized names, icons) comes from
  the game's own pak files, read at runtime with
  [CUE4Parse](https://github.com/FabianFG/CUE4Parse) (the C# library behind FModel). Wikis
  and guides are only a reference for mockups: they do not give internal IDs and lag behind
  patches.
- No SEGA assets and no save files in git (public repo): `samples/`, `extracted/`,
  `backups/` and `*.sav` are ignored.
- Slot count is read from the save or game data, never hardcoded: guides disagree (6 slots,
  7 at Grand Master plate according to others).
- Stack proposed, not confirmed by Gibax yet: C# .NET 10 with a UI-free Core library, like
  padlobby and BluestarDeckExport.

## Open questions

1. **Save location.** Reported on a forum (not checked):
   `%APPDATA%\Sega\SonicRacingCrossWorlds\steam\<SteamID>`. Steam Cloud's own copy may sit
   under `Steam\userdata\<id>\2486820\remote`. SteamDB's cloud page for the app lists the
   real paths: https://steamdb.info/app/2486820/ufs
2. **Save format.** Standard Unreal `SaveGame` (GVAS: the file starts with the 4 bytes
   `GVAS`, readable with existing libraries) or a Sega format, encrypted or compressed
   (needs reverse engineering). This is the go/no-go for the whole project.
3. **Per-player setups.** Does the save really hold separate gadget setups for local
   players 2 to 4, or do guests use something else?
4. **Pak encryption.** Are the release paks encrypted? The open beta was reported
   unencrypted, and Year 2 content has been datamined, which is a good sign.
5. **Prior art.** The Crossworlds Gadget Randomizer
   (https://silviafoxgames.itch.io/crossworlds-gadget-randomizer) may already write the
   save; if so, the format is already worked out. No CrossWorlds save editor was found.
6. **Online.** Is the gadget plate also stored server-side? Matters for how safe editing is.
