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
- **Push**: one commit per subject. Ask before pushing commits touching `src/`, `tests/`,
  `web/` or `.github/workflows/`; docs and notes are pushed freely. Commit subjects start
  with their area: `Core:`, `Tests:`, `Web:`, `CI:`, `Docs:`, `Notes:`.
- `dotnet build` and `dotnet test` need no OK (padlobby's rule): they only touch made-up
  data. Warnings are errors; every rule in the Core comes with its unit test; every commit
  builds and passes the tests on its own.
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
- The repository stays public, and the tool will be released publicly. Everything committed
  must be fit to publish: no SEGA assets, no saves, nothing personal.
- Stack: C# .NET 10 with a UI-free Core library (`src/GadgetTool.Core`), like padlobby and
  BluestarDeckExport.
- **UI: an HTML page hosted in WebView2**, Windows first, fully offline (Gibax). The HTML can
  be prototyped on its own and then becomes the real UI. An online version is "maybe one
  day, not a priority at all": do not design for it.
- Only two pieces need the game: the save adapter (`ISaveFormat`) and the real gadget
  catalog (from the paks). Everything else is built and tested on made-up data: a dev JSON
  save format and a hand-written catalog.
- Game rules, from the Sonic wiki's Gadget page (read 2026-11-19, fetched as raw wikitext;
  the HTML page refuses scripts) and a Grand Master plate screenshot from Gibax:
  - The plate is a 3x2 grid: two rows of 3 slots. A gadget takes 1 to 3 slots.
  - Plate levels: Normal 2 slots, Bronze 3, Silver 4, Gold 5, Master 6, Grand Master 7
    (the 6 slots plus the **Super Slot**, added in 2.0: any 1-slot gadget placed there gets
    its "Super Effect").
  - Presets: the wiki says 5; Gibax remembers 10, lettered A to J (probably 2.0). The count
    is read from the save, never hardcoded.
  - Slot costs change with patches (Crash Pads 2 to 1 in 1.4.1, Boost Item Chance UP 3 to
    2 in 2.0): costs come from the game data, and an old plate can become invalid.
  - About 139 gadgets on the wiki, including 3-slot kits from Festivals.
- Model (in `src/GadgetTool.Core`): `PlateLayout` (row sizes + Super Slot), `GadgetPlate`
  (row gadgets in order + Super Slot gadget), `PlayerSetups`, `SaveSetups`;
  `PlateValidator` (unknown, not unlocked, duplicate, over capacity, does not fit the rows,
  no Super Slot, Super Slot needs a 1-slot gadget); `SetupEditor` (copy, paste on one or
  many presets/players, all or nothing, strict or drop-missing); `ShareCode` (`GT1-...`,
  ~48 characters for 6 gadgets + Super Slot, checksummed); `SetupJson` (export/import
  files); `ISaveFormat` + `DevJsonSaveFormat` (stand-in until the real format is known);
  `SafeSaveWriter` (refuses while the game runs or if the file changed since it was read,
  timestamped backup, write through a temp file); `SaveSession` (only plates may change;
  a changed plate that breaks a rule is never written; an untouched broken plate does not
  block saving).
- Model assumptions to confirm on real data: a gadget appears at most once on a plate
  (Super Slot included); a 2- or 3-slot gadget lies within one row; the save does not need
  each gadget's exact cell (the tool works out an arrangement); how lower plates (2 to 5
  slots) spread over the rows; unlocked gadgets and plate level are per local player (the
  adapter can give every player the same set if the save has only one).
- Icons: the final app reads them from the game files. Wiki icons may be used **locally**
  for prototyping only, in an ignored folder (`extracted/`), never committed.

## Open questions

1. **Save location.** Reported on a forum (not checked):
   `%APPDATA%\Sega\SonicRacingCrossWorlds\steam\<SteamID>`. Steam Cloud's own copy may sit
   under `Steam\userdata\<id>\2486820\remote`. SteamDB's cloud page for the app lists the
   real paths: https://steamdb.info/app/2486820/ufs
2. **Save format.** Standard Unreal `SaveGame` (GVAS: the file starts with the 4 bytes
   `GVAS`, readable with existing libraries) or a Sega format, encrypted or compressed
   (needs reverse engineering). This is the go/no-go for the whole project.
3. **Per-player setups.** Does the save really hold separate gadget setups for local
   players 2 to 4, or do guests use something else? (The wiki does not say.)
4. **Pak encryption.** Are the release paks encrypted? The open beta was reported
   unencrypted, and Year 2 content has been datamined, which is a good sign.
5. **Prior art.** The Crossworlds Gadget Randomizer
   (https://silviafoxgames.itch.io/crossworlds-gadget-randomizer) may already write the
   save; if so, the format is already worked out. No CrossWorlds save editor was found.
6. **Online.** Is the gadget plate also stored server-side? Matters for how safe editing is.
7. **Game process name**, for `ProcessNameProbe` (refuse to save while the game runs).
8. **Duplicates and cells.** Can the game equip one gadget twice? Does the save store each
   gadget's cell in the grid?
