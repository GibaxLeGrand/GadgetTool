// GadgetTool editor UI. Talks to window.GT (the mock backend for now, the C# Core
// through WebView2 later). No game asset: icons are placeholders (initials on the
// category's colour) until the game's own icons are read.
(function () {
  "use strict";

  const $ = (sel, root = document) => root.querySelector(sel);

  /** Small element builder: h("div", { class: "x", onclick }, child, "text", [more]). */
  function h(tag, attrs, ...children) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === null || v === undefined || v === false) continue;
      if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
      else if (k === "style") el.style.cssText = v;
      else if (k === "dataset") Object.assign(el.dataset, v);
      else el.setAttribute(k, v === true ? "" : v);
    }
    for (const c of children.flat(Infinity)) {
      if (c === null || c === undefined || c === false) continue;
      el.append(c instanceof Node ? c : document.createTextNode(String(c)));
    }
    return el;
  }

  // ---- State -------------------------------------------------------------------------

  const usingWiki = !!window.GT_LOCAL_CATALOG;
  const catalog = GT.makeCatalog(window.GT_LOCAL_CATALOG || window.GT_MOCK_CATALOG);
  const initial = GT.makeMockSave(catalog);

  const state = {
    saved: initial,
    current: initial,
    undo: [],
    redo: [],
    player: 1,
    preset: 0,
    clipboard: null, // { plate, label }
    gameRunning: false,
    filter: { text: "", category: "", cost: 0, showLocked: true },
  };

  const player = (n = state.player) => state.current.players.find((p) => p.number === n);
  const savedPlayer = (n) => state.saved.players.find((p) => p.number === n);
  const plate = () => player().plates[state.preset];
  const label = (n, i) => `P${n} ${GT.presetLetter(i)}`;
  const gadget = (id) => catalog.byId.get(id);

  function changedPlates() {
    const out = [];
    for (const p of state.current.players) {
      const s = savedPlayer(p.number);
      p.plates.forEach((pl, i) => { if (!GT.samePlate(pl, s.plates[i])) out.push({ player: p.number, index: i }); });
    }
    return out;
  }
  const isChanged = (n, i) => !GT.samePlate(player(n).plates[i], savedPlayer(n).plates[i]);

  function commit(next, message, kind = "ok") {
    if (next === state.current) return;
    state.undo.push(state.current);
    if (state.undo.length > 200) state.undo.shift();
    state.redo = [];
    state.current = next;
    render();
    if (message) toast(message, kind);
  }

  function setPlate(newPlate, message) {
    commit(GT.withPlate(state.current, state.player, state.preset, newPlate), message);
  }

  // ---- Look --------------------------------------------------------------------------

  function hue(text) {
    let x = 7;
    for (const c of text) x = (x * 31 + c.charCodeAt(0)) >>> 0;
    return x % 360;
  }
  function initials(name) {
    const words = name.replace(/[^\p{L}\p{N}+ ]/gu, " ").split(/\s+/).filter(Boolean);
    return (words.length === 1 ? words[0].slice(0, 2) : words.slice(0, 2).map((w) => w[0]).join("")).toUpperCase();
  }
  const icon = (g, small) =>
    h("span", { class: "icon" + (small ? " small" : ""), style: `--h:${hue(g.category)}`, "aria-hidden": "true" }, initials(g.name));
  const pips = (cost) => h("span", { class: "pips", title: `${cost} slot${cost > 1 ? "s" : ""}` },
    [1, 2, 3].map((i) => h("i", { class: i <= cost ? "on" : "" })));
  const tip = (g) => `${g.name} · ${g.slotCost} slot${g.slotCost > 1 ? "s" : ""} · ${g.category}\n${g.description || ""}`;

  function toast(text, kind = "ok") {
    const el = h("div", { class: "toast " + kind }, text);
    $("#toasts").append(el);
    setTimeout(() => el.remove(), kind === "bad" ? 6000 : 3200);
  }

  // ---- Render ------------------------------------------------------------------------

  function render() {
    renderTop();
    renderPlayers();
    renderPresets();
    renderEditor();
    renderLibrary();
  }

  function renderTop() {
    const n = changedPlates().length;
    $("#saveInfo").textContent =
      `${usingWiki ? "Wiki gadgets (local)" : "Made-up gadgets"} · mock save ${state.current.version}` +
      (n ? ` · ${n} unsaved preset${n > 1 ? "s" : ""}` : " · no unsaved change");
    $("#undoBtn").disabled = state.undo.length === 0;
    $("#redoBtn").disabled = state.redo.length === 0;
    $("#saveBtn").disabled = n === 0;
    $("#saveBtn").textContent = n ? `Save (${n})` : "Save";
    const chip = $("#gameState");
    chip.textContent = state.gameRunning ? "Game running" : "Game closed";
    chip.className = "chip " + (state.gameRunning ? "bad" : "ok");
    $("#pasteHereBtn").disabled = !state.clipboard;
  }

  function renderPlayers() {
    $("#players").replaceChildren(...state.current.players.map((p) => {
      const dirty = p.plates.some((_, i) => isChanged(p.number, i));
      return h("button", {
        class: p.number === state.player ? "active" : "",
        onclick: () => { state.player = p.number; state.preset = Math.min(state.preset, p.plates.length - 1); render(); },
      }, `P${p.number}`, h("span", { class: "lvl" }, p.level), dirty && h("span", { class: "dot", title: "Unsaved changes" }));
    }));
  }

  function miniPlate(p, pl) {
    const known = pl.gadgets.filter((id) => gadget(id));
    const costs = known.map((id) => gadget(id).slotCost);
    const rowsOf = GT.arrange(costs, p.rows);
    const rows = [0, 1].map((r) => {
      const cap = p.rows[r] ?? 0;
      const cells = [];
      let used = 0;
      if (rowsOf) known.forEach((id, i) => { if (rowsOf[i] === r) { cells.push(h("i", { class: `full m${costs[i]}` })); used += costs[i]; } });
      for (; used < cap; used++) cells.push(h("i", { class: "m1" }));
      for (let k = cap; k < 3; k++) cells.push(h("i", { class: "m1 lock" }));
      return h("span", { class: "r" }, cells);
    });
    const s = !p.superSlot ? "none" : pl.super ? "full" : "";
    return h("span", { class: "mini" }, h("span", { class: "rows" }, rows), h("span", { class: "s " + s }));
  }

  function renderPresets() {
    const p = player();
    $("#presets").replaceChildren(...p.plates.map((pl, i) => {
      const issues = GT.validate(pl, p, catalog);
      const used = pl.gadgets.reduce((a, id) => a + (gadget(id)?.slotCost ?? 0), 0);
      const el = h("button", {
        class: "preset" + (i === state.preset ? " active" : ""),
        draggable: "true",
        title: "Drag onto another preset to copy it there",
        onclick: () => { state.preset = i; render(); },
        ondragstart: (e) => { e.dataTransfer.setData("text/gt-preset", JSON.stringify({ player: p.number, index: i })); e.dataTransfer.effectAllowed = "copy"; },
        ondragover: (e) => { if (e.dataTransfer.types.includes("text/gt-preset")) { e.preventDefault(); el.classList.add("drop"); } },
        ondragleave: () => el.classList.remove("drop"),
        ondrop: (e) => {
          el.classList.remove("drop");
          const src = JSON.parse(e.dataTransfer.getData("text/gt-preset") || "null");
          if (!src || (src.player === p.number && src.index === i)) return;
          e.preventDefault();
          pasteOrAsk(player(src.player).plates[src.index], label(src.player, src.index), [{ player: p.number, index: i }]);
        },
      },
      h("span", { class: "letter" }, GT.presetLetter(i)),
      miniPlate(p, pl),
      h("span", { class: "meta" },
        pl.gadgets.length === 0 && !pl.super ? "Empty" : `${used}/${GT.slotCount(p)} slots${pl.super ? " · ★" : ""}`,
        issues.length > 0 && h("span", { class: "warn", title: issues.map((x) => GT.describe(x, catalog)).join("\n") }, "!"),
        isChanged(p.number, i) && h("span", { class: "dot", title: "Unsaved changes" })));
      return el;
    }));
  }

  /** A gadget on the plate: the 1-, 2- or 3-slot shape, never stretched. */
  function block(g, opts = {}) {
    return h("div", {
      class: `hex s${g.slotCost} block${opts.super ? " super" : ""}${opts.bad ? " bad" : ""}`,
      title: tip(g),
      draggable: "true",
      ondragstart: (e) => { e.dataTransfer.setData("text/gt-placed", g.id); e.dataTransfer.effectAllowed = "move"; },
    },
    h("div", { class: "inner" }, icon(g), h("span", { class: "name" }, g.name)),
    h("button", { type: "button", class: "x", title: "Remove", onclick: (e) => { e.stopPropagation(); removeGadget(g.id); } }, "×"));
  }

  const lockSvg = () => {
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("viewBox", "0 0 24 24"); svg.setAttribute("width", "18"); svg.setAttribute("height", "18");
    svg.innerHTML = '<path fill="currentColor" d="M7 10V7a5 5 0 0 1 10 0v3h1a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1h1Zm2 0h6V7a3 3 0 0 0-6 0v3Z"/>';
    return svg;
  };

  function renderEditor() {
    const p = player();
    const pl = plate();
    const issues = GT.validate(pl, p, catalog);
    const known = pl.gadgets.filter((id) => gadget(id));
    const unknown = pl.gadgets.filter((id) => !gadget(id));
    const costs = known.map((id) => gadget(id).slotCost);
    const used = costs.reduce((a, b) => a + b, 0);
    const rowsOf = GT.arrange(costs, p.rows);

    $("#presetTitle").textContent = `Player ${p.number} · Preset ${GT.presetLetter(state.preset)}`;
    $("#presetSub").textContent = `${p.level} plate · ${GT.slotCount(p)} slots${p.superSlot ? " + Super Slot" : ""} · ${used} used`;

    let gridContent;
    if (rowsOf) {
      gridContent = [0, 1].map((r) => {
        const cap = p.rows[r] ?? 0;
        const cells = [];
        let filled = 0;
        known.forEach((id, i) => {
          if (rowsOf[i] !== r) return;
          cells.push(block(gadget(id)));
          filled += costs[i];
        });
        for (; filled < cap; filled++) cells.push(h("div", { class: "hex s1 empty", title: "Empty slot" }, h("span", { class: "plus" }, "+")));
        for (let k = cap; k < 3; k++) cells.push(h("div", { class: "hex s1 locked", title: "Not available on this plate level" }, lockSvg()));
        return h("div", { class: "row" }, cells);
      });
    } else {
      gridContent = [h("div", { class: "overflow", title: "These gadgets do not fit on this plate" }, known.map((id) => block(gadget(id), { bad: true })))];
    }
    if (unknown.length) {
      gridContent.push(h("div", { class: "unknowns" }, unknown.map((id) =>
        h("span", { class: "unknown-chip" }, `Unknown: ${id} `, h("button", { type: "button", class: "x", onclick: () => removeGadget(id) }, "×")))));
    }

    const grid = h("div", { class: "grid" }, gridContent);
    const superSlot = renderSuperSlot(p, pl);
    $("#plate").replaceChildren(h("div", { class: "plate-inner" },
      h("div", { class: "plate-title" }, h("span", {}, `${p.level} plate`)),
      h("div", { class: "plate-body" }, grid, superSlot)));

    const items = [];
    if (issues.length === 0) items.push(h("li", { class: "ok" }, pl.gadgets.length || pl.super ? "This preset follows every rule." : "Empty preset."));
    else {
      if (!isChanged(p.number, state.preset)) items.push(h("li", { class: "note" }, "This preset was already like this in the save. It does not block saving, but the game may not accept it."));
      for (const x of issues) items.push(h("li", {}, GT.describe(x, catalog)));
    }
    $("#issues").replaceChildren(...items);
  }

  function renderSuperSlot(p, pl) {
    const wrap = h("div", { class: "superslot" });
    if (!p.superSlot) {
      wrap.append(h("div", { class: "hex s1 super locked", title: "The Super Slot comes with the Grand Master plate" }, h("span", { class: "label" }, "Super Slot", h("br"), "Grand Master")));
      wrap.append(h("div", { class: "super-caption", style: "color: var(--muted)" }, "Locked"));
      return wrap;
    }
    const g = pl.super && gadget(pl.super);
    const slot = g
      ? block(g, { super: true })
      : h("div", { class: "hex s1 super", title: "Drop a 1-slot gadget here: its effect is amplified" },
          h("span", { class: "label" }, pl.super ? ["Unknown:", h("br"), pl.super] : ["Drop a", h("br"), "1-slot gadget"]));
    slot.addEventListener("dragover", (e) => { if (hasGadgetDrag(e)) { e.preventDefault(); slot.classList.add("over"); } });
    slot.addEventListener("dragleave", () => slot.classList.remove("over"));
    slot.addEventListener("drop", (e) => {
      slot.classList.remove("over");
      const id = e.dataTransfer.getData("text/gt-gadget") || e.dataTransfer.getData("text/gt-placed");
      if (id) { e.preventDefault(); e.stopPropagation(); setSuper(id); }
    });
    wrap.append(slot, h("div", { class: "super-caption" }, "★ Super Slot"));
    return wrap;
  }

  const hasGadgetDrag = (e) => e.dataTransfer.types.includes("text/gt-gadget") || e.dataTransfer.types.includes("text/gt-placed");

  function renderLibrary() {
    const p = player();
    const pl = plate();
    const f = state.filter;
    const text = f.text.trim().toLowerCase();
    const shown = catalog.gadgets.filter((g) =>
      (!text || g.name.toLowerCase().includes(text) || (g.description || "").toLowerCase().includes(text)) &&
      (!f.category || g.category === f.category) &&
      (!f.cost || g.slotCost === f.cost) &&
      (f.showLocked || p.unlocked.has(g.id)));

    const list = [];
    let lastCat = null;
    for (const g of shown) {
      if (g.category !== lastCat) { list.push(h("div", { class: "lib-cat" }, g.category)); lastCat = g.category; }
      const locked = !p.unlocked.has(g.id);
      const inRows = pl.gadgets.includes(g.id);
      const inSuper = pl.super === g.id;
      const canSuper = p.superSlot && g.slotCost === 1 && !locked && !inSuper;
      list.push(h("button", {
        type: "button",
        class: "gadget" + (locked ? " locked" : "") + (inRows || inSuper ? " equipped" : ""),
        title: locked ? `${tip(g)}\n\nNot unlocked for player ${p.number}.` : tip(g),
        disabled: locked,
        draggable: locked ? null : "true",
        ondragstart: (e) => { e.dataTransfer.setData("text/gt-gadget", g.id); e.dataTransfer.effectAllowed = "copy"; },
        onclick: () => (inRows || inSuper ? removeGadget(g.id) : addGadget(g.id)),
      },
      icon(g),
      h("span", {}, h("div", { class: "name" }, g.name), h("div", { class: "desc" }, g.description || g.category)),
      h("span", { class: "side" },
        pips(g.slotCost),
        locked ? h("span", { class: "badge state" }, "Locked")
          : inSuper ? h("span", { class: "badge state" }, "★ Super")
          : inRows ? h("span", { class: "badge state" }, "Equipped")
          : canSuper
            ? h("span", { class: "badge super", title: "Put in the Super Slot", onclick: (e) => { e.stopPropagation(); setSuper(g.id); } }, "★ Super")
            : null)));
    }
    if (shown.length === 0) list.push(h("p", { class: "muted", style: "padding: 12px" }, "No gadget matches."));
    $("#libList").replaceChildren(...list);
    $("#libCount").textContent = `${p.unlocked.size}/${catalog.gadgets.length} unlocked`;
  }

  // ---- Editing -----------------------------------------------------------------------

  function tryPlate(next, okMessage) {
    const p = player();
    const before = GT.validate(plate(), p, catalog).length;
    const after = GT.validate(next, p, catalog);
    if (after.length > before) {
      toast(GT.describe(after[after.length - 1], catalog), "bad");
      return false;
    }
    setPlate(next, okMessage);
    return true;
  }

  function addGadget(id) {
    const pl = plate();
    if (pl.gadgets.includes(id)) return;
    const g = gadget(id);
    const next = { gadgets: [...pl.gadgets, id], super: pl.super === id ? null : pl.super };
    // A full plate with a free Super Slot: a 1-slot gadget goes there instead.
    const p = player();
    const fits = GT.validate(next, p, catalog).length <= GT.validate(pl, p, catalog).length;
    if (!fits && p.superSlot && !pl.super && g?.slotCost === 1) { setSuper(id); return; }
    tryPlate(next);
  }

  function setSuper(id) {
    const pl = plate();
    const g = gadget(id);
    if (!player().superSlot) { toast("This plate has no Super Slot (Grand Master only).", "bad"); return; }
    if (g && g.slotCost > 1) { toast(`${g.name} takes ${g.slotCost} slots; the Super Slot takes 1-slot gadgets only.`, "bad"); return; }
    tryPlate({ gadgets: pl.gadgets.filter((x) => x !== id), super: id });
  }

  function removeGadget(id) {
    const pl = plate();
    setPlate({ gadgets: pl.gadgets.filter((x) => x !== id), super: pl.super === id ? null : pl.super });
  }

  function undo() {
    if (!state.undo.length) return;
    state.redo.push(state.current);
    state.current = state.undo.pop();
    render();
  }
  function redo() {
    if (!state.redo.length) return;
    state.undo.push(state.current);
    state.current = state.redo.pop();
    render();
  }

  function copyPreset() {
    state.clipboard = { plate: GT.clonePlate(plate()), label: label(state.player, state.preset) };
    renderTop();
    toast(`Copied ${state.clipboard.label}.`);
  }

  /** Strict paste; when a target refuses, open the paste dialog to show why. */
  function pasteOrAsk(pl, what, targets) {
    const result = GT.paste(state.current, pl, targets, catalog, "strict");
    if (result.applied) {
      commit(result.save, `Pasted ${what} on ${targets.map((t) => label(t.player, t.index)).join(", ")}.`);
    } else {
      openPasteDialog(pl, what, targets);
    }
  }

  function save() {
    if (state.gameRunning) {
      toast("Close the game before saving: it would overwrite the changes, and Steam Cloud could sync a half-written save.", "bad");
      return;
    }
    const blocking = changedPlates().filter((t) => GT.validate(player(t.player).plates[t.index], player(t.player), catalog).length);
    if (blocking.length) {
      toast(`Not saved: ${blocking.map((t) => label(t.player, t.index)).join(", ")} break${blocking.length === 1 ? "s" : ""} a rule. Fix or undo first.`, "bad");
      return;
    }
    const n = changedPlates().length;
    state.saved = state.current;
    const d = new Date();
    const two = (n) => String(n).padStart(2, "0");
    const stamp = `${d.getFullYear()}${two(d.getMonth() + 1)}${two(d.getDate())}-${two(d.getHours())}${two(d.getMinutes())}${two(d.getSeconds())}`;
    render();
    toast(`Saved ${n} preset${n > 1 ? "s" : ""} (mockup: nothing written). Backup: save.sav.${stamp}.bak`);
  }

  // ---- Paste dialog ------------------------------------------------------------------

  const paste = { plate: null, what: "", selected: new Set() };

  function openPasteDialog(pl, what, preselect = []) {
    paste.plate = GT.clonePlate(pl);
    paste.what = what;
    paste.selected = new Set(preselect.map((t) => t.player + ":" + t.index));
    $("#pasteDrop").checked = false;
    $("#pasteDialog").returnValue = "";
    $("#pasteWhat").textContent = what;
    $("#pastePreview").replaceChildren(...plateChips(paste.plate));
    renderPasteGrid();
    $("#pasteDialog").showModal();
  }

  function plateChips(pl) {
    const chips = pl.gadgets.map((id) => {
      const g = gadget(id);
      return g ? h("span", { class: "chip-g", title: tip(g) }, icon(g, true), g.name, " ", pips(g.slotCost)) : h("span", { class: "chip-g" }, `Unknown: ${id}`);
    });
    if (pl.super) {
      const g = gadget(pl.super);
      chips.push(h("span", { class: "chip-g super", title: "Super Slot" }, g ? icon(g, true) : null, "★ ", g ? g.name : pl.super));
    }
    return chips.length ? chips : [h("span", { class: "muted" }, "An empty preset.")];
  }

  /** [reason, outcomes] pairs, by the first rule each target breaks. */
  function groupByReason(outcomes) {
    const groups = new Map();
    for (const o of outcomes) {
      const reason = GT.describe(o.issues[0], catalog);
      if (!groups.has(reason)) groups.set(reason, []);
      groups.get(reason).push(o);
    }
    return [...groups];
  }

  /** "P2 all, P4 A C" rather than ten labels per player. */
  function compactTargets(targets) {
    const byPlayer = new Map();
    for (const t of targets) {
      if (!byPlayer.has(t.player)) byPlayer.set(t.player, []);
      byPlayer.get(t.player).push(t.index);
    }
    return [...byPlayer].map(([n, idx]) =>
      idx.length === player(n).plates.length ? `P${n} all` : `P${n} ${idx.sort((a, b) => a - b).map(GT.presetLetter).join(" ")}`).join(", ");
  }

  function groupDropped(outcomes) {
    const byName = new Map();
    for (const o of outcomes) for (const id of o.dropped) {
      const name = gadget(id)?.name ?? id;
      if (!byName.has(name)) byName.set(name, []);
      byName.get(name).push(o.target);
    }
    return [...byName].map(([name, ts]) => `${name} on ${compactTargets(ts)}`).join("; ");
  }

  function pasteTargets() {
    return [...paste.selected].map((k) => { const [p, i] = k.split(":").map(Number); return { player: p, index: i }; });
  }

  function renderPasteGrid() {
    const mode = $("#pasteDrop").checked ? "dropMissing" : "strict";
    const targets = pasteTargets();
    const result = GT.paste(state.current, paste.plate, targets, catalog, mode);
    const byKey = new Map(result.targets.map((o) => [o.target.player + ":" + o.target.index, o]));

    const maxPresets = Math.max(...state.current.players.map((p) => p.plates.length));
    const cells = [h("span", {}), ...Array.from({ length: maxPresets }, (_, i) => h("span", { class: "head" }, GT.presetLetter(i))), h("span", {})];
    for (const p of state.current.players) {
      cells.push(h("span", { class: "who" }, `P${p.number}`, h("span", { class: "muted" }, ` · ${p.level}`)));
      for (let i = 0; i < maxPresets; i++) {
        if (i >= p.plates.length) { cells.push(h("span", {})); continue; }
        const key = p.number + ":" + i;
        const o = byKey.get(key);
        const sel = paste.selected.has(key);
        const cls = ["", sel ? "sel" : "", o ? (o.issues.length ? "bad" : "ok") : "", p.number === state.player && i === state.preset ? "here" : ""].join(" ");
        const why = o ? (o.issues.length ? o.issues.map((x) => GT.describe(x, catalog)).join("\n") : o.dropped.length ? "Leaves out: " + o.dropped.map((id) => gadget(id)?.name ?? id).join(", ") : "OK") : "";
        cells.push(h("label", { class: cls, title: `${label(p.number, i)}${why ? "\n" + why : ""}` },
          h("input", { type: "checkbox", checked: sel, onchange: () => { sel ? paste.selected.delete(key) : paste.selected.add(key); renderPasteGrid(); } }),
          GT.presetLetter(i)));
      }
      const all = p.plates.every((_, i) => paste.selected.has(p.number + ":" + i));
      cells.push(h("button", { type: "button", onclick: () => { p.plates.forEach((_, i) => all ? paste.selected.delete(p.number + ":" + i) : paste.selected.add(p.number + ":" + i)); renderPasteGrid(); } }, all ? "None" : "All"));
    }
    const grid = $("#pasteGrid");
    grid.style.gridTemplateColumns = `auto repeat(${maxPresets}, 34px) auto`;
    grid.replaceChildren(...cells);

    const summary = $("#pasteSummary");
    const bad = result.targets.filter((o) => o.issues.length);
    const dropped = result.targets.filter((o) => o.dropped.length && !o.issues.length);
    if (!targets.length) summary.replaceChildren("Pick the presets to paste on. Green: accepted. Red: refused (hover for why).");
    else summary.replaceChildren(
      `${targets.length} preset${targets.length > 1 ? "s" : ""} selected. `,
      bad.length
        ? h("span", { class: "bad" }, `${bad.length} refuse${bad.length > 1 ? "" : "s"}. `,
            groupByReason(bad).map(([reason, outs]) => h("div", {}, `${reason} (${compactTargets(outs.map((o) => o.target))})`)),
            mode === "strict" ? h("div", {}, "Tick “Leave out…” to paste what each player can use.") : null)
        : "Every one accepts it.",
      dropped.length ? h("div", {}, `Left out: ${groupDropped(dropped)}.`) : "");
    $("#pasteGo").disabled = !targets.length || !result.applied;
    paste.result = result;
  }

  // ---- Share and import --------------------------------------------------------------

  function openShareDialog() {
    $("#shareWhat").textContent = label(state.player, state.preset);
    $("#shareCode").value = GT.encodeShareCode(plate());
    $("#shareDialog").showModal();
  }

  function download(name, text) {
    const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    const a = h("a", { href: url, download: name });
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  function exportSetups(scope) {
    const entries = [];
    const players = scope === "all" ? state.current.players : [player()];
    for (const p of players) p.plates.forEach((pl, i) => {
      if (scope === "preset" && i !== state.preset) return;
      entries.push({ label: label(p.number, i), plate: pl });
    });
    const name = scope === "all" ? "gadgettool-all.json" : scope === "player" ? `gadgettool-p${state.player}.json` : `gadgettool-${label(state.player, state.preset).replace(" ", "")}.json`;
    download(name, GT.exportJson(entries));
    toast(`Exported ${entries.length} preset${entries.length > 1 ? "s" : ""} to ${name}.`);
  }

  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
  }

  function readImport(text) {
    $("#importError").textContent = "";
    $("#importList").replaceChildren();
    const trimmed = text.trim();
    if (!trimmed) return;
    let entries;
    try {
      entries = /^gt\d-/i.test(trimmed)
        ? [{ label: "Share code", plate: GT.decodeShareCode(trimmed, catalog) }]
        : GT.importJson(trimmed);
    } catch (e) {
      $("#importError").textContent = e.message;
      return;
    }
    $("#importList").replaceChildren(...entries.map((e, n) => h("div", { class: "import-entry" },
      h("div", { class: "what" },
        h("div", {}, e.label || `Preset ${n + 1}`),
        h("div", { class: "names" }, [...e.plate.gadgets, ...(e.plate.super ? ["★ " + e.plate.super] : [])].map((id) => id.startsWith("★ ") ? "★ " + (gadget(id.slice(2))?.name ?? id.slice(2)) : gadget(id)?.name ?? `Unknown (${id})`).join(" · ") || "Empty")),
      h("button", { type: "button", onclick: () => { $("#importDialog").close(); pasteOrAsk(e.plate, e.label || "imported preset", [{ player: state.player, index: state.preset }]); } }, `Put on ${label(state.player, state.preset)}`),
      h("button", { type: "button", onclick: () => { $("#importDialog").close(); openPasteDialog(e.plate, e.label || "imported preset"); } }, "Paste to…"))));
  }

  // ---- Wiring ------------------------------------------------------------------------

  function wire() {
    const cat = $("#category");
    cat.replaceChildren(h("option", { value: "" }, "All categories"), ...catalog.categories.map((c) => h("option", { value: c }, c)));
    cat.onchange = () => { state.filter.category = cat.value; renderLibrary(); };
    $("#search").oninput = (e) => { state.filter.text = e.target.value; renderLibrary(); };
    $("#showLocked").onchange = (e) => { state.filter.showLocked = e.target.checked; renderLibrary(); };
    $("#costFilter").onclick = (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      state.filter.cost = Number(b.dataset.cost);
      for (const x of $("#costFilter").children) x.classList.toggle("on", x === b);
      renderLibrary();
    };

    $("#undoBtn").onclick = undo;
    $("#redoBtn").onclick = redo;
    $("#saveBtn").onclick = save;
    $("#gameState").onclick = () => { state.gameRunning = !state.gameRunning; renderTop(); };
    $("#copyBtn").onclick = copyPreset;
    $("#pasteHereBtn").onclick = () => state.clipboard && pasteOrAsk(state.clipboard.plate, state.clipboard.label, [{ player: state.player, index: state.preset }]);
    $("#pasteToBtn").onclick = () => openPasteDialog(plate(), label(state.player, state.preset));
    $("#shareBtn").onclick = openShareDialog;
    $("#clearBtn").onclick = () => setPlate({ gadgets: [], super: null }, `Cleared ${label(state.player, state.preset)}.`);
    $("#importBtn").onclick = () => { $("#importError").textContent = ""; $("#importList").replaceChildren(); $("#importDialog").showModal(); $("#importText").focus(); };

    $("#pasteDrop").onchange = renderPasteGrid;
    $("#pasteDialog").addEventListener("close", () => {
      if ($("#pasteDialog").returnValue !== "paste" || !paste.result?.applied) return;
      const n = paste.result.targets.length;
      commit(paste.result.save, `Pasted ${paste.what} on ${n} preset${n > 1 ? "s" : ""}.`);
    });

    $("#copyCodeBtn").onclick = async () => toast(await copyText($("#shareCode").value) ? "Code copied." : "Select the code and copy it.", "ok");
    $("#shareDialog").addEventListener("click", (e) => { const b = e.target.closest("[data-export]"); if (b) exportSetups(b.dataset.export); });

    $("#importReadBtn").onclick = () => readImport($("#importText").value);
    $("#importFileBtn").onclick = () => $("#fileInput").click();
    $("#fileInput").onchange = async (e) => {
      const file = e.target.files[0];
      e.target.value = "";
      if (!file) return;
      const text = await file.text();
      $("#importText").value = text;
      readImport(text);
    };

    // Dropping a placed gadget on the list removes it; dropping a gadget on the plate adds it.
    const lib = $("#library");
    lib.addEventListener("dragover", (e) => { if (e.dataTransfer.types.includes("text/gt-placed")) { e.preventDefault(); lib.classList.add("over"); } });
    lib.addEventListener("dragleave", (e) => { if (!lib.contains(e.relatedTarget)) lib.classList.remove("over"); });
    lib.addEventListener("drop", (e) => { lib.classList.remove("over"); const id = e.dataTransfer.getData("text/gt-placed"); if (id) { e.preventDefault(); removeGadget(id); } });

    const plateEl = $("#plate");
    plateEl.addEventListener("dragover", (e) => { if (e.dataTransfer.types.includes("text/gt-gadget") && !e.target.closest(".superslot")) { e.preventDefault(); plateEl.classList.add("over"); } });
    plateEl.addEventListener("dragleave", (e) => { if (!plateEl.contains(e.relatedTarget)) plateEl.classList.remove("over"); });
    plateEl.addEventListener("drop", (e) => {
      plateEl.classList.remove("over");
      if (e.target.closest(".superslot")) return;
      const id = e.dataTransfer.getData("text/gt-gadget");
      if (id) { e.preventDefault(); addGadget(id); }
    });
    // A gadget dragged from the Super Slot back onto the rows moves there.
    plateEl.addEventListener("dragover", (e) => { if (e.dataTransfer.types.includes("text/gt-placed") && e.target.closest(".grid")) e.preventDefault(); });
    plateEl.addEventListener("drop", (e) => {
      const id = e.dataTransfer.getData("text/gt-placed");
      if (!id || !e.target.closest(".grid") || plate().super !== id) return;
      e.preventDefault();
      tryPlate({ gadgets: [...plate().gadgets, id], super: null });
    });

    document.addEventListener("keydown", (e) => {
      const typing = e.target.closest("input, textarea, select") || document.querySelector("dialog[open]");
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key.toLowerCase() === "s") { e.preventDefault(); if (!typing) save(); return; }
      if (typing) return;
      if (mod && e.key.toLowerCase() === "z" && !e.shiftKey) { e.preventDefault(); undo(); }
      else if (mod && (e.key.toLowerCase() === "y" || (e.key.toLowerCase() === "z" && e.shiftKey))) { e.preventDefault(); redo(); }
      else if (mod && e.key.toLowerCase() === "c" && !String(window.getSelection())) { copyPreset(); }
      else if (mod && e.key.toLowerCase() === "v" && state.clipboard) { e.preventDefault(); $("#pasteHereBtn").click(); }
      else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const n = player().plates.length;
        state.preset = (state.preset + (e.key === "ArrowDown" ? 1 : n - 1)) % n;
        render();
      }
    });
  }

  wire();
  render();
})();
