# 2026-11-19, measuring PC (game installed)

Second session of the day, on a PC with the game (2.0.0, Steam build 24996408). The save
format is known and read: **the project is a go.** Nothing is committed yet; the work is on
the branch `core/crossworlds-save-read`. **The resume point is at the end of this note.**

## What was done

1. Pulled the repository, `dotnet build` and `dotnet test` green (154 tests).
2. Game data: the paks open with the AES key already used for modding on this PC (kept on
   that PC, never in git). The gadget tables sit in `/Game/01_Union/Database/Gadget/`
   (`DT_GadgetData`, `DT_GadgetPlateData` with `EGadgetPlateId::Rank0`..`Rank7` and
   `RentalPlate`, `DT_GadgetLayoutData`, `DT_GadgetCustomCategoryData`,
   `DT_DefaultGadgetPreset`, `DA_GadgetSetting`...), icons in
   `/Game/01_Union/UI/Common/GadgetIcon/Texture/` and thumbnails in
   `/Game/01_Union/UI/Garage/GadgetCustom/Texture/GadgetTmb/`. The tables use unversioned
   properties: reading them needs a mappings file. A 2.0 `.usmap` was dumped in-game with
   UE4SS (Gibax pressed the key); it stays in the git-ignored `extracted/` folder.
3. Save (Gibax's OK, game closed): one copy of `app.sav` into `samples/A` (git-ignored).
   - 16-byte blocks in ECB mode (80,000 blocks, only 11,442 distinct), not compressed.
   - Not the pak key, nothing derived from the Steam ID, and no key in the exe on disk or in
     the game's crash dumps (known-plaintext scan of every 16/32-byte window).
   - With the game running (Gibax launched it, Gibax's OK), a read-only scan of its memory
     found the key in 96 s: AES-256. It is stored in `extracted/` on this PC only.
4. Decrypted the copy: a GVAS file with tagged properties (UE 5.4, complete type names). All
   findings are in `../memoire-agent.md`, section "The real save".
5. Core, read only (branch `core/crossworlds-save-read`):
   - `Saves/Gvas/GvasModel.cs`, `Saves/Gvas/GvasReader.cs`: reads a whole UE 5.4 GVAS file.
   - `Saves/CrossWorlds/SaveCipher.cs`: AES-256-ECB, PKCS#7.
   - `Saves/CrossWorlds/CrossWorldsSaveFormat.cs`: `ISaveFormat` that reads the four players'
     presets, the Super Slot, unlocked gadgets and the plate; `Write` refuses for now.
   - Tests: `GvasWriter` builds synthetic UE 5.4 saves; `GvasReaderTests`,
     `CrossWorldsSaveFormatTests` (made-up gadgets, made-up key). 177 tests, no warnings.
   - Checked locally on the real copy: 4 players x 10 presets, Super Slots, 123 unlocked
     gadgets, Grand Master plate; same result as an independent Python reader.
6. App: the game's process names (`SonicRacingCrossWorldsSteam`, launcher
   `SonicRacingCrossWorlds`) replace the guesses.

## Local-only things (measuring PC, git-ignored)

- `extracted/save_key.local.txt`: the save key. `extracted/mappings/`: the 2.0 `.usmap`.
- `extracted/tools/`: `keyscan.cs` (key scan in a file), `livescan.cs` (read-only scan of a
  running process), `gvas_dump.py` (Python reader), `check_save.cs` (reads a save through the
  Core: `dotnet run check_save.cs -- <app.sav> <key file>`), `sav_probe.py`.
- `extracted/legacy_gadget/`: the gadget tables extracted from the paks.
- `samples/A/app.sav` and `samples/A/app.decrypted.gvas`.

## RESUME POINT

Ask Gibax before launching anything or touching a real save.

1. Gibax decides how the public tool gets the save key (advice: in the release's data
   packs, never in git).
2. Commit the branch's work (Core, Tests, App, Notes: one commit per subject) and open a PR,
   when Gibax says so.
3. App: open the real save (find `app.sav` under `%APPDATA%\SEGA\SonicRacingCrossWorlds\steam`,
   load the key from the local file for now), read only, with the real gadget list.
4. Extractor (`src/GadgetTool.Extractor`, CUE4Parse): read `DT_GadgetData` & co. with the 2.0
   mappings into a `docs/DATA.md` catalog (names in every language, slot costs, categories,
   icons). Ids must match the save's `EGadgetId` names without the prefix.
5. Writing: rebuild the gadget arrays with cells (`SlotIndex` = first cell, Super Slot = 6),
   re-encrypt, test on a **copy** of the save in the game (Gibax's OK), then the real one with
   a backup.
