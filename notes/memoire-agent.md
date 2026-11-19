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
- **App** (`src/GadgetTool.App`, 2026-11-19): WinForms window, the page in WebView2 under
  `https://gadgettool.example/` (navigation elsewhere and popups refused; dev tools and
  browser keys only with `--dev`). The page talks to `AppBridge` (Core) with JSON messages:
  catalog, openSave, openSample, reloadSave, save, gameRunning, exportFile, importFile.
  Dialogs run after the WebView2 event returns (BeginInvoke). Closing with unsaved
  presets asks first. Backups in `%LOCALAPPDATA%\GadgetTool\Backups`, never next to the
  save (Steam Cloud). Game detection by process name uses unverified guesses. Catalog:
  `data/dev/catalog.json` by default, `--catalog` for another (e.g. the local wiki one).
  WebView2 package 1.0.4314.35, as in padlobby.
- **Web UI** (`web/`, see `web/README.md`), first version 2026-11-19: `js/rules.js` keeps the
  Core's rules in JS for instant feedback (a test checks both produce the same share code);
  `js/host.js` talks to the app, or stands in for it in a plain browser. Placement is
  automatic (the Core's `Arrange`), as long as we do not know whether the save stores
  cells. UI text is in English, kept simple for a later translation. Preview in the Claude
  app: `gadgettool-web` in the session root's `.claude/launch.json` (python http.server on
  127.0.0.1:8766), then open `/web/`.
- **Plate look (Gibax, 2026-11-19): shapes never stretch.** There are exactly three gadget
  shapes. One slot is the Super Slot's hexagon (flat top and bottom, pointed ends, 132x116 at
  full size). The 2- and 3-slot shapes are the same hexagon made longer by whole slots: same
  height, same pointed ends. Empty and locked slots use the 1-slot shape. The plate itself
  follows the game's screenshot: a title band ("GRAND MASTER PLATE"), two rows of three, and
  the gold Super Slot on the right. Small windows scale the unit down; they never change its
  proportions.
- **Gibax's feedback on the first app (2026-11-19)**, all done the same day:
  - Keep share codes; add a file to save and send: **`.gtpresets`** (JSON, format
    `gadgettool.setups`: name, player, preset letter, gadgets, super). "Import…" sits next
    to "Share…" and imports onto the selected preset; a multi-preset file can also go back
    where it came from.
  - **Preset names, GadgetTool only** (the game has none): a small title under each preset.
    Stored in `%LOCALAPPDATA%\GadgetTool\preset-names.json`, keyed by the save's full path,
    never in the save; not part of undo. Share codes carry no name; files do.
  - Slot counts include the Super Slot: **7/7** on a Grand Master plate.
  - He prefers the scrolling lists (default). The **Game style** view is an option: preset
    tabs A–J over a blue plate, a description box, gadgets in rows by category with the slot
    cost on top (his screenshot is of an older version with A–E and no Super Slot).
  - More UI changes to come from him, to copy the game's look more closely.
  - Then (same day): **colours are a theme of their own**, separate from the view: he
    likes the game colours and wants them as something to turn on or off. Setting
    "Colours": per view (default: dark lists, game-coloured game style), dark, or the
    game's (blue backdrop and plate, gold/blue tabs, light category rows), for either view;
    remembered in browser storage. **Plate visuals: take them from the game's files**
    (FModel, on a machine with the game) rather than drawing them in CSS; read at runtime
    from the player's install, never committed.
- **Game versions (Gibax, 2026-11-19)**: a rebalance must not break shares or saves silently.
  - The catalog carries the game version ("major.minor.patch"; `data/dev` says 0.1.0, the
    local wiki catalog 2.0.0; the real one will come from the game's files).
  - Share codes carry the version (3 bytes) and each gadget's slot cost (2 bits); `.gtpresets`
    files carry the version and each gadget's name and slot cost.
  - Import rule (his): **same version** → nothing to check; **share older than ours** →
    check the preset's gadgets against our version: unchanged gadgets = compatible (no
    warning), changed or removed ones are listed, then the usual paste rules apply; **share
    newer than ours** → not compatible, error, nothing imported. Unknown version → checked
    like an older one.
  - Saves: `%LOCALAPPDATA%\GadgetTool\seen-game.json` keeps, per save, the game it was last
    opened with. On open after an update, only gadgets the presets use are compared; if any
    changed, a banner says what, and the presets it broke are kept as they are, marked red
    ("fix it before playing"). The old record stays until "Got it".
  - Share-code bytes changed for this (format byte still 1: nothing was released yet).
- **Idea (Gibax, 2026-11-19): the app refreshes its gadget list from the game itself.** He
  ships the exe with a 2.0.0 database; if the game moves to 2.1.0 (or someone adds or
  changes gadgets) before GadgetTool is updated, an option rebuilds the list from the
  player's install. Feasibility, as answered (nothing checked on game files yet):
  - It is the same reader as the real catalog (CUE4Parse in the app, read-only, on the
    player's own install, results cached in LocalAppData, never redistributed), run on
    demand. Detect a game update cheaply with the Steam build id in
    `steamapps/appmanifest_2486820.acf`, then offer the refresh.
  - To check first with FModel on a machine with the game, in this order: (1) are the paks
    encrypted (AES key)? (2) does reading need a mappings file (`.usmap`, usual for UE5
    unversioned properties), and does it change with patches? (3) where the gadget data
    lives (DataTable? one asset per gadget?) and which fields hold id, slot cost, category,
    icon; (4) names from the localisation files (French included); (5) where the game
    states its version.
  - Limits: a new save format, or a renamed table or field, still needs a GadgetTool update
    (the asset path and field names could live in a small config shipped with the
    database); if the refresh fails, keep the last good list and say so.
  - **Mods: ignore them** (Gibax): gadgets are not modded, it is a taboo in the community
    for now. The reader loads the base game's paks only and skips `~mods`; no "with mods"
    option.
  - **Proposed next (Gibax's idea, agent agrees; not decided yet): a separate data builder.**
    A maintainer tool (CLI) reads the game's files once per game update, with the AES key
    and mappings kept on the maintainer's machine, and writes a data pack per game version.
    The release archive ships `data/index.json` (game version ↔ Steam build id, file
    hashes) and `data/<version>/catalog.json`. The app picks the pack for the installed
    game (Steam build id from the appmanifest: no key needed on the player's side), falls
    back to the newest older pack with a warning, and can download new packs on demand
    ("Check for data updates", from the GitHub repository, hashes checked; never
    automatic). Open choice for Gibax: icons in the packs (SEGA art redistributed) or not
    (placeholders, or read locally when possible).
- Local test catalog: `extracted/wiki-catalog.json` (git-ignored), built from the wiki's raw
  wikitext by a throwaway script: 138 gadgets, 13 categories, costs 1/2/3 = 62/33/43, 60
  with a Super Effect line. Not in the repo; rebuild it from the wiki if needed.
- Icons: the final app reads them from the game files. Wiki icons may be used **locally**
  for prototyping only, in an ignored folder (`extracted/`), never committed.
  2026-11-19: the wiki's API gives the icon URLs (134 of 139 found, 31 MB at full size), but
  its image CDN answers scripted downloads with a Cloudflare "Just a moment..." challenge.
  Do not try to get past it. Icons come from the game via FModel on a machine with the
  game, or from Gibax's own browser; the UI mockup uses placeholders until then.

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
