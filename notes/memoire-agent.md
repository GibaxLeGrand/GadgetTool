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
  field in case (`Run45_...` against `run45_...`); the save spells them like `ID`. Done
  2026-11-19 (branch `core/ids-ignore-case`): every comparison goes through
  `GadgetIds.Comparer` (ordinal, ignoring case) in the Core and `idKey` in `web/js/rules.js`;
  a catalog refuses two ids that differ only by case; `SaveSession.Open` spells every id as
  the catalog does (`SaveSetups.WithCatalogSpelling`); share codes hash the catalog's
  spelling, so the code format and the page's codes did not change.
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
  `categories`...). The icons are shown by the app (next section).

## The app on the real save (2026-11-19, branch `app/real-save`)

- `--catalog <pack>\<version>\catalog.json` (its `icons` folder is used too) and
  `--save-key <file>` (any text holding the 64 hex digits; `CrossWorldsSaveFormat.KeyFromText`).
  With a key, saves open through `AnySaveFormat` (game save, else the made-up sample); without
  one, only the sample opens. The open dialog starts in the game's save folder.
- Icons: the app maps the pack's `icons` folder to `https://icons.gadgettool.example/`
  (`DenyCors`: images only) and sends each gadget's URL with the catalog (`IconUrl` in
  `AppBridgeOptions`); the page shows the image, else the old placeholder.
