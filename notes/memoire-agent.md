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
    ("Check for data updates", from the repository's GitHub Releases: release assets,
    never files in git; hashes checked; never automatic).
  - **Icons and game data: in the release (Gibax, 2026-11-19)**, on
    https://github.com/GibaxLeGrand/GadgetTool/releases: the first release ships a working
    2.0.0 data pack (catalog with names, descriptions, slot costs, categories, and the
    icons). The git repository holds the tools only: the extractor that dumps the game's
    gadget data, icons included, and the app; no game data or icons (CLAUDE.md rule kept).
    Players with the AES key can run the extractor on their own install.
  - Gibax's two paths (same day): **offline, exact**: the player gives the AES key and the
    app reads the installed game itself; **online**: the app updates the software and/or
    the data from GitHub. Agent's plan: one extraction library shared by the maintainer
    CLI and the app's offline option; data chosen in this order: local extraction for the
    installed build > downloaded pack for that version > pack in the release > newest older
    pack with a warning. Online: data packs download into LocalAppData (easy); a new app
    version is announced with a download (replacing a running exe needs a small updater:
    later). Only the official repository over HTTPS, SHA-256 checked, opt-in.
- **Languages (Gibax, 2026-11-19): game texts first.** Gadget names, categories and
  descriptions come in every language the game ships, from the extractor; the catalog
  format holds them (`docs/DATA.md`: a text is a string or `{ "lang": text }`, plus a
  `categoryId` that does not change with the language). The app shows a "language of the
  game's texts" picker when the data has more than one, defaulting to Windows' language,
  with the default language as fallback text by text. Shares and files hold ids only, so
  they work across languages. The tool's own UI text (buttons, messages) stays English
  for now: Gibax asked only for the data side ("prépare juste ça pour l'extracteur").
- Local test catalog: `extracted/wiki-catalog.json` (git-ignored), built from the wiki's raw
  wikitext by a throwaway script: 138 gadgets, 13 categories, costs 1/2/3 = 62/33/43, 60
  with a Super Effect line. Not in the repo; rebuild it from the wiki if needed.
- Icons: the final app reads them from the game files. Wiki icons may be used **locally**
  for prototyping only, in an ignored folder (`extracted/`), never committed.
  2026-11-19: the wiki's API gives the icon URLs (134 of 139 found, 31 MB at full size), but
  its image CDN answers scripted downloads with a Cloudflare "Just a moment..." challenge.
  Do not try to get past it. Icons come from the game via FModel on a machine with the
  game, or from Gibax's own browser; the UI mockup uses placeholders until then.

## The real save (measuring PC, 2026-11-19)

