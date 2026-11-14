# Architectural Decisions

Each entry records what was decided, why, and what alternatives were rejected.

## D1 Runtime asset synthesis without external AssetBundles (2026-11-14)

- **Decision**: Clone/configure `UnitBlueprint`, weapons, props, palette colors, and procedural wall/VFX/portrait textures in code via `TrumpAssetBuilder` instead of shipping a Unity `.assetbundle`.
- **Why**: Avoids Unity Editor version lock-in and keeps the entire mod buildable from three C# files with .NET Framework `csc.exe`.
- **Rejected**: Authoring custom Unity AssetBundles in the Unity Editor.

## D2 Deterministic negative `DatabaseID` values (2026-11-14)

- **Decision**: Use `DatabaseID(-1, 20161108)` for the unit and `DatabaseID(-1, 20161109)` for the `Open Alpha` faction.
- **Why**: Negative mod namespace avoids collisions with official TABS content IDs while keeping custom battles/save references stable across restarts.
- **Rejected**: Random or auto-incrementing runtime IDs.

## D3 Embedded WAV resources inside `TABSTrumpMod.dll` (2026-11-14)

- **Decision**: Compile `assets/voices/donald13.wav` and `assets/voices/donald25.wav` into the DLL via `-resource:` flags in `build.sh` and decode PCM WAV bytes at runtime into `AudioClip` instances.
- **Why**: Users only need to drop a single `TABSTrumpMod.dll` into `BepInEx/plugins/TABSTrumpMod/` without managing external asset folders.
- **Rejected**: Loading loose `.wav` files from disk relative to the plugin path.

## D4 Campaign unlock via faction whitelist only (2026-11-14)

- **Decision**: Append `Open Alpha` to `TABSCampaignLevelAsset.AllowedFactions` and `PlacementFactionBar.Spawn` in campaign mode without modifying level budgets.
- **Why**: Early campaign levels with budgets `< 2000` remain balanced as designed by Landfall; levels with `>= 2000` budget can freely deploy Trump.
- **Rejected**: Raising low-budget campaign levels to `2000+`, which breaks early-game campaign difficulty.

## D5 Targeted Harmony patches in `TABSTrumpPlugin` (2026-11-14)

- **Decision**: Patch `ContentDatabase.Instance`, `UnitEditorColorPalette.Initialize`, `UnitBlueprint.Spawn` (9-parameter overload), `UnitAPI.SetMovementSpeed`, `Localizer.GetSinglePhrase`, `CampaignPlayerDataHolder.GetCurrentLevel`, `CampaignGameMode.LoadNewLevel`, `CampaignHandler.ApplySceneData`, and `PlacementFactionBar.Spawn`.
- **Why**: Ensures deferred palette colors apply after palette initialization, attaches `TrumpWallAbility` on every spawn, overrides AI strafing smoothly, handles bilingual strings without modifying game asset files, and covers both official and Workshop campaign loading paths.
- **Rejected**: Replacing core game assemblies or mutating serialized game files on disk.

## D6 Clean repository and zero AI attribution (2026-11-14)

- **Decision**: Host source on `https://github.com/huazaipang-gif/TABS-Open-Alpha-Trump` with `dist/` gitignored, shipping compiled DLLs via GitHub Releases (`v1.0.0+`). Commits and PRs carry only `huazaipang-gif` as author with no `Co-Authored-By` trailers.
- **Why**: Keeps contributor history clean and prevents binary bloat in git history.
- **Rejected**: Committing compiled DLLs to git or appending AI co-author trailers.
