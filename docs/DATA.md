# Game data: what the extractor writes, what the app reads

GadgetTool's code lives in this repository; the game's data does not. The extractor
(`src/GadgetTool.Extractor`, run by a maintainer with the game installed and its AES key)
reads the game's files and writes a **data pack** for one game version. Packs ship on the
[releases page](https://github.com/GibaxLeGrand/GadgetTool/releases): the first release
holds a working 2.0.0 pack. Players with the key can also run the extractor on their own
install. Mods (`~mods`) are ignored: gadgets are not modded.

## Pack layout

```
data/
  index.json            the packs: game version, Steam build id, files and their SHA-256
  2.0.0/
    catalog.json        every gadget, with the game's texts in all its languages
    icons/<id>.png      one icon per gadget (release only, never in git)
    ui/base-*.png       the game's icon bases (release only, never in git)
```

`index.json` lets the app pick the pack for the installed game from the Steam build id in
`steamapps/appmanifest_2486820.acf` (found through Steam's `libraryfolders.vdf`, read only),
without any key. The extractor writes and updates it (other versions are kept; a version
extracted again keeps the build ids it listed). Read by `PackIndex` (Core):

```json
{
  "format": "gadgettool.packs",
  "version": 1,
  "packs": [
    { "gameVersion": "2.0.0", "steamBuildIds": ["24996408"], "folder": "2.0.0",
      "files": { "catalog.json": "<sha-256>", "icons/<id>.png": "<sha-256>" } }
  ]
}
```

The app takes `--data <folder>`, or a `data` folder next to its exe that holds an
`index.json`, and opens the pack made from the installed build; without one, the newest pack,
with a warning. `--catalog <file>` still names a catalog directly. The SHA-256 are there for
downloaded packs (not done yet). `folder` must be a plain folder name: no `..`, no absolute
path.

## `catalog.json`

Read by `CatalogJson` (C#) and `GT.localizeCatalog` (web). Texts are either a string (in
`defaultLanguage`) or an object by language code, using the codes of the game's own
localisation folders (`en`, `fr`, `ja`, `zh-Hans`...). Every language the game ships should
be written: the app shows the one closest to the player's choice and falls back to
`defaultLanguage` text by text.

```json
{
  "format": "gadgettool.catalog",
  "version": 1,
  "source": "Sonic Racing CrossWorlds 2.0.0 (extracted)",
  "gameVersion": "2.0.0",
  "defaultLanguage": "en",
  "gadgets": [
    {
      "id": "<the id the save uses>",
      "slotCost": 1,
      "categoryId": "<a key that does not change with the language>",
      "name":        { "en": "...", "fr": "...", "ja": "..." },
      "category":    { "en": "...", "fr": "...", "ja": "..." },
      "description": { "en": "...", "fr": "...", "ja": "..." }
    }
  ]
}
```

| Field | Required | Notes |
|---|---|---|
| `gameVersion` | yes for real packs | "major.minor.patch". Shares record it: an import from a newer version is refused, an older one is checked gadget by gadget. |
| `defaultLanguage` | no (`en`) | The language plain-string texts are in, and the fallback. |
| `id` | yes | Exactly the id the save stores. Unique. |
| `slotCost` | yes | 1 to 3. Read from the game's data, never typed by hand: rebalances change it. |
| `categoryId` | no | Defaults to the category text in `defaultLanguage`. Keeps colours and grouping stable across languages. |
| `name`, `category`, `description` | `name` yes | Missing translations fall back to `defaultLanguage`. |

`data/dev/catalog.json` is a made-up example in English and French.

### More fields the extractor writes

| Field | Where | Read by the app | Notes |
|---|---|---|---|
| `superEffect` | gadget | yes | The text shown when the gadget sits in the Super Slot (1-slot gadgets only). Optional texts, like `description`. |
| `categories` | top | the colours | `{ id, sort, color, name }` for each category that holds a gadget, in the game's order; `color` is the game's category colour (`#RRGGBB`), used by the "game colours" theme. |
| `steamBuildId` | top | not yet | The Steam build the pack was read from (`buildid` in `appmanifest_2486820.acf`). |
| `languages` | top | not yet | Every language written, the default first. |
| `plate` | gadget | not yet | The plate level the gadget needs (`Rank0` to `Rank7`). |
| `base` | gadget | yes | The base the game draws the icon on: `kit`, `mini`, or absent for the usual hexagon. |
| `icon` | gadget | via `icons/` | The icon's asset path in the game's files; the icon itself is `icons/<id>.png`. |

Texts are kept as the game has them: a description may hold a placeholder the game fills in
(`{GetRing}`, `{AllParam}`, `{CostRing}`, `{Prob}`, `{Rate}`, `{Count}`: 43 texts in 2.0.0) and
rich-text tags (`<SpStyle>...</>`). The page shows the tagged part in bold and each
placeholder as a "?": the values sit in the game's gadget parameters (`DA_GadgetParameter2`,
per level and per Default / Mini / Special variant) and which one a text shows is decided by
the game's code, so GadgetTool does not guess them.

## The extractor

Reads `DT_GadgetData` and `DT_GadgetCustomCategoryData` (`/Game/01_Union/Database/Gadget/`)
once per language, with CUE4Parse, from the base game's paks only. Its rules are in
`PackBuilder` (`src/GadgetTool.Extractor`) and its tests. It needs, all kept out of git:

- the pak AES key (`--aes-key`, the key or a file that holds it);
- a `.usmap` made for the game version (`--mappings`; UE4SS dumps one in-game);
- `oo2core_9_win64.dll` (`--oodle`); it is never downloaded by the extractor.

```
dotnet run --project src/GadgetTool.Extractor -- --paks "<game>\UNION\Content\Paks"
    --aes-key <key or file> --mappings <file.usmap> --oodle <oo2core_9_win64.dll>
    --game-version 2.0.0 --out extracted\pack
```

`--icons false` skips the icons; `--languages en,fr` reads only some languages.

It refuses to write a pack when the data breaks a rule the app relies on (a gadget wider
than 3 slots or taller than 1 row, an id repeated ignoring case or holding characters other
than letters, digits and `_`, an unknown category, a gadget without a name in English) and
lists every problem. Game 2.0.0 (Steam build 24996408): 148 gadgets (68 / 35 / 45 at 1 / 2 / 3
slots), 13 categories, 15 languages (`en`, `ar`, `de`, `es`, `es-US`, `fr`, `it`, `ja`,
`ko`, `pl`, `pt`, `ru`, `th`, `zh-Hans`, `zh-Hant`), 68 Super effects.

**Icons**: `icons/<id>.png`, one per gadget, from the texture its `icon` path names (the
largest mip, decoded from BC7 with AssetRipper.TextureDecoder, written as 8-bit RGBA PNG with
transparency). The drawing is moved to the middle of its canvas (the game's are up to 40
pixels off out of 512; the size does not change), so it sits centred on its base. Game 2.0.0: 148 icons of 512x512, about 12 MB; five textures are shared by two
gadgets each. Thumbnails (`ThumbnailTexture`, 256x256) exist too and are not exported.

**Icon bases** (`ui/`): in the game a gadget is its icon drawn on a base: a blue hexagon
(1 slot), a longer shape (2 and 3 slots), a notched kit frame, a silver frame for the "Mini"
gadgets, a gold one in the Super Slot. The extractor writes them cut to what is drawn (the
game's canvases have transparent margins, the 2-slot base sits on a 3-slot canvas):

| File | Texture (`/Game/01_Union/UI/Common/GadgetIcon/Texture/`) | Used for |
|---|---|---|
| `base-1.png`, `base-2.png`, `base-3.png` | `UI_GadgetIcon_Base_01`, `_02`, `_03` | the usual gadgets, by slot cost |
| `base-kit-1.png`, `base-kit-3.png` | `UI_GadgetIcon_Base_01_Kit_001`, `UI_GadgetIcon_Base_03_Kit_001` | `base: kit` |
| `base-mini.png` | `UI_GadgetIcon_Base_01_Mini` | `base: mini` |
| `base-super.png` | `UI_GadgetIcon_Base_01_Super_01` | the gadget in the Super Slot |

The game picks the base in native code (`EquipmentGadgetIcon::SetIconBaseShape`); its
widgets list the materials (`BaseIconGadgetKitMaterialRefArray`, `FrameIconMiniGadgetMaterial`,
and in the gadget list's `WBP_GadgetIcon_Cost01_Icon`: `DefaultBaseMaterial`,
`KitBaseMaterial`, `ExGadgetDefaultBaseMaterial`, `ExGadgetKitBaseMaterial`,
`MiniBaseMaterial`). `mini` comes from `DA_MiniGadgetList`; `kit` from the categories whose
id ends with "Kit" (32 kits, all 3-slot in 2.0.0; Gibax checked in the game that the advanced
ones, the Spin Dash kit included, have the same kit frame as the others).

As in the game (Gibax): **in the gadget list every gadget is drawn on a 1-slot base** (the
slot cost is shown above it); **on the plate, on its real size**, a 2- or 3-slot base
stretched by its middle to its cells' width (the pointed ends, 115 of 435 pixels on each
side, kept as drawn). The page draws the 512-pixel icon centred over the base, at 512/435 of
the base's height, as the two textures line up in the game. The pink "EX" bases
(`UI_GadgetIcon_Base_*_EX`, `..._Kit_*_EX`) are not used for any gadget seen so far and are
not exported.

Gadget ids are Unreal names, which ignore letter case: compare them ignoring case. In game
2.0.0 the save spells them as the table's `ID` field does (the id the extractor writes),
but the table's row names sometimes differ in case (`run45_...` against `Run45_...`), and
so do asset paths (an icon named `..._run60` in the table is `..._Run60` in the paks): the
extractor looks files up ignoring case.
