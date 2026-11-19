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
```

`index.json` (planned) lets the app pick the pack for the installed game from the Steam
build id in `steamapps/appmanifest_2486820.acf`, without any key.

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

### Written by the extractor, not read by the app yet

The extractor also writes these; `CatalogJson` ignores them for now.

| Field | Where | Notes |
|---|---|---|
| `steamBuildId` | top | The Steam build the pack was read from (`buildid` in `appmanifest_2486820.acf`). |
| `languages` | top | Every language written, the default first. |
| `categories` | top | `{ id, sort, color, name }` for each category that holds a gadget, in the game's order; `color` is the game's category colour (`#RRGGBB`). |
| `superEffect` | gadget | The text shown when the gadget sits in the Super Slot (1-slot gadgets only). |
| `plate` | gadget | The plate level the gadget needs (`Rank0` to `Rank7`). |
| `icon` | gadget | The icon's asset path in the game's files, for the icon export (to come). |

Texts are kept as the game has them: a description may hold a placeholder the game fills in
(`{GetRing}`) and rich-text tags (`<SpStyle>...</>`). The app has to deal with both.

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

It refuses to write a pack when the data breaks a rule the app relies on (a gadget wider
than 3 slots or taller than 1 row, an id repeated ignoring case, an unknown category, a
gadget without a name in English) and lists every problem. Game 2.0.0 (Steam build
24996408): 148 gadgets (68 / 35 / 45 at 1 / 2 / 3 slots), 13 categories, 15 languages (`en`,
`ar`, `de`, `es`, `es-US`, `fr`, `it`, `ja`, `ko`, `pl`, `pt`, `ru`, `th`, `zh-Hans`,
`zh-Hant`), 68 Super effects. Icons are not exported yet.

Gadget ids are Unreal names, which ignore letter case: compare them ignoring case. In game
2.0.0 the save spells them as the table's `ID` field does (the id the extractor writes),
but the table's row names sometimes differ in case (`run45_...` against `Run45_...`).
