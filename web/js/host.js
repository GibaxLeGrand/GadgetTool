// Where the editor's data comes from. In the app, the C# side answers through WebView2
// (AppBridge: reads and writes the save, file pickers, game detection). In a plain browser,
// a development host stands in for it: made-up data, nothing is ever written.
(function () {
  "use strict";

  /** The app: calls go to AppBridge as { id, method, params }; answers come back by id. */
  function appHost(webview) {
    let next = 1;
    const pending = new Map();
    webview.addEventListener("message", (e) => {
      const m = e.data;
      const p = m && pending.get(m.id);
      if (!p) return;
      pending.delete(m.id);
      if (m.error) p.reject(Object.assign(new Error(m.error.message), { kind: m.error.kind }));
      else p.resolve(m.result);
    });
    const call = (method, params = {}) => new Promise((resolve, reject) => {
      const id = next++;
      pending.set(id, { resolve, reject });
      webview.postMessage({ id, method, params });
    });
    return {
      kind: "app",
      catalog: () => call("catalog"),
      openSave: () => call("openSave"),
      openSample: () => call("openSample"),
      reloadSave: () => call("reloadSave"),
      save: (players) => call("save", { players }),
      gameRunning: () => call("gameRunning"),
      exportFile: (suggestedName, content) => call("exportFile", { suggestedName, content }),
      importFile: () => call("importFile"),
    };
  }

  /** A plain browser, for working on the page: serve the repository root and open /web/. */
  function devHost() {
    let running = false;
    let lastText = null;
    let lastName = null;

    const fail = (kind, message) => Object.assign(new Error(message), { kind });

    async function fetchJson(url) {
      const r = await fetch(url, { cache: "no-store" });
      if (!r.ok) throw new Error(`${url}: ${r.status}`);
      return r.json();
    }

    function pickFile(accept) {
      return new Promise((resolve) => {
        const input = Object.assign(document.createElement("input"), { type: "file", accept });
        input.onchange = async () => {
          const file = input.files[0];
          resolve(file ? { content: await file.text(), fileName: file.name } : { cancelled: true });
        };
        input.oncancel = () => resolve({ cancelled: true });
        input.click();
      });
    }

    /** DevJsonSaveFormat (the stand-in save format) read the way AppBridge reports it. */
    function parseDevSave(text, fileName) {
      const doc = JSON.parse(text);
      if (doc.devSave !== "gadgettool") throw fail("saveFormat", "Not a GadgetTool dev save.");
      if (doc.version !== "dev-1") throw fail("saveFormat", `Save version '${doc.version}' is unknown.`);
      return {
        fileName,
        version: doc.version,
        players: doc.players.map((p) => ({
          number: p.player,
          level: GT.levelName(p.rows, !!p.superSlot),
          rows: p.rows,
          superSlot: !!p.superSlot,
          unlocked: p.unlocked,
          plates: p.plates.map((pl) => ({ gadgets: pl.gadgets, super: pl.super || null })),
        })),
      };
    }

    return {
      kind: "browser",
      // A local test catalog (git-ignored) wins over the made-up one; its sample save is then generated.
      async catalog() {
        try { this.local = await fetchJson("../extracted/wiki-catalog.json"); return this.local; }
        catch { this.local = null; return fetchJson("../data/dev/catalog.json"); }
      },
      async openSample() {
        if (this.local) return GTSample.make(this.local);
        const doc = await fetchJson("../data/dev/save.json");
        lastText = JSON.stringify(doc);
        lastName = "save.json (sample)";
        return parseDevSave(lastText, lastName);
      },
      async openSave() {
        const file = await pickFile(".json,application/json");
        if (file.cancelled) return file;
        lastText = file.content;
        lastName = file.fileName;
        return parseDevSave(lastText, lastName);
      },
      async reloadSave() {
        if (!lastText) throw fail("noSave", "No save is open.");
        return parseDevSave(lastText, lastName);
      },
      async save(players) {
        if (running) throw fail("gameRunning", "Close the game before saving: it would overwrite the changes, and Steam Cloud could sync a half-written save.");
        return { saved: players.length, backup: "(browser preview: nothing was written)" };
      },
      async gameRunning() { return { running }; },
      toggleGame() { running = !running; return running; },
      async exportFile(name, content) {
        const url = URL.createObjectURL(new Blob([content], { type: "application/json" }));
        const a = Object.assign(document.createElement("a"), { href: url, download: name });
        document.body.append(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        return { fileName: name };
      },
      importFile: () => pickFile(".json,application/json"),
    };
  }

  /** A sample save for any catalog (browser only, for the local test catalog). */
  window.GTSample = {
    make(catalog) {
      let seed = 20261119;
      const rnd = () => {
        seed |= 0; seed = (seed + 0x6d2b79f5) | 0;
        let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
      };
      const shuffle = (a) => { a = a.slice(); for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };
      const specs = [
        { number: 1, rows: [3, 3], superSlot: true, ratio: 0.92 },
        { number: 2, rows: [3, 3], superSlot: false, ratio: 0.6 },
        { number: 3, rows: [3, 2], superSlot: false, ratio: 0.4 },
        { number: 4, rows: [3, 0], superSlot: false, ratio: 0.25 },
      ];
      return {
        fileName: "generated sample",
        version: "dev-1",
        players: specs.map((s) => {
          const owned = catalog.gadgets.filter(() => rnd() < s.ratio);
          const plates = Array.from({ length: 10 }, (_, i) => {
            const plate = { gadgets: [], super: null };
            if (i < 3 || rnd() < 0.45) {
              const costs = [];
              for (const g of shuffle(owned)) if (GT.arrange([...costs, g.slotCost], s.rows)) { plate.gadgets.push(g.id); costs.push(g.slotCost); }
              if (s.superSlot && rnd() < 0.8) plate.super = shuffle(owned.filter((g) => g.slotCost === 1 && !plate.gadgets.includes(g.id)))[0]?.id ?? null;
            }
            return plate;
          });
          return { number: s.number, level: GT.levelName(s.rows, s.superSlot), rows: s.rows, superSlot: s.superSlot, unlocked: owned.map((g) => g.id), plates };
        }),
      };
    },
  };

  window.GTHost = window.chrome && window.chrome.webview ? appHost(window.chrome.webview) : devHost();
})();
