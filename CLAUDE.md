# TABSTrumpMod

BepInEx 5 plugin for Totally Accurate Battle Simulator (TABS) restoring the 2016 Open Alpha Trump unit (`UNIT_TRUMP_OPEN_ALPHA`) with dual assault rifles, a sky-drop physical wall ability, embedded Open Alpha voice clips, bilingual EN/ZH-CN UI, and full campaign availability.

Repository: `https://github.com/huazaipang/TABS-Open-Alpha-Trump`

## Read first

- `REQUIREMENTS.md`: functional, balance, and distribution requirements.
- `DECISIONS.md`: architectural and balance decisions (`D1`–`D6`). Check before changing asset building, Harmony hooks, or campaign logic.
- `PROGRESS.md`: milestone status, release history, and pending verification items. Update it when work lands.

## Build and deploy

Requires Windows `.NET Framework 4.x` (`csc.exe`), Git Bash, and an installed TABS + BepInEx 5.x directory (`E:\steam\steamapps\common\Totally Accurate Battle Simulator` or `C:\Program Files (x86)\Steam\steamapps\common\Totally Accurate Battle Simulator`, or override via `TABS_DIR`).

```bash
./build.sh                  # compile dist/TABSTrumpMod.dll and deploy to TABS BepInEx/plugins/TABSTrumpMod/
NO_DEPLOY=1 ./build.sh      # compile only without copying into game folder
```

## Rules

- Preserve original campaign budgets in `EnsureCampaignLevelAllowsTrump`: never raise or bypass level budgets `< 2000`.
- Keep all source code, comments, commit messages, PR descriptions, and documentation in a plain, neutral engineering tone.
- Never add `Co-Authored-By`, `Generated with Claude Code`, `sessionUrl`, or any AI attribution to git commits or pull requests. Keep `huazaipang` as the sole commit author.
- Never commit `dist/`, `*.dll`, `*.pdb`, or release zip archives.
- Do not drive the game UI via desktop automation; compile/deploy and let the user verify in-game.
