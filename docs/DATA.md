# Game data: what the extractor writes, what the app reads

GadgetTool's code lives in this repository; the game's data does not. The extractor (to come,
run by a maintainer with the game installed and its AES key) reads the game's files and
writes a **data pack** for one game version. Packs ship on the
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

## What the extractor still has to find out (on a machine with the game)

See `notes/memoire-agent.md`: AES key, mappings (`.usmap`), where the gadget data lives and
which fields hold the id, slot cost, category and icon, the localisation files, and where
the game states its version.
