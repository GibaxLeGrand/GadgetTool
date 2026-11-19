# GadgetTool

An external gadget setup tool for **Sonic Racing CrossWorlds** (PC, Steam). It lets you
build, copy, paste, export and import gadget setups for every slot and every local player
(1 to 4, offline), without launching the game.

> **Status: early work, not usable on real saves yet.** The Windows app runs on a sample
> save with made-up gadgets: rules, copy/paste, share codes, export/import and safe saving
> all work. The game's save format is now known and the Core reads it (read only); the
> app does not open real saves yet, and nothing writes them. See [notes/](notes/) for the
> plan and the resume point.

## Build and run

Needs the .NET 10 SDK, on Windows (the app uses WebView2, part of Windows 11).

```
dotnet build GadgetTool.slnx
dotnet test GadgetTool.slnx
dotnet run --project src/GadgetTool.App
```

`--catalog <file>` uses another gadget list; `--dev` turns on the page's developer tools.

To open the game's own save (read only for now), give the app a data pack's catalog (its
`icons` folder next to it is used too) and a file holding the game's save key; neither is in
this repository (see [docs/DATA.md](docs/DATA.md)):

```
dotnet run --project src/GadgetTool.App -- --catalog <pack>\2.0.0\catalog.json --save-key <key file>
```

The open dialog starts in the game's save folder (`%APPDATA%\SEGA\SonicRacingCrossWorlds\steam`).
Backups go to `%LOCALAPPDATA%\GadgetTool\Backups`, never next to the save (its folder may be
synced by Steam Cloud).

| Folder | What |
|---|---|
| `src/GadgetTool.Core` | Rules, copy/paste, share codes, export/import, save formats, safe saving, the bridge the page talks to. No UI. |
| `src/GadgetTool.App` | The Windows window: the page in WebView2, file dialogs, game detection. |
| `src/GadgetTool.Extractor` | The maintainer's tool that reads the game's gadget tables and writes a data pack (see [docs/DATA.md](docs/DATA.md)). |
| `web/` | The editor's interface (see [web/README.md](web/README.md)). |
| `data/dev/` | Made-up gadgets and a sample save, until the game's files are read. |
| `tests/` | Unit tests for the Core and the extractor. |

## Planned features

- Read the save and show every gadget preset, for local players 1 to 4.
- Edit a preset: only gadgets you have already unlocked, respecting each gadget's slot cost,
  the plate's rows and the Super Slot (1-slot gadgets only).
- Copy and paste a preset to any other preset, of any player, or to many at once.
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
