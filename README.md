# TABS Open Alpha Trump

A BepInEx 5 plugin for **Totally Accurate Battle Simulator (TABS)** that restores the classic 2016 Open Alpha Trump unit with dual assault rifles, a sky-drop physical wall ability, embedded Open Alpha voice lines, and full campaign availability.

[中文说明](#中文说明)

## Features

- **Unit Stats**: Cost `2000`, Health `700`, dual-wielding assault rifles with medium-range combat movement.
- **Physical Wall Ability**: Drops a destructible-style physical brick wall (`14m` wide, `5.8m` tall, `320` impact damage, `12s` lifetime) with ground telegraphing and dust/impact VFX. Supports AI casting (`8.5s` cooldown) and first-person possession casting (`5.0s` cooldown).
- **Embedded Open Alpha Voice Lines**: Bundles `donald13.wav` and `donald25.wav` inside the compiled plugin DLL (`TABSTrumpMod.dll`), playing on battle entry and during combat without external asset folders.
- **Faction & Campaign Unlock**:
  - Added to both the **Legacy** faction and a dedicated **Open Alpha** (`内测版`) faction.
  - Unlocked across all official and custom/Workshop campaigns by adding the Open Alpha faction to campaign faction whitelists while preserving every level's original budget.
- **Bilingual UI**: Automatically switches unit and faction names/descriptions between English and Simplified Chinese based on the game language.

## Installation

1. Install **BepInEx 5.x (x64)** into your Totally Accurate Battle Simulator game folder.
2. Download the latest `TABSTrumpMod.dll` (or release zip) from the [Releases](../../releases) page.
3. Place the plugin at:
   ```text
   <TABS Install Folder>/BepInEx/plugins/TABSTrumpMod/TABSTrumpMod.dll
   ```
4. Launch the game.

## Building from Source

Requirements:
- Windows `.NET Framework 4.x` (`csc.exe`)
- TABS installed with **BepInEx 5.x** (`BepInEx.dll`, `0Harmony.dll`)
- Git Bash (or compatible POSIX shell on Windows)

Build steps:
```bash
export TABS_DIR="C:\\Program Files (x86)\\Steam\\steamapps\\common\\Totally Accurate Battle Simulator"
./build.sh
```

The compiled plugin is written to `dist/TABSTrumpMod.dll` and automatically copied into `$TABS_DIR/BepInEx/plugins/TABSTrumpMod/` unless `NO_DEPLOY=1` is set.

## Compatibility & Asset Notice

- **Multiplayer / Workshop**: All players in a multiplayer match must have the same mod installed. Custom battles containing this unit require the mod to load.
- **Audio Clips**: `assets/voices/donald13.wav` and `assets/voices/donald25.wav` are preserved from 2016 Open Alpha community archives for non-commercial fan-mod use only.

---

## 中文说明

基于 **BepInEx 5** 的《全面战争模拟器（TABS）》模组，复刻 2016 年内测版（Open Alpha）经典单位「川普 (内测版)」。

### 主要特性

- **兵种定位**：造价 `2000`、生命值 `700`，双持突击步枪进行中距离火力压制。
- **召唤高墙技能**：向目标区域空投砸落一道实体砖墙（宽 `14m`、高 `5.8m`、砸落伤害 `320`、持续 `12s`），带落地预警光圈与烟尘冲击特效；AI 自动施放冷却 `8.5s`，附身操控冷却 `5.0s`。
- **内嵌原版语音**：两段 2016 内测版语音直接内嵌于 `TABSTrumpMod.dll` 中，无需额外配置外部音频目录。
- **派系与全战役解锁**：
  - 同时加入 **Legacy（传承）** 派系和独立的 **内测版（Open Alpha）** 派系。
  - 解锁全官方战役及自定义/创意工坊战役可用，且**保留所有关卡的原版预算限制**，不破坏关卡平衡。
- **双语本地化**：随游戏内语言设置自动切换简体中文 / 英文名称与介绍。

### 安装方法

1. 为《全面战争模拟器》安装 **BepInEx 5.x (x64)**。
2. 前往本仓库 [Releases](../../releases) 页面下载 `TABSTrumpMod.dll`（或发布压缩包）。
3. 将文件放入游戏目录：
   ```text
   <TABS 游戏根目录>/BepInEx/plugins/TABSTrumpMod/TABSTrumpMod.dll
   ```
4. 启动游戏即可游玩。
