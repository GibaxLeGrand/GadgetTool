// Stand-in for the C# Core (src/GadgetTool.Core) until the WebView2 host exists.
// Same rules, same share-code format, same JSON export format. When the host is
// written, the UI calls it instead and this file goes away.
//
// Shapes:
//   plate  = { gadgets: [id...], super: id | null }
//   player = { number, level, rows: [n, n], superSlot, unlocked: Set<id>, plates: [plate x presets] }
//   save   = { version, players: [player...] }
(function () {
  "use strict";

  const MAX_ROW = 3;

  // ---- Plate layout ------------------------------------------------------------------

  /** Row for each gadget (by slot cost, in plate order), or null when they cannot all fit. */
  function arrange(costs, rows) {
    const total = costs.reduce((a, b) => a + b, 0);
    if (total > rows.reduce((a, b) => a + b, 0)) return null;
    const order = costs.map((_, i) => i).sort((a, b) => costs[b] - costs[a]);
    const free = rows.slice();
    const out = new Array(costs.length);
    const place = (k) => {
      if (k === order.length) return true;
      const g = order[k];
      for (let r = 0; r < free.length; r++) {
        if (free[r] < costs[g]) continue;
        free[r] -= costs[g];
        out[g] = r;
        if (place(k + 1)) return true;
        free[r] += costs[g];
      }
      return false;
    };
    return place(0) ? out : null;
  }

  const slotCount = (player) => player.rows.reduce((a, b) => a + b, 0);

  // ---- Rules -------------------------------------------------------------------------

  /** Every rule the plate breaks for this player (same kinds as PlateIssueKind in the Core). */
  function validate(plate, player, catalog) {
    const issues = [];
    const seen = new Set();
    const costs = [];
    const check = (id) => {
      if (seen.has(id)) { issues.push({ kind: "Duplicate", id }); return null; }
      seen.add(id);
      const g = catalog.byId.get(id);
      if (!g) { issues.push({ kind: "UnknownGadget", id }); return null; }
      if (!player.unlocked.has(id)) issues.push({ kind: "NotUnlocked", id });
      return g;
    };
    for (const id of plate.gadgets) {
      const g = check(id);
      if (g) costs.push(g.slotCost);
    }
    if (plate.super) {
      if (!player.superSlot) issues.push({ kind: "NoSuperSlot", id: plate.super });
      const g = check(plate.super);
      if (g && g.slotCost > 1) issues.push({ kind: "SuperSlotNeedsOneSlotGadget", id: plate.super });
    }
    const used = costs.reduce((a, b) => a + b, 0);
    if (used > slotCount(player)) issues.push({ kind: "OverCapacity", used, count: slotCount(player) });
    else if (!arrange(costs, player.rows)) issues.push({ kind: "DoesNotFit" });
    return issues;
  }

  function describe(issue, catalog) {
    const name = (id) => catalog.byId.get(id)?.name ?? id;
    switch (issue.kind) {
      case "UnknownGadget": return `“${issue.id}” is not a gadget this game version knows.`;
      case "NotUnlocked": return `${name(issue.id)} is not unlocked for this player.`;
      case "Duplicate": return `${name(issue.id)} is equipped twice.`;
      case "OverCapacity": return `The gadgets take ${issue.used} slots; this plate has ${issue.count}.`;
      case "DoesNotFit": return "Enough slots in total, but the gadgets do not fit in the rows (a gadget cannot span two rows).";
      case "NoSuperSlot": return `${name(issue.id)} is in the Super Slot, but this plate has none (Grand Master only).`;
      case "SuperSlotNeedsOneSlotGadget": return `${name(issue.id)} takes more than one slot; the Super Slot takes 1-slot gadgets only.`;
      default: return issue.kind;
    }
  }

  // ---- Editing -----------------------------------------------------------------------

  const clonePlate = (p) => ({ gadgets: p.gadgets.slice(), super: p.super ?? null });
  const samePlate = (a, b) =>
    (a.super ?? null) === (b.super ?? null) &&
    a.gadgets.length === b.gadgets.length &&
    a.gadgets.every((id, i) => id === b.gadgets[i]);

  function withPlate(save, player, index, plate) {
    return {
      ...save,
      players: save.players.map((p) =>
        p.number !== player ? p : { ...p, plates: p.plates.map((q, i) => (i === index ? clonePlate(plate) : q)) }),
    };
  }

  function keepEquippable(plate, player, catalog) {
    const kept = [], dropped = [], seen = new Set();
    for (const id of plate.gadgets) {
      if (!seen.has(id) && catalog.byId.has(id) && player.unlocked.has(id)) { kept.push(id); seen.add(id); }
      else dropped.push(id);
    }
    let sup = plate.super ?? null;
    if (sup) {
      const g = catalog.byId.get(sup);
      if (!(player.superSlot && !seen.has(sup) && g && g.slotCost === 1 && player.unlocked.has(sup))) {
        dropped.push(sup);
        sup = null;
      }
    }
    return { plate: { gadgets: kept, super: sup }, dropped };
  }

  /** Paste one plate on many presets, all or nothing (SetupEditor.Paste in the Core). */
  function paste(save, plate, targets, catalog, mode) {
    const outcomes = [];
    let updated = save;
    const seen = new Set();
    for (const t of targets) {
      const key = t.player + ":" + t.index;
      if (seen.has(key)) continue;
      seen.add(key);
      const player = save.players.find((p) => p.number === t.player);
      const { plate: toPaste, dropped } = mode === "dropMissing"
        ? keepEquippable(plate, player, catalog)
        : { plate, dropped: [] };
      const issues = validate(toPaste, player, catalog);
      outcomes.push({ target: t, issues, dropped });
      if (issues.length === 0) updated = withPlate(updated, t.player, t.index, toPaste);
    }
    const applied = outcomes.every((o) => o.issues.length === 0);
    return { applied, save: applied ? updated : save, targets: outcomes };
  }

  // ---- Share codes (ShareCode in the Core: same bytes) --------------------------------

  const PREFIX = "GT1-";
  const utf8 = new TextEncoder();

  function fnv1a(bytes) {
    let h = 0x811c9dc5;
    for (const b of bytes) { h ^= b; h = Math.imul(h, 0x01000193) >>> 0; }
    return h >>> 0;
  }
  const shareHash = (id) => fnv1a(utf8.encode(id));
  const checksum = (bytes) => { const h = fnv1a(bytes); return (h ^ (h >>> 16)) & 0xffff; };

  function toBase64Url(bytes) {
    let s = "";
    for (const b of bytes) s += String.fromCharCode(b);
    return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }
  function fromBase64Url(text) {
    if (!/^[A-Za-z0-9_-]*$/.test(text) || text.length % 4 === 1) throw new Error("The code is damaged: it holds characters a code cannot contain.");
    const s = atob(text.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((text.length + 3) % 4));
    return Uint8Array.from(s, (c) => c.charCodeAt(0));
  }

  function encodeShareCode(plate) {
    const hashes = plate.gadgets.map(shareHash);
    if (plate.super) hashes.push(shareHash(plate.super));
    const bytes = new Uint8Array(3 + 4 * hashes.length + 2);
    const view = new DataView(bytes.buffer);
    bytes[0] = 1;
    bytes[1] = plate.gadgets.length;
    bytes[2] = plate.super ? 1 : 0;
    hashes.forEach((h, i) => view.setUint32(3 + 4 * i, h, true));
    view.setUint16(bytes.length - 2, checksum(bytes.subarray(0, bytes.length - 2)), true);
    return PREFIX + toBase64Url(bytes);
  }

  function decodeShareCode(code, catalog) {
    const text = code.trim();
    if (!text.toUpperCase().startsWith(PREFIX)) throw new Error(`Not a GadgetTool code: it should start with “${PREFIX}”.`);
    const bytes = fromBase64Url(text.slice(PREFIX.length));
    if (bytes.length < 5) throw new Error("The code is too short; part of it may be missing.");
    if (bytes[0] !== 1) throw new Error(`The code uses format ${bytes[0]}, which this version does not know.`);
    const count = bytes[1], hasSuper = (bytes[2] & 1) !== 0, n = count + (hasSuper ? 1 : 0);
    if (count > 32 || (bytes[2] & ~1) !== 0 || bytes.length !== 3 + 4 * n + 2) throw new Error("The code is damaged: its length does not match its content.");
    const view = new DataView(bytes.buffer);
    if (view.getUint16(bytes.length - 2, true) !== checksum(bytes.subarray(0, bytes.length - 2)))
      throw new Error("The code is damaged: its checksum does not match (a typo, or a character lost when copying).");
    const ids = [];
    for (let i = 0; i < n; i++) {
      const h = view.getUint32(3 + 4 * i, true);
      const g = catalog.byHash.get(h);
      if (!g) throw new Error("The code holds a gadget this game version does not know; it may come from a newer version.");
      ids.push(g.id);
    }
    return { gadgets: ids.slice(0, count), super: hasSuper ? ids[count] : null };
  }

  // ---- JSON export (SetupJson in the Core: same format) ------------------------------

  function exportJson(entries) {
    return JSON.stringify({
      format: "gadgettool.setups",
      version: 1,
      plates: entries.map((e) => ({
        ...(e.label ? { label: e.label } : {}),
        gadgets: e.plate.gadgets,
        ...(e.plate.super ? { super: e.plate.super } : {}),
      })),
    }, null, 2);
  }

  function importJson(text) {
    let doc;
    try { doc = JSON.parse(text); } catch (e) { throw new Error("Not valid JSON: " + e.message); }
    if (!doc || doc.format !== "gadgettool.setups") throw new Error("Not a GadgetTool setups file.");
    if (doc.version > 1) throw new Error(`The file uses version ${doc.version}; this GadgetTool reads up to 1.`);
    if (!Array.isArray(doc.plates)) throw new Error("The file has no plates list.");
    return doc.plates.map((p, i) => {
      if (!p || !Array.isArray(p.gadgets) || p.gadgets.some((id) => typeof id !== "string" || !id.trim()))
        throw new Error(`Plate ${i + 1} has no valid gadgets list.`);
      return { label: typeof p.label === "string" ? p.label : null, plate: { gadgets: p.gadgets.slice(), super: p.super || null } };
    });
  }

  // ---- Catalog and mock save ---------------------------------------------------------

  function makeCatalog(source) {
    const gadgets = source.gadgets;
    return {
      source: source.source,
      gadgets,
      byId: new Map(gadgets.map((g) => [g.id, g])),
      byHash: new Map(gadgets.map((g) => [shareHash(g.id), g])),
      categories: [...new Set(gadgets.map((g) => g.category))],
    };
  }

  function mulberry32(seed) {
    return function () {
      seed |= 0; seed = (seed + 0x6d2b79f5) | 0;
      let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // Layouts of the plates below Master are a guess until real saves are read.
  const PLAYERS = [
    { number: 1, level: "Grand Master", rows: [3, 3], superSlot: true, unlockRatio: 0.92 },
    { number: 2, level: "Master", rows: [3, 3], superSlot: false, unlockRatio: 0.6 },
    { number: 3, level: "Gold", rows: [3, 2], superSlot: false, unlockRatio: 0.4 },
    { number: 4, level: "Bronze", rows: [3, 0], superSlot: false, unlockRatio: 0.25 },
  ];

  function makeMockSave(catalog, presets = 10) {
    const rnd = mulberry32(20261119);
    const shuffle = (a) => { a = a.slice(); for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };
    const players = PLAYERS.map((spec) => {
      const unlocked = new Set(catalog.gadgets.filter(() => rnd() < spec.unlockRatio).map((g) => g.id));
      const owned = catalog.gadgets.filter((g) => unlocked.has(g.id));
      const plates = [];
      for (let i = 0; i < presets; i++) {
        const plate = { gadgets: [], super: null };
        if (i < 3 || rnd() < 0.45) {
          const costs = [];
          for (const g of shuffle(owned)) {
            if (arrange([...costs, g.slotCost], spec.rows)) { plate.gadgets.push(g.id); costs.push(g.slotCost); }
          }
          if (spec.superSlot && rnd() < 0.8) {
            const pick = shuffle(owned.filter((g) => g.slotCost === 1 && !plate.gadgets.includes(g.id)))[0];
            if (pick) plate.super = pick.id;
          }
        }
        plates.push(plate);
      }
      return { number: spec.number, level: spec.level, rows: spec.rows, superSlot: spec.superSlot, unlocked, plates };
    });
    // As a game patch could leave it: P2's last preset holds a gadget P2 does not own.
    const p2 = players[1];
    const notOwned = catalog.gadgets.find((g) => !p2.unlocked.has(g.id) && g.slotCost === 1);
    if (notOwned) p2.plates[presets - 1] = { gadgets: [notOwned.id], super: null };
    return { version: "mock-1", players };
  }

  window.GT = {
    arrange, slotCount, validate, describe, paste, withPlate, samePlate, clonePlate,
    encodeShareCode, decodeShareCode, exportJson, importJson, makeCatalog, makeMockSave,
    presetLetter: (i) => String.fromCharCode(65 + i),
  };
})();
