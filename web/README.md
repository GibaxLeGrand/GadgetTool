# Web UI

The editor's interface. The app (`src/GadgetTool.App`) shows it in WebView2; it also runs in
a plain browser for work on the page.

| File | What |
|---|---|
| `index.html`, `css/app.css`, `js/app.js` | The editor: players 1 to 4, presets A to J with names (GadgetTool only), the plate (two rows of 3 slots and the Super Slot, 7 slots on a Grand Master plate), copy/paste, share codes, `.gtpresets` files, undo/redo. Two views: **Lists** (default: scrolling presets and gadgets) and **Game style** (preset tabs over the plate, a description box, gadgets in rows by category, as in the game's menu). Colours are a separate setting for either view: per view (default), dark, or the game's. |
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

## Sharing

- **Share code** (`GT1-…`, about 40 characters): one preset's gadgets, to paste in a chat.
- **`.gtpresets` file** (JSON inside): one preset, a player's presets or every player's,
  with their names and where they came from. "Import…" puts a preset on the selected one,
  or puts a whole file back where it came from.

## Keys

Ctrl+O open a save, Ctrl+S save, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+C / Ctrl+V copy and
paste a preset, Up / Down or Page Up / Page Down change preset. Presets can be dragged onto
one another (Lists view). In the Game style view, right-click a 1-slot gadget to put it in
the Super Slot.
