// The editor's copy of the Core's rules (src/GadgetTool.Core), for instant feedback while
// editing. Same rules, same share-code bytes, same JSON export format. The app's C# side
// checks every rule again before it writes a save; a test checks both make the same codes.
//
// Shapes:
//   plate  = { gadgets: [id...], super: id | null }
//   player = { number, level, rows: [n, n], superSlot, unlocked: Set<id>, plates: [plate...] }
//   save   = { version, players: [player...] }
(function () {
  "use strict";

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

  /** Every slot of the plate, the Super Slot included (7 on a Grand Master plate). */
  const capacity = (player) => slotCount(player) + (player.superSlot ? 1 : 0);

  /** Slots the plate's known gadgets take, the Super Slot included. */
  const used = (plate, catalog) =>
    plate.gadgets.reduce((n, id) => n + (catalog.byId.get(id)?.slotCost ?? 0), 0) + (plate.super ? 1 : 0);

  /** PlateLayout.LevelName in the Core. */
  function levelName(rows, superSlot) {
    if (superSlot) return "Grand Master";
    const n = rows.reduce((a, b) => a + b, 0);
    return n <= 2 ? "Normal" : n === 3 ? "Bronze" : n === 4 ? "Silver" : n === 5 ? "Gold" : "Master";
  }

  // ---- Rules -------------------------------------------------------------------------

  /** Every rule the plate breaks for this player (same kinds as PlateIssueKind in the Core). */
  function validate(plate, player, catalog) {
    const issues = [];
    const seen = new Set();
    const costs = [];
    const check = (id) => {
      const key = idKey(id);
      if (seen.has(key)) { issues.push({ kind: "Duplicate", id }); return null; }
      seen.add(key);
      const g = catalog.byId.get(id);
      if (!g) { issues.push({ kind: "UnknownGadget", id }); return null; }
      if (!player.unlocked.has(g.id)) issues.push({ kind: "NotUnlocked", id });
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
      const g = catalog.byId.get(id); // ids ignore case: keep the catalog's spelling
      if (g && !seen.has(idKey(id)) && player.unlocked.has(g.id)) { kept.push(g.id); seen.add(idKey(id)); }
      else dropped.push(id);
    }
    let sup = plate.super ?? null;
    if (sup) {
      const g = catalog.byId.get(sup);
      if (!(player.superSlot && !seen.has(idKey(sup)) && g && g.slotCost === 1 && player.unlocked.has(g.id))) {
        dropped.push(sup);
        sup = null;
      } else {
        sup = g.id;
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

  // ---- Game versions (GameVersion in the Core) ---------------------------------------

  /** "2.0.0" -> [2, 0, 0]; anything else -> null (unknown). */
  function parseVersion(text) {
    if (typeof text !== "string" || !/^\d{1,3}(\.\d{1,3}){0,2}$/.test(text.trim())) return null;
    const parts = text.trim().split(".").map(Number);
    if (parts.some((p) => p > 255)) return null;
    while (parts.length < 3) parts.push(0);
    return parts.every((p) => p === 0) ? null : parts;
  }
  const versionText = (v) => (v ? v.join(".") : null);

  /** A share's version against ours: "same", "older" (check its gadgets), "newer" (refused) or "unknown" (check). */
  function ageOf(shared, ours) {
    if (!shared || !ours) return "unknown";
    for (let i = 0; i < 3; i++) if (shared[i] !== ours[i]) return shared[i] < ours[i] ? "older" : "newer";
    return "same";
  }

  /** then: [{ id, name, slotCost }] as a share recorded them -> what is different now. */
  function compareGadgets(then, catalog) {
    const changes = [];
    const seen = new Set();
    for (const g of then) {
      if (seen.has(g.id)) continue;
      seen.add(g.id);
      const now = catalog.byId.get(g.id);
      if (!now) changes.push({ id: g.id, name: g.name || null, then: g.slotCost, now: null });
      else if (now.slotCost !== g.slotCost) changes.push({ id: g.id, name: now.name, then: g.slotCost, now: now.slotCost });
    }
    return changes;
  }

  const describeChange = (c) => c.now == null
    ? `${c.name || c.id} is not in this game version.`
    : `${c.name || c.id}: ${c.then} slot${c.then === 1 ? "" : "s"} when shared, ${c.now} now.`;

  // Bytes: format 1, row count, flags (bit 0: Super Slot gadget), game version (3 bytes),
  // one 32-bit hash per gadget (rows, then Super Slot), slot costs (2 bits each, 0 = unknown),
  // checksum (16 bits). Same as ShareCode in the Core.
  const HEADER = 6;
  const costBytes = (n) => Math.ceil(n / 4);
  const codeLength = (n) => HEADER + 4 * n + costBytes(n) + 2;

  function encodeShareCode(plate, catalog) {
    const ids = plate.super ? [...plate.gadgets, plate.super] : plate.gadgets.slice();
    const bytes = new Uint8Array(codeLength(ids.length));
    const view = new DataView(bytes.buffer);
    const version = catalog.gameVersion || [0, 0, 0];
    bytes[0] = 1;
    bytes[1] = plate.gadgets.length;
    bytes[2] = plate.super ? 1 : 0;
    bytes.set(version, 3);
    const costAt = HEADER + 4 * ids.length;
    ids.forEach((id, i) => {
      view.setUint32(HEADER + 4 * i, shareHash(catalog.byId.get(id)?.id ?? id), true);
      const cost = catalog.byId.get(id)?.slotCost ?? 0;
      bytes[costAt + (i >> 2)] |= (cost >= 1 && cost <= 3 ? cost : 0) << (2 * (i % 4));
    });
    view.setUint16(bytes.length - 2, checksum(bytes.subarray(0, bytes.length - 2)), true);
    return PREFIX + toBase64Url(bytes);
  }

  /** -> { plate, gameVersion, age, changes, missing } (gadgets this version does not know are left out). */
  function decodeShareCode(code, catalog) {
    const text = code.trim();
    if (!text.toUpperCase().startsWith(PREFIX)) throw new Error(`Not a GadgetTool code: it should start with “${PREFIX}”.`);
    const bytes = fromBase64Url(text.slice(PREFIX.length));
    if (bytes.length < HEADER + 2) throw new Error("The code is too short; part of it may be missing.");
    if (bytes[0] !== 1) throw new Error(`The code uses format ${bytes[0]}, which this version does not know.`);
    const count = bytes[1], hasSuper = (bytes[2] & 1) !== 0, n = count + (hasSuper ? 1 : 0);
    if (count > 32 || (bytes[2] & ~1) !== 0 || bytes.length !== codeLength(n)) throw new Error("The code is damaged: its length does not match its content.");
    const view = new DataView(bytes.buffer);
    if (view.getUint16(bytes.length - 2, true) !== checksum(bytes.subarray(0, bytes.length - 2)))
      throw new Error("The code is damaged: its checksum does not match (a typo, or a character lost when copying).");

    const version = bytes[3] || bytes[4] || bytes[5] ? [bytes[3], bytes[4], bytes[5]] : null;
    const costAt = HEADER + 4 * n;
    const rows = [], then = [];
    let sup = null, missing = 0;
    for (let i = 0; i < n; i++) {
      const g = catalog.byHash.get(view.getUint32(HEADER + 4 * i, true));
      const cost = (bytes[costAt + (i >> 2)] >> (2 * (i % 4))) & 3;
      if (!g) { missing++; continue; }
      if (i < count) rows.push(g.id); else sup = g.id;
      if (cost) then.push({ id: g.id, name: g.name, slotCost: cost });
    }
    const age = ageOf(version, catalog.gameVersion);
    return {
      plate: { gadgets: rows, super: sup },
      gameVersion: versionText(version),
      age,
      changes: age === "same" ? [] : compareGadgets(then, catalog),
      missing,
    };
  }

  // ---- Presets files (SetupJson in the Core: same format, extension .gtpresets) ------

  const FILE_EXTENSION = ".gtpresets";

  /** entries: [{ name, from: { player, index } | null, plate }] */
  function exportJson(entries, catalog) {
    const gadgets = {};
    const used = [...new Set(entries.flatMap((e) => (e.plate.super ? [...e.plate.gadgets, e.plate.super] : e.plate.gadgets)))].sort();
    for (const id of used) {
      const g = catalog.byId.get(id);
      if (g) gadgets[id] = { name: g.name, slotCost: g.slotCost };
    }
    return JSON.stringify({
      format: "gadgettool.setups",
      version: 1,
      ...(catalog.gameVersion ? { gameVersion: versionText(catalog.gameVersion) } : {}),
      gadgets,
      plates: entries.map((e) => ({
        ...(e.name ? { name: e.name } : {}),
        ...(e.from ? { player: e.from.player, preset: String.fromCharCode(65 + e.from.index) } : {}),
        gadgets: e.plate.gadgets,
        ...(e.plate.super ? { super: e.plate.super } : {}),
      })),
    }, null, 2);
  }

  /** -> { gameVersion, age, entries: [{ name, from, plate, changes }] } */
  function importJson(text, catalog) {
    let doc;
    try { doc = JSON.parse(text); } catch (e) { throw new Error("Not a share code, nor a presets file: " + e.message); }
    if (!doc || doc.format !== "gadgettool.setups") throw new Error("Not a GadgetTool presets file.");
    if (doc.version > 1) throw new Error(`The file uses version ${doc.version}; this GadgetTool reads up to 1.`);
    if (!Array.isArray(doc.plates)) throw new Error("The file has no presets list.");
    const version = parseVersion(doc.gameVersion);
    const age = ageOf(version, catalog.gameVersion);
    const recorded = doc.gadgets && typeof doc.gadgets === "object" ? doc.gadgets : {};
    const entries = doc.plates.map((p, i) => {
      if (!p || !Array.isArray(p.gadgets) || p.gadgets.some((id) => typeof id !== "string" || !id.trim()))
        throw new Error(`Preset ${i + 1} has no valid gadgets list.`);
      let from = null;
      if (p.player !== undefined || p.preset !== undefined) {
        if (!(p.player >= 1 && p.player <= 4) || !/^[A-Z]$/.test(p.preset || ""))
          throw new Error(`Preset ${i + 1} has a bad origin: a player is 1 to 4 and a preset a letter.`);
        from = { player: p.player, index: p.preset.charCodeAt(0) - 65 };
      }
      const plate = { gadgets: p.gadgets.slice(), super: p.super || null };
      const then = (plate.super ? [...plate.gadgets, plate.super] : plate.gadgets)
        .filter((id) => recorded[id] && recorded[id].slotCost >= 1)
        .map((id) => ({ id, name: recorded[id].name, slotCost: recorded[id].slotCost }));
      return {
        name: typeof p.name === "string" ? p.name : null,
        from,
        plate,
        changes: age === "same" ? [] : compareGadgets(then, catalog),
      };
    });
    return { gameVersion: versionText(version), age, entries };
  }

  // ---- Data from the host ------------------------------------------------------------

  // ---- Languages of the game's texts (LocalizedCatalog in the Core) -------------------

  /** A text that is either a string (default language) or { lang: text }. */
  function textIn(value, lang, fallback) {
    if (value == null) return null;
    if (typeof value === "string") return value;
    const keys = Object.keys(value);
    const find = (l) => keys.find((k) => k.toLowerCase() === l.toLowerCase());
    const base = lang.split("-")[0];
    const key = find(lang) || find(base) || find(fallback) || keys[0];
    return key ? value[key] : null;
  }

  /** The closest language the data has: same, then base ("fr-FR" -> "fr", "zh-CN" -> "zh-Hans"), then default. */
  function resolveLanguage(languages, requested, fallback) {
    if (!requested) return fallback;
    const req = requested.toLowerCase();
    const base = req.split("-")[0];
    return languages.find((l) => l.toLowerCase() === req)
      || languages.find((l) => l.toLowerCase() === base || l.toLowerCase().startsWith(base + "-"))
      || fallback;
  }

  /** A catalog file (gadgettool.catalog) in one language, as AppBridge's "catalog" sends it. */
  function localizeCatalog(file, requested) {
    const fallback = file.defaultLanguage || "en";
    const others = new Set();
    for (const g of file.gadgets) if (g.name && typeof g.name === "object") Object.keys(g.name).forEach((l) => others.add(l));
    const languages = [fallback, ...[...others].filter((l) => l.toLowerCase() !== fallback.toLowerCase()).sort()];
    const language = resolveLanguage(languages, requested, fallback);
    return {
      source: file.source || "",
      gameVersion: file.gameVersion,
      language,
      languages,
      gadgets: file.gadgets.map((g) => ({
        id: g.id,
        slotCost: g.slotCost,
        name: textIn(g.name, language, fallback) || g.id,
        category: textIn(g.category, language, fallback) || "",
        categoryId: g.categoryId || textIn(g.category, fallback, fallback) || "",
        description: textIn(g.description, language, fallback),
        icon: g.icon || null,
      })),
    };
  }

  /** Gadget ids are Unreal names and ignore letter case, as in the Core (GadgetIds). */
  function idKey(id) {
    return String(id).toLowerCase();
  }

  /** Gadgets by id, ignoring letter case. */
  function idMap(gadgets) {
    const map = new Map(gadgets.map((g) => [idKey(g.id), g]));
    return { get: (id) => map.get(idKey(id)), has: (id) => map.has(idKey(id)) };
  }

  /** A catalog as the host sends it ({ source, gadgets }) with lookups. */
  function makeCatalog(source) {
    const gadgets = source.gadgets;
    return {
      source: source.source || "",
      gameVersion: parseVersion(source.gameVersion),
      language: source.language || "en",
      languages: source.languages || [source.language || "en"],
      gadgets,
      byId: idMap(gadgets),
      byHash: new Map(gadgets.map((g) => [shareHash(g.id), g])),
      categories: [...new Set(gadgets.map((g) => g.category || "Other"))],
    };
  }

  /** A save as the host sends it (AppBridge's openSave) in the editor's shape. */
  function fromSaveDto(dto) {
    return {
      version: dto.version,
      names: new Map(Object.entries(dto.names || {})),
      players: dto.players.map((p) => ({
        number: p.number,
        level: p.level || levelName(p.rows, p.superSlot),
        rows: p.rows,
        superSlot: !!p.superSlot,
        unlocked: new Set(p.unlocked),
        plates: p.plates.map((pl) => ({ gadgets: pl.gadgets.slice(), super: pl.super || null })),
      })),
    };
  }

  /** What AppBridge's save expects: every plate of every player. */
  function toSaveRequest(save) {
    return save.players.map((p) => ({
      number: p.number,
      plates: p.plates.map((pl) => (pl.super ? { gadgets: pl.gadgets, super: pl.super } : { gadgets: pl.gadgets })),
    }));
  }

  window.GT = {
    arrange, slotCount, capacity, used, levelName, validate, describe, paste, withPlate, samePlate, clonePlate,
    encodeShareCode, decodeShareCode, FILE_EXTENSION, exportJson, importJson, makeCatalog, fromSaveDto, toSaveRequest,
    parseVersion, versionText, ageOf, compareGadgets, describeChange, localizeCatalog,
    presetLetter: (i) => String.fromCharCode(65 + i),
  };
})();
