using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using DM;
using HarmonyLib;
using Landfall.TABS;
using Landfall.TABS.AI;
using Landfall.TABS.GameMode;
using Landfall.TABS.Workshop;
using UnityEngine;

namespace TABSTrumpMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class TABSTrumpPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.huazai.tabs.openalphatrump";
        public const string PluginName = "TABS Open Alpha Trump";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static UnitBlueprint TrumpBlueprint;
        internal static Faction OpenAlphaFaction;

        public static readonly DatabaseID TrumpUnitId = new DatabaseID(-1, 20161108);
        public static readonly DatabaseID OpenAlphaFactionId = new DatabaseID(-1, 20161109);

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);

            PatchContentDatabase();
            PatchColorPalette();
            PatchUnitBlueprintSpawn();
            PatchUnitApiMovement();
            PatchLocalizer();
            PatchCampaignUnlock();

            Log.LogInfo("TABS Open Alpha Trump mod initialized.");
        }

        private void PatchContentDatabase()
        {
            MethodInfo instanceMethod = typeof(ContentDatabase).GetMethod(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);

            if (instanceMethod != null)
            {
                MethodInfo postfix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnContentDatabaseInstance",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(instanceMethod, null, new HarmonyMethod(postfix));
                Log.LogInfo("Patched ContentDatabase.Instance.");
            }
            else
            {
                Log.LogError("Could not find ContentDatabase.Instance to patch.");
            }
        }

        private void PatchColorPalette()
        {
            MethodInfo initMethod = typeof(UnitEditorColorPalette).GetMethod(
                "Initialize",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (initMethod != null)
            {
                MethodInfo postfix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnColorPaletteInitialized",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(initMethod, null, new HarmonyMethod(postfix));
            }
        }

        private static void OnColorPaletteInitialized(UnitEditorColorPalette __instance)
        {
            if (__instance == null || TrumpBlueprint == null)
            {
                return;
            }

            try
            {
                TrumpAssetBuilder.ApplyPaletteColors(TrumpBlueprint, __instance);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Deferred palette color assignment skipped: " + ex.Message);
            }
        }

        private void PatchUnitBlueprintSpawn()
        {
            MethodInfo target = null;
            MethodInfo[] methods = typeof(UnitBlueprint).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "Spawn" && methods[i].GetParameters().Length == 9)
                {
                    target = methods[i];
                    break;
                }
            }

            if (target != null)
            {
                MethodInfo postfix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnUnitBlueprintSpawned",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(target, null, new HarmonyMethod(postfix));
            }
            else
            {
                Log.LogError("Could not find UnitBlueprint.Spawn(9 params) to patch.");
            }
        }

        private void PatchUnitApiMovement()
        {
            MethodInfo target = typeof(UnitAPI).GetMethod(
                "SetMovementSpeed",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (target != null)
            {
                MethodInfo postfix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnUnitSetMovementSpeed",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(target, null, new HarmonyMethod(postfix));
            }
            else
            {
                Log.LogWarning("Could not find UnitAPI.SetMovementSpeed to patch.");
            }
        }

        private static void OnUnitSetMovementSpeed(UnitAPI __instance)
        {
            if (__instance == null)
            {
                return;
            }

            TrumpWallAbility.TryOverrideMovementInput(__instance);
        }

        private void PatchLocalizer()
        {
            MethodInfo[] methods = typeof(Localizer).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "GetSinglePhrase" && methods[i].GetParameters().Length == 2)
                {
                    MethodInfo postfix = typeof(TABSTrumpPlugin).GetMethod(
                        "OnGetSinglePhrase",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    _harmony.Patch(methods[i], null, new HarmonyMethod(postfix));
                    break;
                }
            }
        }

        private static bool _registrationAttempted;

        private static void OnContentDatabaseInstance(ContentDatabase __result)
        {
            if (_registrationAttempted || TrumpBlueprint != null)
            {
                return;
            }

            if (__result == null || __result.LandfallContentDatabase == null)
            {
                return;
            }

            _registrationAttempted = true;
            try
            {
                Log.LogInfo("ContentDatabase ready; building Open Alpha Trump...");
                TrumpAssetBuilder.BuildAndRegister(__result.LandfallContentDatabase);
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to register Open Alpha Trump: " + ex);
            }
        }

        private static void OnUnitBlueprintSpawned(UnitBlueprint __instance, Unit unitToSpawn)
        {
            if (__instance == null || unitToSpawn == null)
            {
                return;
            }

            if (__instance == TrumpBlueprint ||
                (__instance.Entity != null && __instance.Entity.GUID == TrumpUnitId))
            {
                TrumpWallAbility ability = unitToSpawn.gameObject.GetComponent<TrumpWallAbility>();
                if (ability == null)
                {
                    ability = unitToSpawn.gameObject.AddComponent<TrumpWallAbility>();
                }
                ability.SuppressSpawnEffectsAndGlow();
            }
        }

        private static void OnGetSinglePhrase(string phraseID, ref string __result)
        {
            if (string.IsNullOrEmpty(phraseID))
            {
                return;
            }

            bool isChinese = IsChineseLanguage();

            if (phraseID == "UNIT_TRUMP_OPEN_ALPHA")
            {
                __result = isChinese ? "川普 (内测版)" : "Trump (Open Alpha)";
            }
            else if (phraseID == "UNIT_TRUMP_OPEN_ALPHA_DESC")
            {
                __result = isChinese
                    ? "2016 内测版复刻：双持突击步枪狂扫，并召唤高墙砸落阻挡敌军。"
                    : "2016 Open Alpha classic: Dual-wields assault rifles and drops a physical wall from the sky.";
            }
            else if (phraseID == "FACTION_OPEN_ALPHA")
            {
                __result = isChinese ? "内测版" : "Open Alpha";
            }
        }

        private void PatchCampaignUnlock()
        {
            MethodInfo getCurrentLevel = typeof(CampaignPlayerDataHolder).GetMethod(
                "GetCurrentLevel",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (getCurrentLevel != null)
            {
                MethodInfo postfix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnGetCurrentCampaignLevel",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(getCurrentLevel, null, new HarmonyMethod(postfix));
            }

            MethodInfo loadNewLevel = typeof(CampaignGameMode).GetMethod(
                "LoadNewLevel",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (loadNewLevel != null)
            {
                MethodInfo prefix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnCampaignLevelLoading",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(loadNewLevel, new HarmonyMethod(prefix), null);
            }

            MethodInfo applySceneData = typeof(CampaignHandler).GetMethod(
                "ApplySceneData",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (applySceneData != null)
            {
                MethodInfo prefix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnCampaignLevelLoading",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(applySceneData, new HarmonyMethod(prefix), null);
            }

            MethodInfo spawnFactionBar = typeof(PlacementFactionBar).GetMethod(
                "Spawn",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(Faction[]), typeof(bool) },
                null);
            if (spawnFactionBar != null)
            {
                MethodInfo prefix = typeof(TABSTrumpPlugin).GetMethod(
                    "OnPlacementFactionBarSpawn",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(spawnFactionBar, new HarmonyMethod(prefix), null);
            }

            Log.LogInfo("Patched campaign level faction restrictions for Open Alpha Trump.");
        }

        private static void OnGetCurrentCampaignLevel(TABSCampaignLevelAsset __result)
        {
            TrumpAssetBuilder.EnsureCampaignLevelAllowsTrump(__result);
        }

        private static void OnCampaignLevelLoading(TABSCampaignLevelAsset __0)
        {
            TrumpAssetBuilder.EnsureCampaignLevelAllowsTrump(__0);
        }

        private static void OnPlacementFactionBarSpawn(ref Faction[] __0)
        {
            if (CampaignPlayerDataHolder.CurrentGameModeState == GameModeState.Campaign)
            {
                __0 = TrumpAssetBuilder.EnsureOpenAlphaInFactionArray(__0);
            }
        }

        private static bool IsChineseLanguage()
        {
            try
            {
                FieldInfo localeField = AccessTools.Field(typeof(Localizer), "m_locale");
                if (localeField != null)
                {
                    Localizer.Language lang = (Localizer.Language)localeField.GetValue(null);
                    return lang == Localizer.Language.LANG_CH;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
