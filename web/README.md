# Web UI

The editor's interface. The app (`src/GadgetTool.App`) shows it in WebView2; it also runs in
a plain browser for work on the page.

| File | What |
|---|---|
| `index.html`, `css/app.css`, `js/app.js` | The editor: players 1 to 4, presets A to J, the plate (two rows of 3 slots and the Super Slot), the gadget list, copy/paste, share codes, JSON export/import, undo/redo. |
| `js/rules.js` | The Core's rules in JS, for instant feedback while editing (same share-code bytes and JSON format; a C# test checks the codes match). The app checks every rule again before writing. |
| `js/host.js` | Where data and files come from. In the app: calls to `AppBridge` through WebView2 messages (`{ id, method, params }`). In a browser: a stand-in that never writes anything. |

Icons are placeholders (initials on the category's colour). The real ones will be read
from the game's files at runtime; no game asset is ever committed.

## In a browser

Serve the repository root and open `/web/`:

```
python -m http.server 8766 --bind 127.0.0.1
```

The page opens the sample at once: `data/dev/catalog.json` and `data/dev/save.json` (made-up
gadgets). If the git-ignored `extracted/wiki-catalog.json` exists, its gadgets are used and a
sample save is generated for them. "Game closed" can be clicked to pretend the game runs.

## Keys

Ctrl+O open a save, Ctrl+S save, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+C / Ctrl+V copy and
paste a preset, Up / Down change preset. Presets can be dragged onto one another.
