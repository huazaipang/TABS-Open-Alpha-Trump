# Requirements

## Core Unit (`UNIT_TRUMP_OPEN_ALPHA`)

- **GUIDs**: deterministic negative IDs `DatabaseID(-1, 20161108)` for the unit blueprint and `DatabaseID(-1, 20161109)` for the `Open Alpha` faction.
- **Stats**: cost `2000`, health `700`, movement speed multiplier `1.15`, size multiplier `1.05` (no random variance), mass multiplier `1.25`, attack speed multiplier `1.25`, damage multiplier `2.15`.
- **Weapons**: dual-wielded assault rifles (`internalCooldown = 0.095s`).
- **Appearance**: black suit, white shirt, red tie, blond hair, gunmetal rifles, plus fallback visuals if palette/props are unavailable.
- **Voices**: embedded 2016 Open Alpha clips (`assets/voices/donald13.wav`, `assets/voices/donald25.wav`) played on battle entry and periodically during combat, without requiring external audio directories.

## Physical Wall Ability (`TrumpWallAbility`)

- Spawns a physical brick wall (`14.0m` wide, `5.8m` tall, `0.54m` thick) from `22.0m` height after a `0.32s` ground telegraph.
- Impact deals `320` damage with dust and impact VFX; wall persists for `12.0s`.
- Cooldowns: `2.0s` initial AI delay, `8.5s` AI cooldown, `5.0s` first-person possession cooldown.
- Maximum cast range `19.0m` (spawn offset clamped to `12.0m`).

## Factions, Campaigns, and Balance

- Register the unit into both **Legacy** and a dedicated **Open Alpha** (`内测版`) faction added to the default faction hotbar.
- Allow **Open Alpha** in all official and custom/Workshop campaign levels (`AllowedFactions` and placement faction bar).
- **Hard balance constraint**: preserve every campaign level's original budget; do not raise budgets under `2000` to force-fit Trump.

## Localization and Distribution

- Bilingual localization via `Localizer.GetSinglePhrase`: Simplified Chinese (`LANG_CH`) and English (`Trump (Open Alpha)` / `川普 (内测版)`).
- Single-DLL distribution (`TABSTrumpMod.dll`) built via `build.sh` with embedded WAV resources.