- Checked on the measuring PC, reading a **copy** of a real save with the extracted 2.0.0
  catalog through `SaveSession` (`extracted/tools/check_session.cs`): 4 players, Grand Master,
  123 unlocked gadgets all in the catalog, French names, and **no rule problem on any of the
  40 presets** (slot costs, rows, Super Slot, unlocked). The model's assumptions hold on real
  data. The app itself was not launched (Gibax's OK needed).
- Saving a real save was refused on this branch; writing comes with the next one.

## Writing the real save (2026-11-19, branch `core/crossworlds-save-write`)

- `CrossWorldsSaveFormat.Write` rebuilds only `GadgetPresetDataLists` and
  `SpGadgetPresetDataLists`, and inside them only the presets whose rows (or Super Slot)
  changed; the two lists are spliced back and the size of `_UserMachineAssemblyData` is
  patched. Every other byte stays. `GvasSerializer` writes the subset the lists use.
- Safety: refuses an unchecked game version (`WritableRomVersions` = { 20000 }); refuses to
  write unless the unchanged lists serialize back byte for byte; copies the entry layout
  from an entry the save has; reads the result back and checks every preset before
  returning it; unchanged setups return the original bytes.
- Cells: `PlateLayout.Arrange` picks rows (biggest gadgets first), then cells left to right
  in the plate's order, row r starting at cell 3r; Super Slot at cell 6. An emptied preset
  is written as six `NonGadget` cells, as the game does; no Super Slot = empty list. Only
  the Super Slot changed → the rows keep the game's cells.
- The format needs slot costs to place gadgets (`slotCost`, given by the app from the
  catalog).
- Checked on the measuring PC with **copies** only (`extracted/tools/check_write.cs`,
  output in `samples/W`, git-ignored): unchanged setups give the identical file; changing
  P2 B and P3 J's Super Slot changes exactly those presets; before the lists only the 4-byte
  size field differs, everything after them is identical. Not loaded in the game yet.

## Super effects, category colours, game texts (2026-11-19, branch `app/super-effects-colours`)

- `CatalogJson` reads `superEffect` (texts by language) and the category colours
  (`categories[].color`, `#RRGGBB` only); `GadgetDefinition` gains `SuperEffect` and
  `CategoryColor`; the bridge sends both. The page shows the Super effect in the tooltip and
  in the game-style info box, and the "game colours" theme paints category rows with the
  game's colours.
- Game texts: `<SpStyle>...</>` shown in bold; `{Placeholders}` shown as a "?" with a
  tooltip. Their values exist in `DA_GadgetParameter2` (e.g. `BonusDashPanelMap`: Default /
  Mini / Special, `RingNumLv1..4`), but which value a text shows is the game's code: **not
  guessed**. Open question for Gibax: read a few texts in the game (e.g. "Dash Panel Bounty"
  and its Mini) to see what numbers the game shows, then decide whether to fill them.
- Checked in the Claude app's browser on the page's stand-in host (python http.server, the
  pack catalog copied to the git-ignored `extracted/wiki-catalog.json`): French texts,
  "?" placeholders, bold Super effect, game colours on category rows; no console error.

## Data pack index (2026-11-19, branch `data/pack-index`)

- `PackIndex` (Core) reads and writes `index.json` (`gadgettool.packs`, version 1): per pack
  the game version, the Steam builds it was made from, its folder and every file's SHA-256.
  `Pick(build)` gives the pack made from that build, else the newest (the app warns).
  `SteamInstall` (Core, read only) finds the installed build: `libraryfolders.vdf` → the
  library holding app 2486820 → `appmanifest_2486820.acf` → `buildid`.
- The extractor writes/updates `index.json` after the catalog and icons. The app takes
  `--data <folder>` or a `data` folder next to its exe; `--catalog` still wins. Steam's folder
  comes from `HKCU\Software\Valve\Steam\SteamPath` (read only), else Program Files (x86).
- Checked on the measuring PC: the local pack's index lists 149 files for 2.0.0, and the
  installed build 24996408 picks it exactly (`extracted/tools/check_pick.cs`).

## The game's gadget screen (2026-11-19)

- Gibax (from work): "les icônes sont pas comme ça in game", "fais vraiment comme in game":
  in the game the gadget's art fills its hexagon and no name is written in it. Done for the
  plate and the gadget list (names stay in tooltips and the info box; placeholders keep
  them).
- The game's gadget screen textures are in `/Game/01_Union/UI/Garage/GadgetCustom/Texture/`:
  `UI_GadgetCustom_GadgetPlate` (4096x4096 atlas of 7 plates: light blue, bronze, silver,
  gold, dark blue, festival pink/blue, black and gold; each with 2 rows of 3 hexagon cells
  and the gold Super Slot hexagon on the right), `UI_GadgetCustom_GadgetBase`,
  `UI_GudgetCustom_BG`, `UI_GudgetCustom_HexagonCursor`, `UI_GudgetCustom_LockIcon`,
  `UI_GudgetCustom_PlusMark`, tabs (`..._TabBtnBase_*`, `..._TabPict`). Each gadget also has
  a 256x256 thumbnail (`ThumbnailTexture`, `GadgetTmb/`): a picture on a sky background,
  apparently for the gadget details, not the hexagons. Exported locally for a look
  (`extracted/ui_textures/`, `extracted/tools/export_textures.cs`).
- **Open (needs Gibax, a screenshot of the game's gadget screen with 2- and 3-slot gadgets
  on the plate)**: how the game draws a multi-slot gadget over separate hexagon cells, which
  atlas frame goes with which plate level (`DT_GadgetPlateData`, `M_UI_FlipBook_GadgetPlate`),
  and whether the list uses the icons or the thumbnails. Then use the plate texture itself.

## Icons on the game's bases (2026-11-19, branch `pack/icon-bases`)

- Gibax sent a screenshot of an icon in the game: the art over a dark hexagon with the "G"
  pattern and a blue edge, "je sais que tu peux récup ça". Those are the game's icon bases
  (`UI_GadgetIcon_Base_*`, see `docs/DATA.md`): exported to the pack's `ui/`, cut to what is
  drawn; each gadget gets `base` (`kit` / `mini` / none) in the catalog; the page draws the
  icon centred on its base, the Super Slot on the gold one. Checked in the Claude app's
  browser on the stand-in host: matches the screenshot for a 1-slot gadget.
- Gibax's corrections (same day): stretch the 2- and 3-slot bases over their cells "comme
  avant"; in the gadget list every gadget is 1-slot "pour l'ui"; the EX kits ("Spindash
  etc") on the EX bases, 1-slot in the list and 3-slot on the plate. First tried
  EX = the "AdvancedKit" category on the pink bases; Gibax then checked in the game: the
  Spin Dash kit has the same frame as the kit under it (yellow, notched). So every kit has
  the kit base, and the pink EX bases are not used (not exported). Plate bases stretched
  with a CSS border-image (ends kept). Gibax then found an icon off-centre: the game's
  drawings sit up to 40 px off the middle of their 512 canvas; the extractor now moves each
  one to the middle (`PngWriter.CenterDrawing`, alpha > 24), all 148 within half a pixel. Still to confirm: where the icon sits on a long base
  (centred here).
- The app serves the whole pack folder on `https://pack.gadgettool.example/` (`DenyCors`,
  images only): `icons/`, `ui/`; `AppBridgeOptions.PackUrl` tells the page.

## The game's plates and the generated sample (2026-11-19, branch `ui/game-plates`)

- Gibax asked for the game's plates and its empty cell. The extractor reads
  `DT_GadgetPlateData` (catalog `plates`) and cuts the seven frames of
  `UI_GadgetCustom_GadgetPlate` to `ui/plate-<n>.png`; `plateGeometry` (from
  `WBP_GadgetCustom_Sub_GadgetPlate`) says where the cells, Super Slot and name go. Details
  and the 2.0.0 table in `docs/DATA.md`.
- Every plate picture already draws the six cells and the Super Slot empty (the dark "G"
  hexagon): that is the game's empty cell, so the page draws nothing on an empty cell (a "+"
  on hover) and darkens a closed one under `ui/lock.png`. The page picks the plate by slot
  count and Super Slot (`RentalPlate` skipped); without a pack it draws its own plate.
