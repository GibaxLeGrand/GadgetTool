# GadgetTool: rules for agents

Read first, in this order:

1. [notes/README.md](notes/README.md), then [notes/memoire-agent.md](notes/memoire-agent.md)
   (how Gibax works, every decision, open questions) and the last note in
   [notes/sessions/](notes/sessions/) (the resume point).
2. [README.md](README.md): what the tool is.

## Non-negotiable rules

- **Talk to Gibax (the project owner) in French. Everything committed is in English.**
- **The repository is public.** Nothing personal anywhere (no local paths, user names, Steam
  IDs), no save files, no SEGA assets (icons, data tables, text taken from the game).
- **Nothing is launched without Gibax's explicit OK, every time**: the game, the tool's exe,
  anything that reads or writes a real save. `dotnet build` and `dotnet test` need no OK.
- **One commit per subject.** Ask before pushing a commit that touches `src/`, `tests/`,
  `web/` or `.github/workflows/`; docs and notes are pushed freely. Subjects start with
  their area: `Core:`, `Tests:`, `Web:`, `CI:`, `Docs:`, `Notes:`.
- `dotnet build` stays free of warnings (warnings are errors) and `dotnet test` stays green.
  Every rule in `src/GadgetTool.Core` comes with its unit test.
- **At the end of a session**, add a note in `notes/sessions/` and bring
  `notes/memoire-agent.md` up to date.
