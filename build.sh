#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WIN_DIR="$(cygpath -w "$SCRIPT_DIR")"

DEFAULT_TABS_E="E:\\steam\\steamapps\\common\\Totally Accurate Battle Simulator"
DEFAULT_TABS_C="C:\\Program Files (x86)\\Steam\\steamapps\\common\\Totally Accurate Battle Simulator"
if [[ -z "${TABS_DIR:-}" ]]; then
  if [[ -d "$(cygpath -u "$DEFAULT_TABS_E")" ]]; then
    TABS_DIR="$DEFAULT_TABS_E"
  else
    TABS_DIR="$DEFAULT_TABS_C"
  fi
fi

MANAGED="$TABS_DIR\\TotallyAccurateBattleSimulator_Data\\Managed"
BEPINEX_CORE="$TABS_DIR\\BepInEx\\core"
CSC="${CSC:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"

DIST_DIR="$SCRIPT_DIR/dist"
mkdir -p "$DIST_DIR"
DIST_DLL="$WIN_DIR\\dist\\TABSTrumpMod.dll"

"$CSC" \
  -nologo \
  -nostdlib \
  -noconfig \
  -target:library \
  -optimize+ \
  -out:"$DIST_DLL" \
  -r:"$MANAGED\\mscorlib.dll" \
  -r:"$MANAGED\\netstandard.dll" \
  -r:"$MANAGED\\System.dll" \
  -r:"$MANAGED\\System.Core.dll" \
  -r:"$MANAGED\\Assembly-CSharp.dll" \
  -r:"$MANAGED\\AstarPathfindingProject.dll" \
  -r:"$MANAGED\\InControl.dll" \
  -r:"$MANAGED\\Sirenix.Serialization.dll" \
  -r:"$MANAGED\\Sirenix.OdinInspector.Attributes.dll" \
  -r:"$MANAGED\\UnityEngine.dll" \
  -r:"$MANAGED\\UnityEngine.CoreModule.dll" \
  -r:"$MANAGED\\UnityEngine.PhysicsModule.dll" \
  -r:"$MANAGED\\UnityEngine.ParticleSystemModule.dll" \
  -r:"$MANAGED\\UnityEngine.ImageConversionModule.dll" \
  -r:"$MANAGED\\UnityEngine.InputLegacyModule.dll" \
  -r:"$MANAGED\\UnityEngine.AudioModule.dll" \
  -r:"$BEPINEX_CORE\\BepInEx.dll" \
  -r:"$BEPINEX_CORE\\0Harmony.dll" \
  -resource:"$WIN_DIR\\assets\\voices\\donald13.wav",TABSTrumpMod.voices.donald13.wav \
  -resource:"$WIN_DIR\\assets\\voices\\donald25.wav",TABSTrumpMod.voices.donald25.wav \
  "$WIN_DIR\\src\\TABSTrumpPlugin.cs" \
  "$WIN_DIR\\src\\TrumpAssetBuilder.cs" \
  "$WIN_DIR\\src\\TrumpWallAbility.cs"

echo "Built: $DIST_DIR/TABSTrumpMod.dll"

if [[ "${NO_DEPLOY:-0}" != "1" ]] && [[ -d "$(cygpath -u "$TABS_DIR")/BepInEx" ]]; then
  PLUGIN_DIR="$(cygpath -u "$TABS_DIR")/BepInEx/plugins/TABSTrumpMod"
  mkdir -p "$PLUGIN_DIR"
  cp -f "$DIST_DIR/TABSTrumpMod.dll" "$PLUGIN_DIR/TABSTrumpMod.dll"
  echo "Deployed: $PLUGIN_DIR/TABSTrumpMod.dll"
fi