- Assumed, not seen: a lower plate fills its cells left to right, top row first (as the
  save's `[n]` / `[3, n-3]` rows say). To check in the game with a lower plate.
- v0.1.0 on the work PC showed "30/148 unlocked", everything locked, "1/7 slots !": the
  bundled `data/dev/save.json` has made-up ids. With the game's data (`catalog.Plates` not
  empty) the app now writes the sample from the gadget list (`SampleSave.Generate`: P1 Grand
  Master, P2 Master, P3 Gold, P4 Bronze, every gadget unlocked, presets A-F filled and valid,
  G-J empty, always the same file).
- Gibax: "for the test preset, just turn off save/import/export, but unlock everything". The
  sample (the file at `SampleCopyPath`, however it was opened) is read-only: the bridge
  refuses `save`, `exportFile` and `importFile` with `sampleReadOnly` and tells the page
  (`sample: true` in the open reply); the page greys out Save, Share... and Import... and says
  why. Editing, copying and pasting between presets still work. The browser stand-in does
  the same.
- In the game view, a container-sized plate inside a shrink-wrapped column is 0 pixels wide:
  `#gamePlateSlot` stretches.
- Gibax: the stretched 2/3-slot bases "do not go into the corners". `WBP_GadgetIcon` draws
  `Base_02` / `Base_03` (1024x512 textures) at 2048x512: the whole texture twice as wide,
  which matches 2 and 3 cells of the plate to 3 pixels. The page now does that on the game's
  plate (`scaleX(2)`), no more stretching by the middle there.
- Gibax also felt some bases were wrong (his capture: the "Perfect Charge" drawing on a
  silver frame in the game, blue in the tool). Two gadgets share that drawing: the Mini
  (1 slot, in `DA_MiniGadgetList`, silver in the tool) and the 2-slot one (blue). He could not
  say which one his capture is. Checked: the kit frame follows `DT_GadgetVariousDefineData`
  exactly (32 enabled kits, all in the "Kit" categories); EX is only for festival rules. A
  sheet of all 148 gadgets on their bases: `extracted/bases_sheet.html` (git-ignored, served
  from the repository root). Still open until he points at a wrong one in the game.

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
    needed). The page assumes left to right, top row first (see "The game's plates").
