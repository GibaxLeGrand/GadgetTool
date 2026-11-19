// GadgetTool editor UI. Rules come from window.GT (rules.js, the Core's rules in JS), data
// and files from window.GTHost (host.js: the app through WebView2, or a browser stand-in).
// Icons come from the data pack (the app serves them); without one, a placeholder: initials
// on the category's colour.
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

  const host = window.GTHost;
  let catalog = GT.makeCatalog({ source: "", gadgets: [] });

  const state = {
    fileName: null,
    saved: null, // as in the file
    current: null, // with the edits not saved yet
    undo: [],
    redo: [],
    player: 1,
    preset: 0,
    clipboard: null, // { plate, label, name }
    gameRunning: false,
    names: new Map(), // "player:index" -> name; GadgetTool only, not part of undo
    gameUpdate: null, // what a game update changed for this save's presets, until "Got it"
    view: loadPref("gadgettool.view", ["list", "game"]),
    // Colours, separate from the view: "auto" gives each view its own (dark lists, game-coloured game style).
    theme: loadPref("gadgettool.theme", ["auto", "dark", "game"]),
    filter: { text: "", category: "", cost: 0, showLocked: true },
  };

  const player = (n = state.player) => state.current.players.find((p) => p.number === n);
  const savedPlayer = (n) => state.saved.players.find((p) => p.number === n);
  const plate = () => player().plates[state.preset];
  const label = (n, i) => `P${n} ${GT.presetLetter(i)}`;
  const presetName = (n, i) => state.names.get(`${n}:${i}`) || null;
  /** "P1 A" or "P1 A “Time trial”". */
  const fullLabel = (n, i) => presetName(n, i) ? `${label(n, i)} “${presetName(n, i)}”` : label(n, i);
  const gadget = (id) => catalog.byId.get(id);

  /** A remembered choice, or the first allowed value. Browser storage is a comfort only. */
  function loadPref(key, allowed) {
    try {
      const value = localStorage.getItem(key);
      return allowed.includes(value) ? value : allowed[0];
    } catch {
      return allowed[0];
    }
  }
  function savePref(key, value) {
    try { localStorage.setItem(key, value); } catch { /* per-viewer comfort only */ }
  }

  function applyTheme() {
    const colours = state.theme === "auto" ? (state.view === "game" ? "game" : "dark") : state.theme;
    document.body.classList.toggle("theme-game", colours === "game");
  }

  function changedPlates() {
    const out = [];
    if (!state.current) return out;
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
  const icon = (g, small) => g.icon
    ? h("span", { class: "icon img" + (small ? " small" : ""), "aria-hidden": "true" }, h("img", { src: g.icon, alt: "", loading: "lazy", draggable: "false" }))
    : h("span", { class: "icon" + (small ? " small" : ""), style: `--h:${hue(categoryKey(g))}`, "aria-hidden": "true" }, initials(g.name));
  /**
   * The game's base for a gadget's icon (from the data pack), or null without one. In the
   * gadget list every gadget is drawn on a 1-slot base, as in the game's menu; on the plate,
   * on its real size (where: "list", "plate" or "super").
   */
  function baseUrl(g, where) {
    if (!catalog.packUrl || !g.icon) return null;
    const cost = where === "plate" ? Math.min(Math.max(g.slotCost, 1), 3) : 1;
    const name = where === "super" ? "super"
      : g.base === "mini" ? "mini"
      : g.base === "ex" ? `ex-kit-${cost === 1 ? 1 : 3}`
      : g.base === "kit" && (cost === 1 || cost === 3) ? `kit-${cost}`
      : String(cost);
    return `${catalog.packUrl}/ui/base-${name}.png`;
  }
  /**
   * As in the game: the gadget's icon drawn on its base. A 2- or 3-slot base on the plate is
   * stretched to its cells' width by its middle, its pointed ends kept as they are.
   */
  function art(g, where) {
    // Absolute: a relative url() in a CSS variable would be read against the stylesheet.
    const url = new URL(baseUrl(g, where), document.baseURI).href;
    const long = where === "plate" && g.slotCost > 1;
    return h("span", { class: "art" + (long ? " long" : ""), "aria-hidden": "true", style: long ? `--base:url("${url}")` : null },
      long ? null : h("img", { class: "art-base", src: url, alt: "", draggable: "false" }),
      h("img", { class: "art-icon", src: g.icon, alt: "", draggable: "false" }));
  }

  /** The category's key: the same in every language, so colours and groups do not move. */
  const categoryKey = (g) => g.categoryId || g.category || "Other";
  const pips = (cost) => h("span", { class: "pips", title: `${cost} slot${cost > 1 ? "s" : ""}` },
    [1, 2, 3].map((i) => h("i", { class: i <= cost ? "on" : "" })));
  const tip = (g) => `${g.name} · ${g.slotCost} slot${g.slotCost > 1 ? "s" : ""} · ${g.category}\n${plainText(g.description)}`
    + (g.superEffect ? `\n\n★ Super Slot: ${plainText(g.superEffect)}` : "");

  // The game's texts hold rich-text tags (<SpStyle>...</>) and placeholders the game fills in
  // with a value ({GetRing}); GadgetTool does not know those values, so it shows "?".
  const TEXT_TOKENS = /(<[A-Za-z]+>|<\/>|\{[A-Za-z0-9_]+\}|\n)/;
  const plainText = (t) => (t || "").replace(/<[A-Za-z]+>|<\/>/g, "").replace(/\{[A-Za-z0-9_]+\}/g, "?");
  function richText(t) {
    const out = [];
    let strong = false;
    for (const part of (t || "").split(TEXT_TOKENS)) {
      if (!part) continue;
      if (/^<[A-Za-z]+>$/.test(part)) strong = true;
      else if (part === "</>") strong = false;
      else if (part === "\n") out.push(h("br"));
      else if (/^\{[A-Za-z0-9_]+\}$/.test(part)) out.push(h("span", { class: "ph", title: "A value the game fills in" }, "?"));
      else out.push(strong ? h("strong", {}, part) : part);
    }
    return out;
  }

  function toast(text, kind = "ok") {
    const el = h("div", { class: "toast " + kind }, text);
    $("#toasts").append(el);
    setTimeout(() => el.remove(), kind === "bad" ? 6000 : 3200);
  }

  // ---- Render ------------------------------------------------------------------------

  function render() {
    const open = !!state.current;
    const game = state.view === "game";
    $("#welcome").hidden = open;
    $("#players").hidden = !open;
    $("#editorLayout").hidden = !open || game;
    $("#gameLayout").hidden = !open || !game;
    applyTheme();
    placeSharedParts();
    renderTop();
    renderUpdateBanner();
    if (!open) return;
    renderPlayers();
    renderEditor();
    if (game) {
      renderGameTabs();
      renderGameGadgets();
      showInfo(null);
    } else {
      renderPresets();
      renderLibrary();
    }
  }

  /** The editor's head, plate and issues belong to whichever view is shown. */
  function placeSharedParts() {
    const head = $("#editorHead"), plateEl = $("#plate"), issues = $("#issues");
    if (state.view === "game") {
      if (head.parentElement.id !== "gameHeadSlot") {
        $("#gameHeadSlot").append(head);
        $("#gamePlateSlot").append(plateEl);
        $("#gameIssuesSlot").append(issues);
      }
    } else if (head.parentElement.id !== "listEditor") {
      $("#listEditor").prepend(head, plateEl, issues);
    }
  }

  const sourceText = () => catalog.gameVersion ? `${catalog.source} · game ${GT.versionText(catalog.gameVersion)}` : catalog.source;

  // ---- Game update since this save was last opened -----------------------------------

  /** Changes of the last game update that touch this preset (empty when none). */
  function updateChanges(n, i) {
    const u = state.gameUpdate;
    if (!u || !u.presets.includes(`${n}:${i}`)) return [];
    const pl = player(n).plates[i];
    const ids = new Set(pl.super ? [...pl.gadgets, pl.super] : pl.gadgets);
    return u.changes.filter((c) => ids.has(c.id));
  }

  function renderUpdateBanner() {
    const u = state.gameUpdate;
    const banner = $("#updateBanner");
    banner.hidden = !u || !state.current;
    if (banner.hidden) return;
    const broken = u.presets
      .map((k) => k.split(":").map(Number))
      .filter(([n, i]) => GT.validate(player(n).plates[i], player(n), catalog).length > 0);
    banner.replaceChildren(
      h("div", { class: "u-text" },
        h("b", {}, `The game changed since GadgetTool last opened this save (${u.from} → ${u.to}).`),
        h("div", {}, "Gadgets your presets use: ", u.changes.map((c) => GT.describeChange(c)).join(" ")),
        broken.length
          ? h("div", { class: "u-urgent" }, `${broken.length} preset${broken.length > 1 ? "s" : ""} no longer fit${broken.length > 1 ? "" : "s"}: `,
              broken.map(([n, i]) => fullLabel(n, i)).join(", "),
              ". They are left as they are and marked in red: fix them before playing.")
          : h("div", {}, "Every preset still fits.")),
      h("button", { type: "button", onclick: acknowledgeUpdate }, "Got it"));
  }

  async function acknowledgeUpdate() {
    try { await host.acknowledgeGameUpdate(); } catch (e) { toast(e.message, "bad"); return; }
    state.gameUpdate = null;
    render();
  }

  function renderTop() {
    const open = !!state.current;
    const n = changedPlates().length;
    $("#saveInfo").textContent = !open
      ? sourceText()
      : `${state.fileName} · ${sourceText()}` + (n ? ` · ${n} unsaved preset${n > 1 ? "s" : ""}` : " · no unsaved change");
    $("#undoBtn").disabled = state.undo.length === 0;
    $("#redoBtn").disabled = state.redo.length === 0;
    for (const b of $("#viewSwitch").children) b.classList.toggle("on", b.dataset.view === state.view);
    $("#viewSwitch").hidden = !open;
    $("#themeSelect").value = state.theme;
    $("#saveBtn").disabled = n === 0;
    $("#saveBtn").textContent = n ? `Save (${n})` : "Save";
    $(".brand .tag").textContent = host.kind === "app" ? "early version" : "browser preview";
    const chip = $("#gameState");
    chip.textContent = state.gameRunning ? "Game running" : "Game closed";
    chip.className = "chip " + (state.gameRunning ? "bad" : "ok");
    chip.title = host.kind === "app"
      ? "GadgetTool refuses to save while the game runs"
      : "Browser preview: click to pretend the game is running";
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
      const name = presetName(p.number, i);
      const urgent = issues.length > 0 && updateChanges(p.number, i).length > 0;
      const el = h("button", {
        class: "preset" + (i === state.preset ? " active" : "") + (urgent ? " urgent" : ""),
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
      h("span", { class: "pname" + (name ? "" : " none") }, name || "No name"),
      h("span", { class: "meta" },
        pl.gadgets.length === 0 && !pl.super ? "Empty" : `${GT.used(pl, catalog)}/${GT.capacity(p)} slots`,
        issues.length > 0 && h("span", { class: "warn", title: issues.map((x) => GT.describe(x, catalog)).join("\n") }, "!"),
        isChanged(p.number, i) && h("span", { class: "dot", title: "Unsaved changes" })));
      return el;
    }));
  }

  /** A gadget on the plate: the 1-, 2- or 3-slot shape, never stretched. */
  function block(g, opts = {}) {
    const where = opts.super ? "super" : "plate";
    const onBase = !!baseUrl(g, where);
    return h("div", {
      class: `hex s${g.slotCost} block${opts.super ? " super" : ""}${opts.bad ? " bad" : ""}${onBase ? " art-block" : ""}`,
      title: tip(g),
      draggable: "true",
      ondragstart: (e) => { e.dataTransfer.setData("text/gt-placed", g.id); e.dataTransfer.effectAllowed = "move"; },
      onmouseenter: () => showInfo(g),
      onmouseleave: () => showInfo(null),
    },
    // As in the game: the gadget's art fills its cell, no name (it is in the tooltip and the
    // info box). Placeholders keep the name: initials alone say too little.
    onBase ? art(g, where)
      : g.icon ? h("div", { class: "inner art" }, icon(g))
      : h("div", { class: "inner" }, icon(g), h("span", { class: "name" }, g.name)),
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
    const rowsOf = GT.arrange(costs, p.rows);

    $("#presetTitle").textContent = `Player ${p.number} · Preset ${GT.presetLetter(state.preset)}`;
    const nameInput = $("#presetName");
    if (document.activeElement !== nameInput) nameInput.value = presetName(p.number, state.preset) || "";
    $("#presetSub").textContent = `${p.level} plate · ${GT.used(pl, catalog)}/${GT.capacity(p)} slots used` +
      (p.superSlot ? ` (${GT.slotCount(p)} + Super Slot)` : "");

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
      gridContent.push(h("div", { class: "unknowns" }, unknown.map((id) => {
        const oldName = state.gameUpdate?.changes.find((c) => c.id === id)?.name;
        return h("span", { class: "unknown-chip", title: id }, oldName ? `${oldName} (no longer in the game) ` : `Unknown: ${id} `,
          h("button", { type: "button", class: "x", title: "Remove", onclick: () => removeGadget(id) }, "×"));
      })));
    }

    const grid = h("div", { class: "grid" }, gridContent);
    const superSlot = renderSuperSlot(p, pl);
    $("#plate").replaceChildren(h("div", { class: "plate-inner" },
      h("div", { class: "plate-title" }, h("span", {}, `${p.level} plate`)),
      h("div", { class: "plate-body" }, grid, superSlot)));

    const items = [];
    if (issues.length === 0) items.push(h("li", { class: "ok" }, pl.gadgets.length || pl.super ? "This preset follows every rule." : "Empty preset."));
    else {
      const fromUpdate = updateChanges(p.number, state.preset);
      if (fromUpdate.length && !isChanged(p.number, state.preset)) {
        items.push(h("li", { class: "urgent" },
          `The game update ${state.gameUpdate.from} → ${state.gameUpdate.to} broke this preset: `,
          fromUpdate.map((c) => GT.describeChange(c)).join(" "),
          " It is left as it is; fix it before playing."));
      } else if (!isChanged(p.number, state.preset)) {
        items.push(h("li", { class: "note" }, "This preset was already like this in the save. It does not block saving, but the game may not accept it."));
      }
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
      (!text || g.name.toLowerCase().includes(text) || plainText(g.description).toLowerCase().includes(text)) &&
      (!f.category || (g.category || "Other") === f.category) &&
      (!f.cost || g.slotCost === f.cost) &&
      (f.showLocked || p.unlocked.has(g.id)));

    const list = [];
    let lastCat = null;
    for (const g of shown) {
      const category = g.category || "Other";
      if (category !== lastCat) { list.push(h("div", { class: "lib-cat" }, category)); lastCat = category; }
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
      h("span", {}, h("div", { class: "name" }, g.name), h("div", { class: "desc" }, g.description ? richText(g.description) : g.category)),
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

  // ---- Game-style view ---------------------------------------------------------------

  function renderGameTabs() {
    const p = player();
    const n = p.plates.length;
    const go = (d) => { state.preset = (state.preset + d + n) % n; render(); };
    $("#gameTabs").replaceChildren(
      h("button", { type: "button", class: "g-arrow", title: "Previous preset (Page Up)", onclick: () => go(-1) }, "◀"),
      ...p.plates.map((pl, i) => {
        const broken = GT.validate(pl, p, catalog).length > 0;
        const urgent = broken && updateChanges(p.number, i).length > 0;
        return h("button", {
          type: "button",
          class: "g-tab" + (i === state.preset ? " on" : "") + (urgent ? " urgent" : ""),
          title: `${fullLabel(p.number, i)} · ${pl.gadgets.length || pl.super ? `${GT.used(pl, catalog)}/${GT.capacity(p)} slots` : "Empty"}`,
          onclick: () => { state.preset = i; render(); },
        }, GT.presetLetter(i),
        broken ? h("span", { class: "g-warn" }, "!") : isChanged(p.number, i) ? h("span", { class: "g-dot" }) : null);
      }),
      h("button", { type: "button", class: "g-arrow", title: "Next preset (Page Down)", onclick: () => go(1) }, "▶"));
  }

  function renderGameGadgets() {
    const p = player();
    const pl = plate();
    const text = state.filter.text.trim().toLowerCase();
    const groups = new Map();
    for (const g of catalog.gadgets) {
      if (text && !g.name.toLowerCase().includes(text) && !plainText(g.description).toLowerCase().includes(text)) continue;
      const key = categoryKey(g);
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(g);
    }
    const rows = [...groups].map(([key, gadgets]) => h("div", {
      class: "g-cat",
      // The game's category colour when the data pack has it (used by the game colours).
      style: `--h:${hue(key)}` + (/^#[0-9A-Fa-f]{6}$/.test(gadgets[0].categoryColor || "") ? `;--cat:${gadgets[0].categoryColor}` : ""),
    },
      h("div", { class: "g-cat-name" }, gadgets[0].category || "Other"),
      h("div", { class: "g-row" }, gadgets.map((g) => gameTile(g, p, pl)))));
    $("#gameGadgets").replaceChildren(...(rows.length ? rows : [h("p", { class: "g-none" }, "No gadget matches.")]));
  }

  function gameTile(g, p, pl) {
    const locked = !p.unlocked.has(g.id);
    const inSuper = pl.super === g.id;
    const equipped = inSuper || pl.gadgets.includes(g.id);
    const onBase = !!baseUrl(g, "list");
    return h("button", {
      type: "button",
      class: "g-tile" + (locked ? " locked" : "") + (equipped ? " equipped" : "") + (onBase ? " art-tile" : ""),
      title: locked ? `${tip(g)}\n\nNot unlocked for player ${p.number}.` : tip(g),
      "aria-disabled": locked ? "true" : null,
      draggable: locked ? null : "true",
      ondragstart: (e) => { e.dataTransfer.setData("text/gt-gadget", g.id); e.dataTransfer.effectAllowed = "copy"; },
      onmouseenter: () => showInfo(g),
      onmouseleave: () => showInfo(null),
      onclick: () => {
        if (locked) toast(`${g.name} is not unlocked for player ${p.number}.`, "bad");
        else if (equipped) removeGadget(g.id);
        else addGadget(g.id);
      },
      oncontextmenu: (e) => {
        e.preventDefault();
        if (!locked && !inSuper) setSuper(g.id);
      },
    },
    h("span", { class: "g-cost", title: `${g.slotCost} slot${g.slotCost > 1 ? "s" : ""}` }, g.slotCost),
    equipped ? h("span", { class: "g-check", title: inSuper ? "In the Super Slot" : "Equipped" }, inSuper ? "★" : "✓") : null,
    onBase ? art(g, "list") : h("span", { class: "hex s1 g-hex" + (g.icon ? " art" : "") }, icon(g)),
    g.icon ? null : h("span", { class: "g-name" }, g.name));
  }

  /** The description box under the plate (game-style view): the gadget pointed at, or the preset. */
  function showInfo(g) {
    if (state.view !== "game" || !state.current) return;
    const p = player();
    const pl = plate();
    $("#gameInfo").replaceChildren(...(g
      ? [icon(g), h("div", {},
          h("div", { class: "g-info-name" }, `${g.name} · ${g.slotCost} slot${g.slotCost > 1 ? "s" : ""}`),
          h("div", { class: "g-info-text" }, g.description ? richText(g.description) : g.category),
          g.superEffect ? h("div", { class: "g-info-super" }, "★ Super Slot: ", richText(g.superEffect)) : null)]
      : [h("div", {},
          h("div", { class: "g-info-name" }, `${fullLabel(p.number, state.preset)} · ${GT.used(pl, catalog)}/${GT.capacity(p)} slots used`),
          h("div", { class: "g-info-text" }, "Click a gadget to add or remove it. Right-click a 1-slot gadget to put it in the Super Slot."))]));
  }

  // ---- Names (GadgetTool only) -------------------------------------------------------

  function setName(n, i, name) {
    if (name) state.names.set(`${n}:${i}`, name);
    else state.names.delete(`${n}:${i}`);
    render();
  }

  async function nameCurrentPreset(value) {
    const n = state.player, i = state.preset;
    if ((value || "").trim() === (presetName(n, i) || "")) return;
    try {
      setName(n, i, (await host.renamePreset(n, i, value)).name);
    } catch (e) {
      toast(e.message, "bad");
    }
  }

  async function applyName(target, name) {
    if (!name) return;
    try {
      setName(target.player, target.index, (await host.renamePreset(target.player, target.index, name)).name);
    } catch (e) {
      toast(e.message, "bad");
    }
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

  let saving = false;

  async function save() {
    if (!state.current || saving || !changedPlates().length) return;
    const blocking = changedPlates().filter((t) => GT.validate(player(t.player).plates[t.index], player(t.player), catalog).length);
    if (blocking.length) {
      toast(`Not saved: ${blocking.map((t) => label(t.player, t.index)).join(", ")} break${blocking.length === 1 ? "s" : ""} a rule. Fix or undo first.`, "bad");
      return;
    }
    const sent = state.current;
    const n = changedPlates().length;
    saving = true;
    try {
      const r = await host.save(GT.toSaveRequest(sent));
      state.saved = sent;
      render();
      toast(`Saved ${n} preset${n === 1 ? "" : "s"}. Backup: ${r.backup}`);
    } catch (e) {
      if (e.kind === "saveChanged") {
        toast(e.message, "bad");
        if (confirm("The save changed on disk since it was opened (the game or Steam Cloud wrote it).\n\nReload it now? Your unsaved changes will be lost; export them first if you want to keep them.")) {
          await openWith(() => host.reloadSave(), true);
        }
      } else {
        toast(e.message, "bad");
      }
    } finally {
      saving = false;
    }
  }

  // ---- Opening a save ----------------------------------------------------------------

  async function openWith(load, discard = false) {
    if (!discard && changedPlates().length && !confirm("Discard the unsaved changes?")) return;
    let dto;
    try {
      dto = await load();
    } catch (e) {
      $("#welcomeError").textContent = e.message;
      toast(e.message, "bad");
      return;
    }
    if (!dto || dto.cancelled) return;
    const save = GT.fromSaveDto(dto);
    state.fileName = dto.fileName;
    state.saved = save;
    state.current = save;
    state.names = save.names;
    state.gameUpdate = dto.gameUpdate || null;
    state.undo = [];
    state.redo = [];
    state.player = save.players[0]?.number ?? 1;
    state.preset = 0;
    $("#welcomeError").textContent = "";
    render();
    const broken = save.players.reduce((n, p) => n + p.plates.filter((pl) => GT.validate(pl, p, catalog).length).length, 0);
    toast(`Opened ${dto.fileName}.` + (broken ? ` ${broken} preset${broken > 1 ? "s" : ""} already break a rule (marked “!”).` : ""));
  }

  async function watchGame() {
    if (host.kind !== "app") return;
    try {
      const { running } = await host.gameRunning();
      if (running !== state.gameRunning) { state.gameRunning = running; renderTop(); }
    } catch { /* the next tick tries again */ }
    setTimeout(watchGame, 3000);
  }

  // ---- Paste dialog ------------------------------------------------------------------

  const paste = { plate: null, what: "", name: null, selected: new Set() };

  /** name: a preset name to give the targets once pasted (imports only). */
  function openPasteDialog(pl, what, preselect = [], name = null) {
    paste.plate = GT.clonePlate(pl);
    paste.what = what;
    paste.name = name;
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
    $("#shareWhat").textContent = fullLabel(state.player, state.preset);
    $("#shareCode").value = GT.encodeShareCode(plate(), catalog);
    $("#shareDialog").showModal();
  }

  async function exportSetups(scope) {
    const entries = [];
    const players = scope === "all" ? state.current.players : [player()];
    for (const p of players) p.plates.forEach((pl, i) => {
      if (scope === "preset" && i !== state.preset) return;
      entries.push({ name: presetName(p.number, i), from: { player: p.number, index: i }, plate: pl });
    });
    const own = presetName(state.player, state.preset);
    const base = scope === "all" ? "All presets"
      : scope === "player" ? `P${state.player} presets`
      : `${label(state.player, state.preset)}${own ? " - " + own : ""}`;
    const name = base.replace(/[\\/:*?"<>|]/g, "").trim() + GT.FILE_EXTENSION;
    try {
      const r = await host.exportFile(name, GT.exportJson(entries, catalog));
      if (!r.cancelled) toast(`Exported ${entries.length} preset${entries.length > 1 ? "s" : ""} to ${r.fileName}.`);
    } catch (e) {
      toast(e.message, "bad");
    }
  }

  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
  }

  // Import: onto the selected preset first; a whole file can also go back where it came from.

  function openImportDialog() {
    $("#importTarget").textContent = fullLabel(state.player, state.preset);
    $("#importText").value = "";
    $("#importError").textContent = "";
    $("#importNote").replaceChildren();
    $("#importAll").replaceChildren();
    $("#importList").replaceChildren();
    $("#importDialog").showModal();
    $("#importText").focus();
  }

  const describePlate = (pl) =>
    [...pl.gadgets.map((id) => gadget(id)?.name ?? `Unknown (${id})`), ...(pl.super ? ["★ " + (gadget(pl.super)?.name ?? pl.super)] : [])].join(" · ") || "Empty";

  function readImport(text) {
    $("#importError").textContent = "";
    $("#importNote").replaceChildren();
    $("#importAll").replaceChildren();
    $("#importList").replaceChildren();
    const trimmed = text.trim();
    if (!trimmed) return;
    let entries, gameVersion, age, missing = 0;
    try {
      if (/^gt\d-/i.test(trimmed)) {
        const d = GT.decodeShareCode(trimmed, catalog);
        ({ gameVersion, age, missing } = d);
        entries = [{ name: null, from: null, plate: d.plate, changes: d.changes, code: true }];
      } else {
        ({ gameVersion, age, entries } = GT.importJson(trimmed, catalog));
      }
    } catch (e) {
      $("#importError").textContent = e.message;
      return;
    }

    // Same game version: nothing to check. Older (or unknown): the gadgets were checked against
    // ours, and only those that changed are shown. Newer than ours: not compatible.
    const ours = GT.versionText(catalog.gameVersion);
    if (age === "newer") {
      $("#importError").textContent =
        `Made with game version ${gameVersion}, newer than yours (${ours}): not compatible. Update the game, then import again.`;
      return;
    }
    const notes = [];
    if (age === "older") notes.push(`Made with game version ${gameVersion}; yours is ${ours}. Its gadgets were checked against your version.`);
    if (missing) notes.push(`${missing} gadget${missing > 1 ? "s" : ""} of this code ${missing > 1 ? "are" : "is"} not in your game version and ${missing > 1 ? "were" : "was"} left out.`);
    $("#importNote").replaceChildren(...notes.map((t) => h("div", {}, t)));

    const here = { player: state.player, index: state.preset };
    const exists = (t) => state.current.players.some((p) => p.number === t.player && t.index < p.plates.length);
    const placed = entries.filter((e) => e.from && exists(e.from));
    if (placed.length > 1) {
      $("#importAll").replaceChildren(h("button", {
        type: "button", class: "primary wide",
        onclick: () => { $("#importDialog").close(); putBack(placed); },
      }, `Put the ${placed.length} presets back where they came from`));
    }

    $("#importList").replaceChildren(...entries.map((e, n) => h("div", { class: "import-entry" + (e.changes.length ? " changed" : "") },
      h("div", { class: "what" },
        h("div", {}, e.name ? `“${e.name}”` : e.code ? "Share code" : `Preset ${n + 1}`,
          e.from ? h("span", { class: "muted" }, ` · from ${label(e.from.player, e.from.index)}`) : null,
          age === "older" || age === "unknown"
            ? h("span", { class: e.changes.length ? "tag-bad" : "tag-ok" }, e.changes.length ? " · changed since" : " · compatible")
            : null),
        h("div", { class: "names" }, describePlate(e.plate)),
        e.changes.length ? h("div", { class: "changes" }, e.changes.map((c) => h("div", {}, GT.describeChange(c)))) : null),
      h("button", { type: "button", class: entries.length === 1 ? "primary" : null, onclick: () => { $("#importDialog").close(); putImported(e, [here]); } },
        `Put on ${label(here.player, here.index)}`),
      h("button", { type: "button", onclick: () => { $("#importDialog").close(); openPasteDialog(e.plate, e.name ? `“${e.name}”` : "the imported preset", [], e.name); } },
        "Paste to…"))));
  }

  function putImported(entry, targets) {
    const what = entry.name ? `“${entry.name}”` : "the imported preset";
    const result = GT.paste(state.current, entry.plate, targets, catalog, "strict");
    if (!result.applied) {
      openPasteDialog(entry.plate, what, targets, entry.name);
      return;
    }
    commit(result.save, `Imported ${what} onto ${targets.map((t) => label(t.player, t.index)).join(", ")}.`);
    for (const t of targets) applyName(t, entry.name);
  }

  function putBack(entries) {
    const run = (mode) => {
      let save = state.current;
      const refused = [];
      for (const e of entries) {
        const r = GT.paste(save, e.plate, [e.from], catalog, mode);
        if (r.applied) save = r.save;
        else refused.push(`${label(e.from.player, e.from.index)}: ${GT.describe(r.targets[0].issues[0], catalog)}`);
      }
      return { save, refused };
    };
    let r = run("strict");
    if (r.refused.length) {
      if (!confirm(`${r.refused.length} preset${r.refused.length > 1 ? "s" : ""} refuse:\n${r.refused.join("\n")}\n\nPut them back anyway, leaving out the gadgets each player cannot use?`)) return;
      r = run("dropMissing");
      if (r.refused.length) { toast(`Nothing imported: ${r.refused.join("; ")}`, "bad"); return; }
    }
    commit(r.save, `Imported ${entries.length} presets back where they came from.`);
    for (const e of entries) applyName(e.from, e.name);
  }

  // ---- Wiring ------------------------------------------------------------------------

  function fillCategories() {
    $("#category").replaceChildren(h("option", { value: "" }, "All categories"), ...catalog.categories.map((c) => h("option", { value: c }, c)));
  }

  function wire() {
    const cat = $("#category");
    cat.onchange = () => { state.filter.category = cat.value; renderLibrary(); };
    $("#search").oninput = (e) => { state.filter.text = e.target.value; $("#gameSearch").value = e.target.value; renderLibrary(); };
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
    $("#gameState").onclick = () => { if (host.toggleGame) { state.gameRunning = host.toggleGame(); renderTop(); } };
    $("#openBtn").onclick = () => openWith(() => host.openSave());
    $("#welcomeOpen").onclick = () => openWith(() => host.openSave());
    $("#welcomeSample").onclick = () => openWith(() => host.openSample());
    $("#copyBtn").onclick = copyPreset;
    $("#pasteHereBtn").onclick = () => state.clipboard && pasteOrAsk(state.clipboard.plate, state.clipboard.label, [{ player: state.player, index: state.preset }]);
    $("#pasteToBtn").onclick = () => openPasteDialog(plate(), label(state.player, state.preset));
    $("#shareBtn").onclick = openShareDialog;
    $("#clearBtn").onclick = () => setPlate({ gadgets: [], super: null }, `Cleared ${label(state.player, state.preset)}.`);
    $("#importBtn").onclick = openImportDialog;
    $("#importText").addEventListener("input", (e) => {
      if (/^\s*gt\d-[A-Za-z0-9_-]{8,}\s*$/i.test(e.target.value)) readImport(e.target.value);
    });

    $("#viewSwitch").onclick = (e) => {
      const b = e.target.closest("button");
      if (!b || b.dataset.view === state.view) return;
      state.view = b.dataset.view;
      savePref("gadgettool.view", state.view);
      render();
    };
    $("#themeSelect").onchange = (e) => {
      state.theme = e.target.value;
      savePref("gadgettool.theme", state.theme);
      render();
    };

    const nameInput = $("#presetName");
    nameInput.addEventListener("change", () => nameCurrentPreset(nameInput.value));
    nameInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") nameInput.blur();
      else if (e.key === "Escape") { nameInput.value = presetName(state.player, state.preset) || ""; nameInput.blur(); }
    });

    $("#gameSearch").oninput = (e) => { state.filter.text = e.target.value; $("#search").value = e.target.value; renderGameGadgets(); };
    const gamePanel = $("#gameGadgets");
    gamePanel.addEventListener("dragover", (e) => { if (e.dataTransfer.types.includes("text/gt-placed")) { e.preventDefault(); gamePanel.classList.add("over"); } });
    gamePanel.addEventListener("dragleave", (e) => { if (!gamePanel.contains(e.relatedTarget)) gamePanel.classList.remove("over"); });
    gamePanel.addEventListener("drop", (e) => { gamePanel.classList.remove("over"); const id = e.dataTransfer.getData("text/gt-placed"); if (id) { e.preventDefault(); removeGadget(id); } });

    $("#pasteDrop").onchange = renderPasteGrid;
    $("#pasteDialog").addEventListener("close", () => {
      if ($("#pasteDialog").returnValue !== "paste" || !paste.result?.applied) return;
      const n = paste.result.targets.length;
      commit(paste.result.save, `Pasted ${paste.what} on ${n} preset${n > 1 ? "s" : ""}.`);
      if (paste.name) for (const o of paste.result.targets) applyName(o.target, paste.name);
    });

    $("#copyCodeBtn").onclick = async () => toast(await copyText($("#shareCode").value) ? "Code copied." : "Select the code and copy it.", "ok");
    $("#shareDialog").addEventListener("click", (e) => { const b = e.target.closest("[data-export]"); if (b) exportSetups(b.dataset.export); });

    $("#importReadBtn").onclick = () => readImport($("#importText").value);
    $("#importFileBtn").onclick = async () => {
      try {
        const file = await host.importFile();
        if (file.cancelled) return;
        $("#importText").value = file.content;
        readImport(file.content);
      } catch (e) {
        $("#importError").textContent = e.message;
      }
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
      if (mod && e.key.toLowerCase() === "o") { e.preventDefault(); if (!typing) openWith(() => host.openSave()); return; }
      if (typing || !state.current) return;
      if (mod && e.key.toLowerCase() === "z" && !e.shiftKey) { e.preventDefault(); undo(); }
      else if (mod && (e.key.toLowerCase() === "y" || (e.key.toLowerCase() === "z" && e.shiftKey))) { e.preventDefault(); redo(); }
      else if (mod && e.key.toLowerCase() === "c" && !String(window.getSelection())) { copyPreset(); }
      else if (mod && e.key.toLowerCase() === "v" && state.clipboard) { e.preventDefault(); $("#pasteHereBtn").click(); }
      else if (["ArrowDown", "ArrowUp", "PageDown", "PageUp"].includes(e.key)) {
        e.preventDefault();
        const n = player().plates.length;
        const next = e.key === "ArrowDown" || e.key === "PageDown";
        state.preset = (state.preset + (next ? 1 : n - 1)) % n;
        render();
      }
    });
  }

  // ---- Language of the game's texts (gadget names, categories, descriptions) -----------

  function loadLanguage() {
    try { return localStorage.getItem("gadgettool.language") || navigator.language || "en"; } catch { return navigator.language || "en"; }
  }

  /** Each language under its own name ("français", "日本語"...). */
  function languageName(code) {
    try { return new Intl.DisplayNames([code], { type: "language" }).of(code) || code; } catch { return code; }
  }

  function fillLanguages() {
    const select = $("#langSelect");
    select.hidden = catalog.languages.length < 2;
    select.replaceChildren(...catalog.languages.map((l) => h("option", { value: l }, languageName(l))));
    select.value = catalog.language;
  }

  async function loadCatalog(language) {
    catalog = GT.makeCatalog(await host.catalog(language));
    fillLanguages();
    fillCategories();
    state.filter.category = "";
  }

  async function start() {
    wire();
    render();
    try {
      await loadCatalog(loadLanguage());
    } catch (e) {
      $("#welcomeError").textContent = `The gadget list could not be read: ${e.message}`;
      return;
    }
    $("#langSelect").onchange = async (e) => {
      try { localStorage.setItem("gadgettool.language", e.target.value); } catch { /* comfort only */ }
      try { await loadCatalog(e.target.value); } catch (err) { toast(err.message, "bad"); }
      render();
    };
    render();
    watchGame();
    // In a browser preview, start on the sample right away.
    if (host.kind === "browser") await openWith(() => host.openSample());
  }

  // For the window: it asks before closing with unsaved changes.
  window.GTApp = { unsavedCount: () => changedPlates().length };

  start();
})();
