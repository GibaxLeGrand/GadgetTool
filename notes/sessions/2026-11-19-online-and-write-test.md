# 2026-11-19 (evening), measuring PC: is the plate online? Preparing the write test

Gibax, away, gave the go-ahead (relayed by another session: "Oui, les trois") to move open
questions 6 ("Online") and 9 ("Writing") of `../memoire-agent.md` forward **without running
the game and without touching the real save**. Everything below comes from the web, from the
game's files read with the extractor's tools (read-only), from a **copy** of a real save
(`samples/A`, taken earlier with Gibax's OK) and from Steam's own index of the game's cloud
files. Nothing here was checked in the game.

## Question 6: is the gadget plate also kept online?

**Answer: no sign of it. The plate, the presets, the unlocked gadgets and the plate level
live in `app.sav`, which Steam Cloud copies; the game's online services do not appear to
keep them.** What remains uncertain is listed at the end.

What points that way:

- **Online stack.** The game uses Epic Online Services through the Redpoint EOS plugin
  (`UNION/Binaries/Win64/RedpointEOS/EOSSDK-Win64-Shipping.dll`;
  `[EpicOnlineServices]` in `UNION/Config/DefaultEngine.ini`). The login is anonymous
  (`AuthenticationGraph=Anonymous`, `CrossPlatformAccountProvider=None`,
  `RequireCrossPlatformAccount=False`): a device id, no Epic account. EOS anti-cheat and
  sanction checks are off in that file (`EnableAntiCheat=False`,
  `EnableSanctionChecks=False`). The config's ids, secret and keys are not copied here.
- **The save is the store.** `app.sav`'s top level holds every progress section
  (`_UserMachineAssemblyData` with the presets and `GadgetList`, `_UserCurrencyData`,
  `_StatsData`, `_UserTimeTrialData`...). The only account link in it is the player's own
  EOS id (`_UserCommonData.ProductUserId`); `_AppFriendUserData._RecentPlayers` lists other
  players' EOS ids.
- **The server pushes gifts, the save keeps the score.** `_CompensationData`
  (`CompensationIdArray`) and `_UserRestorationData` (`ExecutedInnerDataMap` by
  `ERestorationCategory`) record which server-sent compensations and "online reward
  restorations" (`BPC_OnlineRewardRestoration`) the game has already applied. Their titles
  (`DT_CompensationTitle`, `DT_CompensationBody`) are "Compensation", "Gift", "New Gadgets",
  "Compensation for System Failure", "... for Maintenance", "... for issues with Online
  Mode", campaign and festival rewards. A server that kept the inventory would not need the
  client to remember which gifts it applied.
- **Nothing in the exe uploads presets.** The game's own names around presets
  (`GadgetPresetDataLists`, `GetSelectedGadgetPresetIdsAllPlayer`,
  `IsAttachableGadgetToGadgetPresetData`...) say nothing online; the cloud-storage strings
  found (`Error_PlayerDataStorage_*`, `UserCloudBuckets`,
  `FOnlineAsyncTaskSteamWriteUserFile`) are the EOS SDK's, Redpoint's and Unreal's Steam
  subsystem's, compiled in whether used or not. A `PlayerDataEncryptionKey` is set in the
  config, which EOS player data storage would need; nothing shows the game using it.
- **The web.** SEGA documents no online save. Version 1.2.0 "adjusted network
  communications so that the server prioritizes synchronizing data that affects race
  results" (race data, not saves). SEGA warned players about cheating, unauthorized external
  tools and modified game content, with penalties up to account suspension; its network
  terms allow "limiting/deleting some or all" of a cheater's save data.

What goes online with the plate:

- **During an online race** the equipped gadgets matter to the race, so they are sent to the
  other players or the server for that race (not stored).
- **Time Trial ghosts carry the gadget preset** (`ConvertGhostDataToGadgetPreset`,
  `GhostGadgetPresetData`, `GetTimeTrialGadgetPreset` in the exe; `GhostHash` in
  `_UserTimeTrialData`): a record set online shows the plate it was set with.
- A speedrun.com forum thread mentions a leaderboard run that used kits "that should have
  been unavailable" (could not be opened: 403), so others can see impossible plates.

Still uncertain:

- Whether the game checks, online, that a plate is possible (gadgets unlocked, slots, plate
  level). GadgetTool only writes plates the game itself allows, with gadgets the save has
  unlocked: such a plate cannot be told apart from one made in the game.
- What `net.sav` (39 KB, next to `app.sav`, also in Steam Cloud) holds: not read (it is part
  of the real save folder).
- Whether EOS player data storage is used for anything (the key in the config says it could
  be).

Sources: the game's files (above); [SEGA, Ver.1.2.0 update notice](https://asia.sega.com/SonicRacingCrossWorlds/en/update/v-1-2-0.html);
[SEGA, Open Network Test FAQ](https://asia.sega.com/SonicRacingCrossWorlds/en/news/open-network-test.html)
(test saves not carried over); [Epic Online Services spotlight on the game](https://onlineservices.epicgames.com/spotlights/sega-delivers-global-crossplay-in-sonic-racing-crossworlds-with-epic-online-services)
(behind a login; seen through search results only);
[Operation Sports on SEGA's cheating warning](https://www.operationsports.com/sega-issues-warning-to-sonic-racing-crossworlds-players-over-cheating-and-modding)
and [Game*Spark](https://www.gamespark.jp/article/2025/10/10/158244.html) (search results);
[speedrun.com forum thread](https://www.speedrun.com/srcw/forums/y06c8) (search result).

## Question 9: writing, checked on a copy

Local tool `extracted/tools/check_q9.cs` (git-ignored; refuses to work in the game's save
folder), on a copy of a real save, results in `samples/Q9` (git-ignored):

1. **Decrypt then encrypt again gives the same bytes** (1 279 968 bytes).
2. **Read then write back unchanged gives the same file.**
3. **A small change reads back right**: P1 preset A without its last gadget (a kit) reads
   back as written; it is the only preset that differs; unlocked gadgets and plate level
   are unchanged.
4. **Counters and checksums.** The save has **no checksum**: the GVAS header and `_Header`
   (`_Version` 39862400, `_RomVersion` 20000, `_SaveCount` 4093) are the only "header"
   fields, and no property name in the file looks like a hash or a signature of the file
   (the 369 names were searched; `GhostHash` and `bIsValidData` belong to Time Trial
   ghosts). **GadgetTool leaves `_SaveCount` as it is**: the header and everything before
   `_UserMachineAssemblyData`'s size field are identical, and everything after the two
   preset lists too. Whether the game minds an unchanged `_SaveCount` can only be seen in
   the game (it would be odd: it is the value the game itself wrote last).
5. **Steam Cloud.** `Steam\userdata\<id>\2486820\remotecache.vdf` lists `app.sav`,
   `net.sav`, `playlog.sav` and the replays under `SEGA/SonicRacingCrossWorlds/steam/<id>/`
   (`root` 4 = `%APPDATA%`), each with its size, times and SHA-1, all synced. Steam syncs
   when the game starts and after it exits ([Steamworks: Steam Cloud](https://partner.steamgames.com/doc/features/cloud)).
   A file changed while the game is closed has another SHA-1 than the index. Steamworks
   does not document what Steam then does; the usual behaviour is that, if the cloud copy
   did not change meanwhile, Steam keeps the local file and uploads it after the next
   session, and that, if the cloud copy changed too (the game played on another device),
   Steam asks which one to keep. Not checked here: hence the procedure below.

## The real test (Gibax does it himself)

Do it on a day you can check right away, on the measuring PC, **without playing the game on
another device (Steam Machine) in between**.

1. **Close the game and its launcher** (`SonicRacingCrossWorlds.exe` stays open while the
   game runs: check the Task Manager).
2. **Back up the whole save folder** to D:, dated, for instance
   `Copy-Item "$env:APPDATA\SEGA\SonicRacingCrossWorlds\steam" "D:\GadgetTool_backup\save-YYYYMMDD-HHMM" -Recurse`.
3. **Turn Steam Cloud off for the game** for this first test: Steam > Sonic Racing:
   CrossWorlds > Properties > General > "Keep game saves in the Steam Cloud" off. The cloud
   then keeps the original as a second backup, and Steam cannot overwrite the written file.
4. **Write one small, visible change** with GadgetTool (`extracted\lancer_gadgettool.cmd`):
   on a preset you do not use (P1 J, for instance), a simple plate, then Save. Note the
   backup path GadgetTool shows (`%LOCALAPPDATA%\GadgetTool\backups`).
5. **Start the game** and check: it loads with no error and no "new save" or "data
   corrupted" message; the gadget screen shows the change on P1 J; the other presets, the
   unlocked gadgets, the plate level and the rest of the progress are as before. Use the
   preset in an **offline** race (Grand Prix or a free race; not Time Trial, whose ghost
   carries the plate online, and not Ranked). Back to the menu so the game saves, then quit.
6. **Open the save in GadgetTool again**: P1 J should still be as written (the game kept
   it), and `_SaveCount` will have gone up by the game's own save.
7. **Turn Steam Cloud back on**, start the game once and quit, so Steam uploads the file. If
   Steam reports a conflict, keep **this PC's local files**.

**Going back**, at any step: close the game, copy `app.sav` from the step 2 backup (or
GadgetTool's backup) over the one in the save folder. With Steam Cloud still off, nothing
else is needed; with it on, if Steam reports a conflict, keep the local files.

What to report: any message from the game at start, anything missing or reset, and whether
P1 J looks as written. If all is well, question 9 is closed and writing can be used for
real presets.

## Result: the in-game test (2026-11-19, about 21:10 to 21:20, Gibax at the measuring PC)

**The game accepts a save written by GadgetTool.** Steps as above, with these details:

- The game and launcher were closed; the whole save folder was copied to
  `D:\GadgetTool_backup\save-20261119-2110` (53 files, every SHA-256 equal to the
  original). Steam Cloud's index already had the same `app.sav` (same size and SHA-1).
- GadgetTool (Release build of `main` at `4713f04`, started through `explorer.exe` so that it
  runs outside the Claude app's package, whose writes to `%AppData%` would be redirected)
  wrote the save twice; it backed up the file before each write
  (`%LOCALAPPDATA%\GadgetTool\backups\app.sav.<time>.bak`). Gibax changed **P1 E and P1 G**
  (not J). Read back from a copy: those two presets only, both within the rules; unlocked
  gadgets (123 a player), plate level (Grand Master) and `_Header` unchanged
  (`_SaveCount` 4152).
- **In the game**: no message at start; P1 E and P1 G as written; the rest as before. The
  game saved on its own about a minute later: its file keeps P1 E and P1 G exactly, every
  other preset is unchanged, and `_SaveCount` went from 4152 to 4153, so leaving it as the
  game last wrote it is fine.
- **Steam Cloud**: afterwards, Steam's index (`remotecache.vdf`) had the game's new
  `app.sav` (same size and SHA-1, synced), with no conflict. The index was updated a few
  seconds after the game's own save, during the first session: Steam Cloud was apparently
  still on then, and it did not matter.

Question 9 is closed: writing can be used for real presets. Local tools used (git-ignored):
`extracted/tools/check_q9.cs`, `extracted/tools/compare_saves.cs`; copies in `samples/T`.
