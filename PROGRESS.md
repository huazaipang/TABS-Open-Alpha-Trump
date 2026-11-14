# Progress

## Milestones

- [x] **M0 Core Unit & Wall Ability**: Dual assault rifles, stats (`2000` cost / `700` HP), sky-drop brick wall (`14m x 5.8m`, `320` damage, `12s` lifetime), embedded Open Alpha WAV voices, bilingual UI.
- [x] **M1 Campaign Balance & Unlock**: Unlocked across official and Workshop campaigns via faction whitelist injection while preserving original `< 2000` level budgets.
- [x] **M2 Public GitHub Release**: Cleaned repository structure (`README.md`, `LICENSE`, `.gitignore`, `build.sh`), published to `https://github.com/huazaipang/TABS-Open-Alpha-Trump`, tagged and released `v1.0.0` with `TABSTrumpMod-v1.0.0.zip` and `TABSTrumpMod.dll`.
- [x] **M3 Project Permanent Setup**: Promoted workspace from `F:\project\temp\test\TABSTrumpMod` to `F:\project\TABSTrumpMod`, added in-repo documentation (`CLAUDE.md`, `REQUIREMENTS.md`, `DECISIONS.md`, `PROGRESS.md`) and Claude project memory.
- [ ] **M4 Post-Release Verification & Polish**: User in-game verification of campaign `< 2000` budget preservation and future balance/FX tweaks if needed.

## Log

### 2026-11-14

- Implemented `TABSTrumpPlugin.cs` (328 lines), `TrumpAssetBuilder.cs` (1,454 lines), and `TrumpWallAbility.cs` (2,819 lines).
- Reverted an earlier campaign budget floor override so levels with budgets `< 2000` retain their original difficulty.
- Added `build.sh`, `.gitignore`, MIT `LICENSE`, and bilingual `README.md`.
- Compiled `dist/TABSTrumpMod.dll` cleanly with `csc.exe` and deployed to TABS `BepInEx/plugins/TABSTrumpMod/`.
- Published initial commit (`e984803`) to `https://github.com/huazaipang/TABS-Open-Alpha-Trump` with clean contributor history (`huazaipang` only) and created GitHub Release `v1.0.0`.
- Moved local repository to permanent location `F:\project\TABSTrumpMod` and added project engineering docs (`CLAUDE.md`, `REQUIREMENTS.md`, `DECISIONS.md`, `PROGRESS.md`) plus Claude memory files.