Read on a copy of a real save (taken with the game closed, Gibax's OK), game 2.0.0, Steam
build 24996408. Details in `notes/sessions/2026-11-19-measuring-pc.md`.

- **File**: `app.sav` in `%APPDATA%\SEGA\SonicRacingCrossWorlds\steam\<SteamID>\` (about
  1.2 MB; `net.sav`, `option.sav`, `playlog.sav` and replays sit next to it). Steam Cloud's
  folder `Steam\userdata\<id>\2486820\` only holds its index (`remotecache.vdf`).
- **Encryption**: the whole file is AES-256 in ECB mode with PKCS#7 padding. The key is not
  the pak key and is not in the exe on disk (Denuvo); it was found in the running game's
  memory with a read-only scan, Gibax's OK. **The key is never committed** (public repo):
  it lives in the git-ignored `extracted/` folder on the measuring PC. Re-encrypting the
  decrypted file gives the original byte for byte.
- **Inside**: a standard GVAS (save version 3, package 522/1012, engine 5.4.3, class
  `/Script/UnionSystem.AppSaveGame`, one 0 byte after the class name, 4 zero bytes after the
  last "None"), **tagged properties with complete type names** (UE 5.4): property names are
  in the file, so **reading the save needs no mappings file**. `_Header._RomVersion` = 20000
  (probably 2.0.0), `_Header._SaveCount` counts saves.
- **Gadgets** live in `_UserMachineAssemblyData`:
  - `GadgetPresetDataLists`: **4 local players**, each a `list` of **10 presets (A to J)**,
    each a `GadgetDataList` of `{ GadgetID: EGadgetId::<name>, SlotIndex: int }`.
    **`SlotIndex` is the gadget's first cell** (0-2 top row, 3-5 bottom row): the save does
    store cells, so writing must place gadgets. A 3-slot kit was seen at 3 (cells 3-5), 2-slot
    gadgets at 1 and 3. An empty preset is either six `EGadgetId::NonGadget` entries or a
    short list.
  - `SpGadgetPresetDataLists`: per player, `Presets` (20 entries, only the first 10 used),
    each with `EquippedSpGadgets`: the Super Slot's gadget at `SlotIndex` 6, or `NonGadget`,
    or an empty list.
  - `GadgetList`: one map for the whole save (not per player), `EGadgetId` →
    `{ bIsNew, LockType }`; `ELockType::None` = unlocked (135 entries, 11 with
    `ELockType::Price` in this save).
  - `GadgetSlotNum`: one value for the whole save, Super Slot included (7 = Grand Master).
  - `GadgetFragmentNum`: gadget fragments (currency).
  - No duplicate gadget in a preset, and the Super Slot's gadget never also in the rows, in
    this save.
- **Core** (branch `core/crossworlds-save-read`): `Saves/Gvas/GvasReader` reads the whole file
  (unknown types and native structs kept as raw bytes); `Saves/CrossWorlds/SaveCipher`;
  `Saves/CrossWorlds/CrossWorldsSaveFormat` reads the setups (ids without the `EGadgetId::`
  prefix, gadgets ordered by cell, `NonGadget` = empty) and **refuses to write** for now.
  Lower plates (2-5 slots) are assumed to fill the top row first: not seen in a save yet.
  Tests build synthetic encrypted saves with made-up gadgets and a made-up key.
- **Game process**: `SonicRacingCrossWorldsSteam.exe` (`UNION\Binaries\Win64`), started by
  the launcher `SonicRacingCrossWorlds.exe`, which stays open while the game runs.
- **Mappings for the game data**: a 2.0 `.usmap` was dumped in-game with UE4SS (git-ignored,
  measuring PC only). The paks open with the AES key already used for modding on that PC.
- **Decision pending (Gibax)**: how the public tool gets the save key. Agent's advice: ship
  it with the data packs on the releases page, never in git, like the icons.
- The save (PR #1, merged by Gibax on 2026-11-19) is read by the Core; the app does not use
  it yet.

## The extractor (2026-11-19)

- `src/GadgetTool.Extractor` (CLI, CUE4Parse 1.2.2.202611 from nuget.org, plus
  `Microsoft.Bcl.Memory` 10.0.15 because CUE4Parse brings 9.0.0, which has a known advisory
  and fails the build with warnings as errors). `GameFiles` reads the paks (base game only,
  `~mods` skipped, Oodle from a local dll, never downloaded); `PackBuilder` (pure, tested in
  `tests/GadgetTool.Extractor.Tests` with made-up tables) decides what goes in the pack.
- **Gadget data**: `DT_GadgetData` rows: `bIsEnabled`, `ID` (`EGadgetId::...`), `Category`
  (`EGadgetCategory::...`), `Name` / `Explanation` / `ExplanationSpecial` (Super effect)
  as string-table texts (`DB_Gadget`, `S2_DB_Gadget`, `S2_DB_Gadget_Special`; their source
  strings are the keys, the real texts are in `Localization/Game/<lang>/Game.locres`),
  `IconTexture`, `ThumbnailTexture`, `SizeX` (**slot cost**), `SizeY` (always 1), `plateId`
  (plate level needed), `Price`, `bIsDefault`, `GadgetCustomSortId`,
  `bInvisibleGadgetCustom`. `DT_GadgetCustomCategoryData`: category name, colours (hex),
  `SortID`.
- 2.0.0: 174 rows, 148 enabled (the pack holds those), 13 categories in use, 15 languages,
  68 Super effects. The 23 `bInvisibleGadgetCustom` gadgets stay in: saves own and equip
  some of them. Every gadget of the measuring PC's save is in the pack.
- **Ids ignore case** (Unreal names): the table's row names sometimes differ from its `ID`
  field in case (`Run45_...` against `run45_...`); the save spells them like `ID`. The Core
  compares ids exactly for now: **to fix when the app opens real saves** (compare ignoring
  case, or canonicalise to the catalog's spelling on read).
- Descriptions hold game placeholders (`{GetRing}`, filled from gadget parameters) and
  rich-text tags (`<SpStyle>...</>`): the app shows them raw for now.
- **Icons** (same day, branch `extractor/icons`): `icons/<id>.png` for all 148 gadgets,
  512x512 RGBA, about 12 MB. The textures are BC7 (`PF_BC7`, UI group), decoded with
  AssetRipper.TextureDecoder 2.6.3 (MIT, managed, no dependencies; the decoder
  CUE4Parse-Conversion itself uses, without that package's ImageSharp, SkiaSharp and glTF
  baggage); `PngWriter` writes the PNG with no imaging library. Icons live in five content
  folders (`/Game/01_Union` to `/Game/06_Union`); asset paths in the table do not always
  match the paks' letter case (`..._run60` against `..._Run60`), so files are looked up
  ignoring case. Thumbnails (`ThumbnailTexture`, 256x256) are not exported.
- Not done yet: `index.json`, the catalog reader for the extra fields (`superEffect`,
  `categories`...), the icons in the app.

## Open questions

1. ~~Save location.~~ Answered 2026-11-19 (see "The real save" above).
2. ~~Save format.~~ Answered 2026-11-19: encrypted GVAS, readable. The project is a go.
3. ~~Per-player setups.~~ Answered 2026-11-19: 4 players with 10 presets each; unlocked
   gadgets and plate level are shared.
4. ~~Pak encryption.~~ Answered: encrypted, key known on the measuring PC.
5. **Prior art.** The Crossworlds Gadget Randomizer
   (https://silviafoxgames.itch.io/crossworlds-gadget-randomizer) also runs in a browser
   (HTML5), so it most likely does not touch the save. No CrossWorlds save editor was found.
6. **Online.** Is the gadget plate also stored server-side? Matters for how safe editing is.
7. ~~Game process name.~~ Answered 2026-11-19: `SonicRacingCrossWorldsSteam` (and the
   launcher `SonicRacingCrossWorlds`).
8. **Duplicates.** Can the game equip one gadget twice? Not seen in the save. Cells: answered,
   the save stores each gadget's first cell.
9. **Writing**: does the game accept a save the tool re-encrypts (it should: the bytes come
   out identical), and does it check anything else (`_SaveCount`, Steam Cloud)? To test on a
   copy first, with Gibax's OK.
10. **Lower plates**: how a 2- to 5-slot plate maps to cells (a save with a lower plate is
    needed).
