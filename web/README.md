# Web UI

The editor's interface. It becomes the app's real UI once a WebView2 host serves it; until
then it runs on its own in any browser, on a mock save.

| File | What |
|---|---|
| `index.html`, `css/app.css`, `js/app.js` | The editor: players 1 to 4, presets A to J, the plate (two rows of 3 slots and the Super Slot), the gadget list, copy/paste, share codes, JSON export/import, undo/redo. |
| `js/backend-mock.js` | Stands in for `src/GadgetTool.Core` (same rules, same share-code bytes, same JSON format) and makes a mock save. Replaced by calls to the host later. |
| `mock/mock-catalog.js` | Made-up gadgets, so the page works from a fresh clone. |

Icons are placeholders (initials on the category's colour). The real ones will be read
from the game's files at runtime; no game asset is ever committed.

## Run it

Open `index.html` in a browser, or serve the repository root and open `/web/`:

```
python -m http.server 8766 --bind 127.0.0.1
```

The page also tries `../extracted/wiki-catalog.js`, a local file (git-ignored) with the real
gadget names for testing; without it the browser logs one 404 and the made-up gadgets are
used.

## Keys

Ctrl+Z / Ctrl+Y undo and redo, Ctrl+C / Ctrl+V copy and paste a preset, Ctrl+S save,
Up / Down change preset. Presets can be dragged onto one another to copy them.
