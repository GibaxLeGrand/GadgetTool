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
    ui/plate-<n>.png    the game's plates, one per picture (release only, never in git)
    ui/lock.png, ui/plus.png   the lock and "+" marks of the game's plate screen (same)
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
| `plates` | top | yes | The plate levels: `{ id, slots, superSlot, presets, image, name }` (`image` is `ui/<name>.png`; anything else is refused). |
| `plateGeometry` | top | yes | Where things go on a plate picture, in its pixels: `width`, `height`, `cell` (a cell's size), `first` (the first cell's centre), `step` (to the next cell across, to the next row down), `super` (the Super Slot's centre), `title` (left, top, right, bottom of the name band). |
| `base` | gadget | yes | The base the game draws the icon on: `kit`, `mini`, or absent for the usual hexagon. |
| `icon` | gadget | via `icons/` | The icon's asset path in the game's files; the icon itself is `icons/<id>.png`. |

Texts are kept as the game has them: a description may hold a placeholder the game fills in
(`{GetRing}`, `{AllParam}`, `{CostRing}`, `{Prob}`, `{Rate}`, `{Count}`: 43 texts in 2.0.0) and
rich-text tags (`<SpStyle>...</>`). The page shows the tagged part in bold and each
placeholder as a "?": the values sit in the game's gadget parameters (`DA_GadgetParameter2`,
per level and per Default / Mini / Special variant) and which one a text shows is decided by
the game's code, so GadgetTool does not guess them.

## The extractor

Reads `DT_GadgetData`, `DT_GadgetCustomCategoryData` and `DT_GadgetPlateData` (`/Game/01_Union/Database/Gadget/`)
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
| `super-outline.png`, `super-mask.png` | `UI_GadgetIcon_Base_01_Super_01_Mask_Outline`, `..._Mask` | the Super base's light (see below) |

The game picks the base in native code (`EquipmentGadgetIcon::SetIconBaseShape`); its
widgets list the materials (`BaseIconGadgetKitMaterialRefArray`, `FrameIconMiniGadgetMaterial`,
and in the gadget list's `WBP_GadgetIcon_Cost01_Icon`: `DefaultBaseMaterial`,
`KitBaseMaterial`, `ExGadgetDefaultBaseMaterial`, `ExGadgetKitBaseMaterial`,
`MiniBaseMaterial`). `mini` comes from `DA_MiniGadgetList`; `kit` from the categories whose
id ends with "Kit" (32 kits, all 3-slot in 2.0.0; Gibax checked in the game that the advanced
ones, the Spin Dash kit included, have the same kit frame as the others).

**Plates** (`ui/plate-<n>.png`): the plate levels come from `DT_GadgetPlateData`; the
pictures from the atlas `UI_GadgetCustom_GadgetPlate` (`/Game/01_Union/UI/Garage/GadgetCustom/Texture/`,
4096x4096, seven frames of 2048x1024, two per row), each cut to what is drawn (1488x852 at
296,88 of its frame, the same for all seven). Game 2.0.0:

| Plate (`id`) | Slots | Super Slot | Picture | Name (en) |
|---|---|---|---|---|
| `Rank0` | 2 | no | `plate-0` | NORMAL PLATE |
| `Rank1` | 3 | no | `plate-1` | BRONZE PLATE |
| `Rank2` | 4 | no | `plate-2` | SILVER PLATE |
| `Rank3` | 5 | no | `plate-3` | GOLD PLATE |
| `Rank4` | 6 | no | `plate-4` | MASTER PLATE |
| `Rank5` to `Rank7` | 6 | yes | `plate-6` | GRAND MASTER PLATE |
| `RentalPlate` | 6 | yes | `plate-5` | RENTAL PLATE (festival; not used by the page) |

Every picture draws all six cells and the Super Slot as empty hexagons (the game's empty
cell, a dark hexagon with a "G"). The geometry comes from `WBP_GadgetCustom_Sub_GadgetPlate`:
cells of 307 pixels (512 scaled by 0.6), the first centred at 222,348 of the cut picture, the
next 374 across and 316 down, the Super Slot at 1261,499 (the widget says 1257; 1261 is the centre of the picture's gold hole, and Gibax saw its rim on the right at 1257), the name band at 25,85 to 1400,175.
The page fills the cells left to right, top row first; it leaves an empty cell as the picture
draws it (a "+" on hover), and darkens a cell the plate level does not open under the game's
lock (`UI_GudgetCustom_LockIcon`; the "+" is `UI_GudgetCustom_PlusMark`). Without a pack, the
page draws its own plate.

As in the game (Gibax): **in the gadget list every gadget is drawn on a 1-slot base** (the
slot cost is shown above it); **on the plate, on its real size**. The game draws its
1024x512 2- and 3-slot textures (`Base_02`, `Base_03` in `WBP_GadgetIcon`) at 2048x512: the
whole drawing twice as wide, which covers 2 cells (1132 of 1135 icon pixels, a cell being 512
and the next one 623 further at the plate's 0.6 scale) or 3 cells (1756 of 1758); the small
hexagons at the ends of the kit frame are drawn squashed in the texture for that. The page
draws them the same way on the game's plate, centred on their cells (on its own plate,
without a data pack, it stretches them by the middle); the game moves the icon by 314 and 628
for 2 and 3 slots (`GadgetIcon_Loop_Cost2`, `_Cost3`), the middle of the cells, as the page
does. The page draws the 512-pixel icon centred over the base, at 512/435 of the base's
height, as the two textures line up in the game. The 1-slot bases (usual, kit, Mini, Super) are 512x512 textures that the game draws in
a 580x512 image (`Base_01` in `WBP_GadgetIcon`, `DMY_Frame` in the list's
`WBP_GadgetIcon_Cost01_Icon`): 580/512 wider, which fills the plate's cell; the page does the
same, the icon keeping its shape.

**The Super Slot's gold base** is drawn with the material `M_UI_TitlePlateR_Gadget`: the gold
texture (483 of 512 pixels high where the others are 435), with, added on top
(`Blend_LinearDodge`), its "G" pattern (`..._Mask`) lit in `#FFD235` and its outline
(`..._Mask_Outline`) in `#FFA346`, and a `#FFF800` light turning around the centre
(`VectorToRadialValue`, `UI_CMN_Material_Scroll_MaskAlpha2`, speed 10); colours and textures
are the material's default values (`CachedExpressionData`). The extractor writes both masks
(8-bit grey in the game, `PF_G8`) as white with the grey as opacity, cut to the same box as
`base-super.png`; the page adds them over the base. How bright each part is, and the light's
speed, is the page's guess: the material's graph is not in the game's files.

The Mini frame is the usual base at 0.8 (its
blue edge 359 pixels wide instead of 448, its dark inside 324 instead of 407) in a silver
ring, and the game draws a Mini gadget's icon at 0.8 too (Gibax's renders of "Lv1 Quick
Charge" and "Mini Lv1 Quick Charge"); the page does the same. The pink "EX" bases (`UI_GadgetIcon_Base_*_EX`, `..._Kit_*_EX`) are for the festival
rules' EX gadgets (`WBP_StatusInfo_Sub_EXGadgetDetail`, `festa_rule_gadget_ex`) and are
not exported.

Gadget ids are Unreal names, which ignore letter case: compare them ignoring case. In game
2.0.0 the save spells them as the table's `ID` field does (the id the extractor writes),
but the table's row names sometimes differ in case (`run45_...` against `Run45_...`), and
so do asset paths (an icon named `..._run60` in the table is `..._Run60` in the paks): the
extractor looks files up ignoring case.
