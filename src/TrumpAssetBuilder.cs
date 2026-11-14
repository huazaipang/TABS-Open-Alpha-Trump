using System;
using System.Collections.Generic;
using System.Reflection;
using DM;
using HarmonyLib;
using Landfall.TABS;
using Landfall.TABS.Workshop;
using UnityEngine;

namespace TABSTrumpMod
{
    internal static class TrumpAssetBuilder
    {
        internal static bool HasSuitProp;
        internal static bool HasTieProp;
        internal static bool HasHairProp;
        internal static GameObject WallImpactVfxTemplate;
        internal static GameObject WallSmokeVfxTemplate;

        private static GameObject _prefabContainer;
        private static readonly List<string> _propRoles = new List<string>();
        private static readonly int[] _suitSlotRoles = new int[4];

        public static void BuildAndRegister(LandfallContentDatabase db)
        {
            if (db == null)
            {
                return;
            }

            _prefabContainer = new GameObject("TABSTrumpMod_PrefabContainer");
            _prefabContainer.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_prefabContainer);

            UnitBlueprint template = FindTemplateBlueprint(db);
            if (template == null)
            {
                TABSTrumpPlugin.Log.LogError("No template UnitBlueprint found in LandfallContentDatabase.");
                return;
            }

            GameObject humanoidBase = FindUnitBase(db);
            GameObject riflePrefab = BuildDualRiflePrefab(db);
            CaptureOfficialVfxPrefabs(db);
            UnitEditorColorPalette palette = db.GetUnitEditorColorPalette();
            if (palette != null && !HasInitializedColors(palette))
            {
                try
                {
                    palette.Initialize();
                }
                catch (Exception ex)
                {
                    TABSTrumpPlugin.Log.LogWarning("Palette Initialize() during registration skipped: " + ex.Message);
                }
            }

            List<GameObject> propList = new List<GameObject>();
            List<PropItemData> propDataList = new List<PropItemData>();
            SelectTrumpProps(db, palette, propList, propDataList);

            UnitBlueprint trump = ScriptableObject.CreateInstance<UnitBlueprint>();
            FieldInfo bpEntityField = AccessTools.Field(typeof(UnitBlueprint), "m_entity");
            if (bpEntityField != null && bpEntityField.GetValue(trump) == null)
            {
                bpEntityField.SetValue(trump, new DatabaseEntity(WorkshopContentType.Unit));
            }

            trump.SetValues(template);
            trump.objectsToSpawnAsChildren = null;

            trump.Entity.GUID = TABSTrumpPlugin.TrumpUnitId;
            trump.Entity.Name = "UNIT_TRUMP_OPEN_ALPHA";
            trump.Entity.SetUnlockKey(string.Empty);

            Sprite trumpIcon = CreateTrumpPortraitSprite();
            if (trumpIcon != null)
            {
                trump.Entity.SetSpriteIcon(trumpIcon);
            }

            if (humanoidBase != null)
            {
                trump.UnitBase = humanoidBase;
            }

            trump.holdinigWithTwoHands = false;
            if (riflePrefab != null)
            {
                trump.RightWeapon = riflePrefab;
                trump.LeftWeapon = riflePrefab;
                trump.RightWeaponData = CreateWeaponColorData(palette);
                trump.LeftWeaponData = CreateWeaponColorData(palette);
            }

            trump.m_props = propList.ToArray();
            trump.m_propData = propDataList.ToArray();

            trump.health = 700f;
            trump.forceCost = 2000u;
            trump.useCustomCost = true;
            trump.customCost = 2000;
            trump.movementSpeedMuiltiplier = 1.15f;
            trump.sizeMultiplier = 1.05f;
            trump.minSizeRandom = 1.0f;
            trump.maxSizeRandom = 1.0f;
            trump.massMultiplier = 1.25f;
            trump.weaponSeparation = 0.08f;
            trump.attackSpeedMultiplier = 1.25f;
            trump.damageMultiplier = 2.15f;
            trump.rangeMultiplier = 1.0f;
            trump.UnitDescription = "UNIT_TRUMP_OPEN_ALPHA_DESC";

            VoiceBundle fallbackVoice = FindVoiceBundle(db);
            if (fallbackVoice != null)
            {
                trump.voiceBundle = fallbackVoice;
            }

            RegisterUnitBlueprint(db, trump);
            RegisterIntoFactions(db, trump);
            RegisterIntoCampaigns(db);
            ApplyPaletteColors(trump, palette);

            TABSTrumpPlugin.TrumpBlueprint = trump;
            TABSTrumpPlugin.Log.LogInfo("Registered Open Alpha Trump (cost 2000, health 700).");
        }

        internal static void ApplyPaletteColors(UnitBlueprint trump, UnitEditorColorPalette palette)
        {
            if (trump == null || !HasInitializedColors(palette))
            {
                return;
            }

            int gunmetal = FindClosestColorIndex(palette, new Color(0.14f, 0.15f, 0.17f));
            int suitBlack = FindClosestColorIndex(palette, new Color(0.05f, 0.05f, 0.06f));
            int shirtWhite = FindClosestColorIndex(palette, new Color(0.96f, 0.96f, 0.97f));
            int tieRed = FindClosestColorIndex(palette, new Color(0.84f, 0.08f, 0.08f));
            int hairBlond = FindClosestColorIndex(palette, new Color(0.93f, 0.77f, 0.26f));
            int shoeBlack = FindClosestColorIndex(palette, new Color(0.04f, 0.04f, 0.05f));

            if (trump.RightWeapon != null)
            {
                trump.RightWeaponData = BuildPropData(gunmetal, gunmetal, gunmetal, gunmetal);
            }
            if (trump.LeftWeapon != null)
            {
                trump.LeftWeaponData = BuildPropData(gunmetal, gunmetal, gunmetal, gunmetal);
            }

            if (trump.m_propData != null)
            {
                for (int i = 0; i < trump.m_propData.Length && i < _propRoles.Count; i++)
                {
                    string role = _propRoles[i];
                    if (role == "suit")
                    {
                        trump.m_propData[i] = BuildSuitPropData(suitBlack, shirtWhite, tieRed);
                    }
                    else if (role == "shirt")
                    {
                        trump.m_propData[i] = BuildPropData(shirtWhite, shirtWhite, shirtWhite, shirtWhite);
                    }
                    else if (role == "tie")
                    {
                        trump.m_propData[i] = BuildPropData(tieRed, tieRed, tieRed, tieRed);
                    }
                    else if (role == "pants")
                    {
                        trump.m_propData[i] = BuildPropData(suitBlack, suitBlack, shoeBlack, suitBlack);
                    }
                    else if (role == "shoes")
                    {
                        trump.m_propData[i] = BuildPropData(shoeBlack, shoeBlack, shoeBlack, shoeBlack);
                    }
                    else if (role == "hair")
                    {
                        trump.m_propData[i] = BuildPropData(hairBlond, hairBlond, hairBlond, hairBlond);
                    }
                }
            }

            TABSTrumpPlugin.Log.LogInfo("Applied UnitEditorColorPalette colors to Open Alpha Trump.");
        }

        private static UnitBlueprint FindTemplateBlueprint(LandfallContentDatabase db)
        {
            string[] preferredRanged = new string[]
            {
                "GUNSLINGER",
                "QUICKDRAW",
                "MUSKETEER",
                "DEADEYE",
                "BLUNDERBUSS",
                "FLINTLOCK",
                "ARCHER"
            };

            UnitBlueprint preferred = null;
            int bestPriority = int.MaxValue;
            UnitBlueprint anyRanged = null;
            UnitBlueprint fallback = null;

            foreach (UnitBlueprint bp in db.GetUnitBlueprints())
            {
                if (bp == null || bp.Entity == null || bp.UnitBase == null || bp.Mount != null)
                {
                    continue;
                }

                string name = bp.Entity.Name ?? string.Empty;
                bool hasRangedWeapon = bp.RightWeapon != null && bp.RightWeapon.GetComponentInChildren<RangeWeapon>(true) != null;

                if (hasRangedWeapon)
                {
                    if (anyRanged == null && !bp.IsSecret)
                    {
                        anyRanged = bp;
                    }

                    for (int i = 0; i < preferredRanged.Length; i++)
                    {
                        if (name.IndexOf(preferredRanged[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (i < bestPriority)
                            {
                                bestPriority = i;
                                preferred = bp;
                            }
                            break;
                        }
                    }
                }

                if (fallback == null && !bp.IsSecret)
                {
                    fallback = bp;
                }
            }

            UnitBlueprint chosen = preferred ?? anyRanged ?? fallback;
            if (chosen != null && chosen.Entity != null)
            {
                TABSTrumpPlugin.Log.LogInfo("Selected template UnitBlueprint: " + chosen.Entity.Name);
            }
            return chosen;
        }

        private static GameObject FindUnitBase(LandfallContentDatabase db)
        {
            GameObject fallback = null;
            foreach (GameObject ub in db.GetUnitBases())
            {
                if (ub == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = ub;
                }

                if (ub.name.IndexOf("Humanoid", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TABSTrumpPlugin.Log.LogInfo("Selected UnitBase: " + ub.name);
                    return ub;
                }
            }

            return fallback;
        }

        private static GameObject BuildDualRiflePrefab(LandfallContentDatabase db)
        {
            string[] exactPriority = new string[]
            {
                "M16",
                "AssaultRifle",
                "Assault_Rifle",
                "AR15",
                "AK47",
                "AK-47",
                "Carbine",
                "Thompson",
                "SMG",
                "Rifle"
            };

            GameObject matched = null;
            int bestPriority = int.MaxValue;
            List<string> rangedNames = new List<string>();

            foreach (GameObject weaponObj in db.GetWeapons())
            {
                if (weaponObj == null)
                {
                    continue;
                }

                RangeWeapon rw = weaponObj.GetComponentInChildren<RangeWeapon>(true);
                if (rw == null)
                {
                    continue;
                }

                if (rangedNames.Count < 40)
                {
                    rangedNames.Add(weaponObj.name);
                }

                for (int p = 0; p < exactPriority.Length; p++)
                {
                    if (weaponObj.name.IndexOf(exactPriority[p], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (p < bestPriority)
                        {
                            bestPriority = p;
                            matched = weaponObj;
                        }
                        break;
                    }
                }
            }

            TABSTrumpPlugin.Log.LogInfo("Sample ranged weapons: " + string.Join(", ", rangedNames.ToArray()));

            if (matched == null)
            {
                foreach (GameObject weaponObj in db.GetWeapons())
                {
                    if (weaponObj != null && weaponObj.GetComponentInChildren<RangeWeapon>(true) != null)
                    {
                        matched = weaponObj;
                        break;
                    }
                }
            }

            if (matched == null)
            {
                TABSTrumpPlugin.Log.LogWarning("No RangeWeapon found in database.");
                return null;
            }

            TABSTrumpPlugin.Log.LogInfo("Selected base rifle weapon: " + matched.name);

            GameObject clone = UnityEngine.Object.Instantiate(matched, _prefabContainer.transform);
            clone.name = "Trump_OpenAlpha_M16";
            clone.SetActive(true);

            Weapon[] weapons = clone.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                w.auto = true;
                w.internalCooldown = 0.095f;
                w.maxRange = 17.5f;
                w.maxAngle = 50f;
                w.startOnCooldown = false;
                w.attackSpeedM = 1.0f;
            }

            RangeWeapon[] rangeWeapons = clone.GetComponentsInChildren<RangeWeapon>(true);
            for (int i = 0; i < rangeWeapons.Length; i++)
            {
                RangeWeapon rw = rangeWeapons[i];
                rw.magSize = 36;
                rw.reloadTime = 1.55f;
                rw.spread = 2.5f;
                rw.extraSpreadInMelee = 4.2f;
                rw.shootRecoil = 0.14f;
                rw.torsoRecoil = 0.08f;
                if (rw.numberOfObjects < 1)
                {
                    rw.numberOfObjects = 1;
                }
            }

            return clone;
        }

        private static PropItemData CreateWeaponColorData(UnitEditorColorPalette palette)
        {
            PropItemData data = new PropItemData();
            int gunmetal = FindClosestColorIndex(palette, new Color(0.14f, 0.15f, 0.17f));
            data.m_colors = new int[] { gunmetal, gunmetal, gunmetal, gunmetal };
            data.m_isTeamColor = new bool[] { false, false, false, false };
            return data;
        }

        private static void SelectTrumpProps(
            LandfallContentDatabase db,
            UnitEditorColorPalette palette,
            List<GameObject> propList,
            List<PropItemData> propDataList)
        {
            _propRoles.Clear();
            List<GameObject> allProps = new List<GameObject>();

            foreach (GameObject prop in db.GetCharacterProps())
            {
                if (prop == null)
                {
                    continue;
                }

                allProps.Add(prop);
            }

            GameObject suitProp = PickBestProp(
                allProps,
                new string[] { "CP_Suit", "SuitJacket", "Suit_Jacket", "Tuxedo", "Blazer", "CP_Coat001", "CP_legacy_jacket001" },
                new string[] { "Pants", "Space", "Hazmat", "Swim", "Knight", "Pirate", "Viking", "Tribal", "Ancient", "Bee", "Fish", "Dino", "Banana", "Bear", "Clown", "HotDog", "Mummy", "Pickle", "Skeleton", "Track", "Wet", "Winter", "Lab", "Chemist", "Elvis", "Flakt", "Farmer", "Spider", "Vlad", "Astronaut" });

            GameObject shirtProp = PickBestProp(
                allProps,
                new string[] { "CP_Shirt001", "DressShirt", "Dress_Shirt", "Suit_Shirt", "Button_Shirt", "CP_legacy_shirt001" },
                new string[] { "Ancient", "Tribal", "Peasant", "Viking", "Pirate", "Medieval", "Asia", "Dynasty", "Crusader", "Barrel", "Blowdart", "Darkpeasant", "Flagbearer", "Pike", "Poacher", "Thor", "Superpeasant", "Evil", "Halloween", "Spooky", "Wrapping", "Tennis", "Holiday", "Karate" });

            GameObject tieProp = PickBestProp(
                allProps,
                new string[] { "CP_Tie001", "Necktie", "Neck_Tie" },
                new string[] { "Hair", "Bowtie", "Bow_Tie", "Belt", "Shoe", "Boot", "Bandana" });

            GameObject pantsProp = PickBestProp(
                allProps,
                new string[] { "CP_Pants001", "SuitPants", "Suit_Pants", "DressPants", "Dress_Pants", "Slacks", "Trousers", "CP_PolicePants001", "CP_legacy_pants001" },
                new string[] { "Space", "Ancient", "Tribal", "Viking", "Asia", "Dynasty", "Medieval", "Conan", "Pirate", "Evil", "Halloween", "Spooky", "Track", "Punk", "Gladiator", "Vigilante", "Cupid", "Dorky", "Cowboy", "Elvis", "Farmer", "Firefighter", "Karate", "Landsknecht", "Ottoman", "Wrapping", "Boxer", "Barrel", "Blowdart", "Darkpeasant", "Flagbearer", "Peasant", "Pike", "Poacher", "Superpeasant", "Thor" });

            GameObject shoesProp = PickBestProp(
                allProps,
                new string[] { "CP_Shoes001", "DressShoes", "Dress_Shoes", "Loafer", "CP_legacy_shoes001" },
                new string[] { "Ancient", "Tribal", "Viking", "Asia", "Medieval", "Pirate", "Evil", "Assassin", "Clown", "Football", "Sporty", "Halloween", "Spooky", "Crusader", "Farmer", "Boxer", "Darkpeasant", "Flagbearer", "Peasant", "Pharao", "Superboxer", "Thor" });

            GameObject hairProp = PickBestProp(
                allProps,
                new string[] { "CP_PompadourHair001", "CP_DadHair001", "CP_BoyBandHair001", "CP_legacy_hair001" },
                new string[] { "Facial", "Beard", "Mustache", "Helmet", "Hat" });

            int suitBlack = FindClosestColorIndex(palette, new Color(0.05f, 0.05f, 0.06f));
            int shirtWhite = FindClosestColorIndex(palette, new Color(0.96f, 0.96f, 0.97f));
            int tieRed = FindClosestColorIndex(palette, new Color(0.84f, 0.08f, 0.08f));
            int hairBlond = FindClosestColorIndex(palette, new Color(0.93f, 0.77f, 0.26f));
            int shoeBlack = FindClosestColorIndex(palette, new Color(0.04f, 0.04f, 0.05f));

            bool suitHasBuiltInShirt = false;
            bool suitHasBuiltInTie = false;

            if (suitProp != null)
            {
                HasSuitProp = true;
                InspectSuitMaterials(suitProp, out suitHasBuiltInShirt, out suitHasBuiltInTie);
                propList.Add(suitProp);
                propDataList.Add(BuildSuitPropData(suitBlack, shirtWhite, tieRed));
                _propRoles.Add("suit");
                TABSTrumpPlugin.Log.LogInfo("Selected suit prop: " + suitProp.name);
            }

            if (!suitHasBuiltInShirt && shirtProp != null && shirtProp != suitProp)
            {
                propList.Add(shirtProp);
                propDataList.Add(BuildPropData(shirtWhite, shirtWhite, shirtWhite, shirtWhite));
                _propRoles.Add("shirt");
                TABSTrumpPlugin.Log.LogInfo("Selected shirt prop: " + shirtProp.name);
            }

            bool tieAdded = false;
            if (!suitHasBuiltInTie && tieProp != null && tieProp != suitProp && tieProp != shirtProp)
            {
                tieAdded = true;
                propList.Add(tieProp);
                propDataList.Add(BuildPropData(tieRed, tieRed, tieRed, tieRed));
                _propRoles.Add("tie");
                TABSTrumpPlugin.Log.LogInfo("Selected tie prop: " + tieProp.name);
            }

            HasTieProp = suitHasBuiltInTie || tieAdded;

            if (pantsProp != null)
            {
                propList.Add(pantsProp);
                propDataList.Add(BuildPropData(suitBlack, suitBlack, shoeBlack, suitBlack));
                _propRoles.Add("pants");
                TABSTrumpPlugin.Log.LogInfo("Selected pants prop: " + pantsProp.name);
            }

            if (shoesProp != null)
            {
                propList.Add(shoesProp);
                propDataList.Add(BuildPropData(shoeBlack, shoeBlack, shoeBlack, shoeBlack));
                _propRoles.Add("shoes");
                TABSTrumpPlugin.Log.LogInfo("Selected shoes prop: " + shoesProp.name);
            }

            if (hairProp != null)
            {
                HasHairProp = true;
                propList.Add(hairProp);
                propDataList.Add(BuildPropData(hairBlond, hairBlond, hairBlond, hairBlond));
                _propRoles.Add("hair");
                TABSTrumpPlugin.Log.LogInfo("Selected hair prop: " + hairProp.name);
            }
        }

        private static GameObject PickBestProp(List<GameObject> props, string[] priorityPatterns, string[] excludePatterns)
        {
            for (int p = 0; p < priorityPatterns.Length; p++)
            {
                string pat = priorityPatterns[p];
                for (int i = 0; i < props.Count; i++)
                {
                    GameObject prop = props[i];
                    if (prop == null)
                    {
                        continue;
                    }

                    if (string.Equals(prop.name, pat, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop;
                    }
                }
            }

            GameObject best = null;
            int bestPriority = int.MaxValue;

            for (int i = 0; i < props.Count; i++)
            {
                GameObject prop = props[i];
                if (prop == null)
                {
                    continue;
                }

                string n = prop.name;
                if (excludePatterns != null && IsMatchAny(n, excludePatterns))
                {
                    continue;
                }

                for (int p = 0; p < priorityPatterns.Length; p++)
                {
                    if (n.IndexOf(priorityPatterns[p], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (p < bestPriority)
                        {
                            bestPriority = p;
                            best = prop;
                        }
                        break;
                    }
                }
            }

            return best;
        }

        internal static int GetSuitSlotRole(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < _suitSlotRoles.Length)
            {
                return _suitSlotRoles[slotIndex];
            }

            return 0;
        }

        private static void InspectSuitMaterials(GameObject suitProp, out bool hasBuiltInShirt, out bool hasBuiltInTie)
        {
            hasBuiltInShirt = false;
            hasBuiltInTie = false;
            for (int i = 0; i < _suitSlotRoles.Length; i++)
            {
                _suitSlotRoles[i] = 0;
            }

            if (suitProp == null)
            {
                return;
            }

            List<string> slotNames = new List<string>();
            CharacterItem item = suitProp.GetComponent<CharacterItem>();
            if (item != null)
            {
                CharacterItem.RendererMaterialWrapper[] wrappers = item.DefaultColors;
                if (wrappers == null || wrappers.Length == 0)
                {
                    wrappers = item.SharedMaterials;
                }

                if (wrappers != null && wrappers.Length > 0)
                {
                    for (int i = 0; i < wrappers.Length; i++)
                    {
                        string matName = wrappers[i] != null && wrappers[i].m_material != null
                            ? (wrappers[i].m_material.name ?? string.Empty)
                            : string.Empty;
                        slotNames.Add(matName);
                    }
                }
            }

            if (slotNames.Count == 0)
            {
                Renderer[] renderers = suitProp.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] == null || renderers[r].sharedMaterials == null || renderers[r].sharedMaterials.Length == 0)
                    {
                        continue;
                    }

                    Material[] mats = renderers[r].sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        slotNames.Add(mats[m] != null ? (mats[m].name ?? string.Empty) : string.Empty);
                    }
                    break;
                }
            }

            for (int i = 0; i < slotNames.Count && i < _suitSlotRoles.Length; i++)
            {
                int role = ClassifySuitMaterialName(slotNames[i]);
                _suitSlotRoles[i] = role;
                if (role == 1)
                {
                    hasBuiltInShirt = true;
                }
                else if (role == 2)
                {
                    hasBuiltInTie = true;
                }
            }

            TABSTrumpPlugin.Log.LogInfo(
                "Suit submesh slots (" + slotNames.Count + "): [" + string.Join(", ", slotNames.ToArray()) +
                "], builtInShirt=" + hasBuiltInShirt + ", builtInTie=" + hasBuiltInTie);
        }

        private static int ClassifySuitMaterialName(string matName)
        {
            if (string.IsNullOrEmpty(matName))
            {
                return 0;
            }

            if (matName.IndexOf("Shirt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                matName.IndexOf("White", StringComparison.OrdinalIgnoreCase) >= 0 ||
                matName.IndexOf("Collar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            if (matName.IndexOf("Tie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                matName.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            return 0;
        }

        private static PropItemData BuildSuitPropData(int suitBlack, int shirtWhite, int tieRed)
        {
            int[] cols = new int[4];
            for (int i = 0; i < 4; i++)
            {
                int role = _suitSlotRoles[i];
                if (role == 1)
                {
                    cols[i] = shirtWhite;
                }
                else if (role == 2)
                {
                    cols[i] = tieRed;
                }
                else
                {
                    cols[i] = suitBlack;
                }
            }

            return BuildPropData(cols[0], cols[1], cols[2], cols[3]);
        }

        private static PropItemData BuildPropData(int c0, int c1, int c2, int c3)
        {
            PropItemData data = new PropItemData();
            data.m_colors = new int[] { c0, c1, c2, c3 };
            data.m_isTeamColor = new bool[] { false, false, false, false };
            return data;
        }

        private static bool IsMatchAny(string source, string[] patterns)
        {
            for (int i = 0; i < patterns.Length; i++)
            {
                if (source.IndexOf(patterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly FieldInfo ColorsField = AccessTools.Field(typeof(UnitEditorColorPalette), "m_colors");

        private static bool HasInitializedColors(UnitEditorColorPalette palette)
        {
            if (palette == null || ColorsField == null)
            {
                return false;
            }

            try
            {
                ColorPaletteData[] colors = ColorsField.GetValue(palette) as ColorPaletteData[];
                return colors != null && colors.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static int FindClosestColorIndex(UnitEditorColorPalette palette, Color target)
        {
            if (palette == null || ColorsField == null)
            {
                return 0;
            }

            ColorPaletteData[] colors = null;
            try
            {
                colors = ColorsField.GetValue(palette) as ColorPaletteData[];
            }
            catch
            {
                colors = null;
            }

            if (colors == null || colors.Length == 0)
            {
                return 0;
            }

            int bestMatteIndex = -1;
            float bestMatteDist = float.MaxValue;
            int bestAnyIndex = 0;
            float bestAnyDist = float.MaxValue;

            for (int i = 0; i < colors.Length; i++)
            {
                Color c = colors[i].m_color;
                float dr = c.r - target.r;
                float dg = c.g - target.g;
                float db = c.b - target.b;
                float dist = dr * dr + dg * dg + db * db;

                if (dist < bestAnyDist)
                {
                    bestAnyDist = dist;
                    bestAnyIndex = i;
                }

                if (!IsReflectiveOrEmissiveMaterial(colors[i].m_material) && dist < bestMatteDist)
                {
                    bestMatteDist = dist;
                    bestMatteIndex = i;
                }
            }

            return bestMatteIndex >= 0 ? bestMatteIndex : bestAnyIndex;
        }

        private static bool IsReflectiveOrEmissiveMaterial(Material mat)
        {
            if (mat == null)
            {
                return true;
            }

            string name = mat.name ?? string.Empty;
            string shaderName = mat.shader != null ? (mat.shader.name ?? string.Empty) : string.Empty;
            string combined = name + " " + shaderName;

            string[] blockedTokens = new string[]
            {
                "Glow", "Neon", "Emiss", "Metal", "Gold", "Silver", "Chrome",
                "Holo", "Glass", "Clear", "Ice", "Fire", "Flame", "Water",
                "Additive", "Unlit", "Shiny", "Sparkle", "Team", "Fresnel",
                "Rim", "Highlight", "Outline", "Energy", "Plasma"
            };

            for (int i = 0; i < blockedTokens.Length; i++)
            {
                if (combined.IndexOf(blockedTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            if (mat.IsKeywordEnabled("_EMISSION"))
            {
                return true;
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                Color em = mat.GetColor("_EmissionColor");
                if (em.r > 0.04f || em.g > 0.04f || em.b > 0.04f)
                {
                    return true;
                }
            }

            if (mat.HasProperty("_Metallic") && mat.GetFloat("_Metallic") > 0.3f)
            {
                return true;
            }

            if (mat.HasProperty("_Glossiness") && mat.GetFloat("_Glossiness") > 0.55f)
            {
                return true;
            }

            return false;
        }

        private static void CaptureOfficialVfxPrefabs(LandfallContentDatabase db)
        {
            List<GameObject> candidates = new List<GameObject>();
            HashSet<int> seenIds = new HashSet<int>();

            CollectVfxCandidates(db.GetCombatMoves(), candidates, seenIds);
            CollectVfxCandidates(db.GetProjectiles(), candidates, seenIds);
            CollectVfxCandidates(db.GetWeapons(), candidates, seenIds);

            GameObject[] loadedObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            if (loadedObjects != null)
            {
                for (int i = 0; i < loadedObjects.Length; i++)
                {
                    GameObject go = loadedObjects[i];
                    if (go == null)
                    {
                        continue;
                    }

                    string n = go.name ?? string.Empty;
                    if (n.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Smoke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Dust", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Stomp", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AddCandidateWithSpawnRefs(go, candidates, seenIds);
                    }
                }
            }

            string[] impactPriorities = new string[]
            {
                "E_ExplosionSamuraiGiantStomp",
                "E_ExplosionMinotaurPillar",
                "E_ExplosionTreeGiant",
                "E_ExplosionGiant",
                "E_ExplosionBoulder",
                "E_ExplosionBombCannon",
                "Stomp",
                "Pillar",
                "Boulder",
                "E_Explosion"
            };

            string[] smokePriorities = new string[]
            {
                "M_Grenade_Smoke001",
                "W_SmokeGrenade001",
                "SM_Particle_Smoke_01",
                "SmokeGrenade",
                "Particle_Smoke",
                "Dust",
                "Smoke"
            };

            string[] excludedTokens = new string[]
            {
                "Laser", "Lightning", "Void", "Magic", "Neon", "DarkP", "Super",
                "Firework", "Confetti", "Heart", "Cupid", "Halloween", "Spooky",
                "Snow", "Ice", "Water", "Poison", "Heal", "Vampire", "Banshee",
                "Wizard", "Witch", "Necro", "Plasma", "Solar", "Present"
            };

            GameObject impactSrc = PickBestVfx(candidates, impactPriorities, excludedTokens);
            GameObject smokeSrc = PickBestVfx(candidates, smokePriorities, excludedTokens);

            if (impactSrc == null)
            {
                impactSrc = smokeSrc;
            }
            if (smokeSrc == null)
            {
                smokeSrc = impactSrc;
            }

            if (impactSrc != null)
            {
                WallImpactVfxTemplate = CreateSanitizedVfxTemplate(impactSrc, "TrumpOfficialImpactVfx");
                TABSTrumpPlugin.Log.LogInfo("Captured official impact VFX: " + impactSrc.name);
            }
            else
            {
                TABSTrumpPlugin.Log.LogWarning("No official impact VFX found; falling back to billboard dust.");
            }

            if (smokeSrc != null)
            {
                WallSmokeVfxTemplate = CreateSanitizedVfxTemplate(smokeSrc, "TrumpOfficialSmokeVfx");
                TABSTrumpPlugin.Log.LogInfo("Captured official smoke VFX: " + smokeSrc.name);
            }
            else
            {
                TABSTrumpPlugin.Log.LogWarning("No official smoke VFX found; falling back to billboard dust.");
            }
        }

        private static void CollectVfxCandidates(IEnumerable<GameObject> source, List<GameObject> candidates, HashSet<int> seenIds)
        {
            if (source == null)
            {
                return;
            }

            foreach (GameObject go in source)
            {
                AddCandidateWithSpawnRefs(go, candidates, seenIds);
            }
        }

        private static void AddCandidateWithSpawnRefs(GameObject go, List<GameObject> candidates, HashSet<int> seenIds)
        {
            if (go == null)
            {
                return;
            }

            int id = go.GetInstanceID();
            if (!seenIds.Add(id))
            {
                return;
            }

            if (go.GetComponentInChildren<ParticleSystem>(true) != null)
            {
                candidates.Add(go);
            }

            SpawnObject[] spawners = go.GetComponentsInChildren<SpawnObject>(true);
            for (int i = 0; i < spawners.Length; i++)
            {
                if (spawners[i] != null && spawners[i].objectToSpawn != null)
                {
                    AddCandidateWithSpawnRefs(spawners[i].objectToSpawn, candidates, seenIds);
                }
            }

            MonoBehaviour[] behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour mb = behaviours[i];
                if (mb == null)
                {
                    continue;
                }

                Type t = mb.GetType();
                FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int f = 0; f < fields.Length; f++)
                {
                    if (fields[f].FieldType == typeof(GameObject))
                    {
                        GameObject refObj = fields[f].GetValue(mb) as GameObject;
                        if (refObj != null)
                        {
                            AddCandidateWithSpawnRefs(refObj, candidates, seenIds);
                        }
                    }
                    else if (fields[f].FieldType == typeof(GameObject[]))
                    {
                        GameObject[] refArr = fields[f].GetValue(mb) as GameObject[];
                        if (refArr != null)
                        {
                            for (int k = 0; k < refArr.Length; k++)
                            {
                                if (refArr[k] != null)
                                {
                                    AddCandidateWithSpawnRefs(refArr[k], candidates, seenIds);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static GameObject PickBestVfx(List<GameObject> candidates, string[] priorityPatterns, string[] excludePatterns)
        {
            GameObject best = null;
            int bestPriority = int.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject go = candidates[i];
                if (go == null || go.GetComponentInChildren<ParticleSystem>(true) == null)
                {
                    continue;
                }

                string n = go.name ?? string.Empty;
                if (excludePatterns != null && IsMatchAny(n, excludePatterns))
                {
                    continue;
                }

                for (int p = 0; p < priorityPatterns.Length; p++)
                {
                    if (n.IndexOf(priorityPatterns[p], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (p < bestPriority)
                        {
                            bestPriority = p;
                            best = go;
                        }
                        break;
                    }
                }
            }

            return best;
        }

        private static GameObject CreateSanitizedVfxTemplate(GameObject source, string templateName)
        {
            GameObject clone = UnityEngine.Object.Instantiate(source, _prefabContainer.transform);
            clone.name = templateName;
            clone.SetActive(true);

            Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(colliders[i]);
                }
            }

            Rigidbody[] bodies = clone.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(bodies[i]);
                }
            }

            AudioSource[] audioSources = clone.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(audioSources[i]);
                }
            }

            Light[] lights = clone.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(lights[i]);
                }
            }

            MeshRenderer[] meshRenderers = clone.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                if (meshRenderers[i] != null)
                {
                    meshRenderers[i].enabled = false;
                }
            }

            Component[] allComponents = clone.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < allComponents.Length; i++)
            {
                Component c = allComponents[i];
                if (c == null || c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is MeshFilter || c is MeshRenderer)
                {
                    continue;
                }

                string typeName = c.GetType().Name;
                if (typeName.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("SpawnObject", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("DelayEvent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("CollisionSound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Sound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Shake", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Force", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Shockwave", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("RemoveAfter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Destroy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Pool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }

            ParticleSystem[] systems = clone.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = ps.main;
                main.playOnAwake = true;
                main.loop = false;
                main.stopAction = ParticleSystemStopAction.None;
            }

            return clone;
        }

        private static VoiceBundle FindVoiceBundle(LandfallContentDatabase db)
        {
            string[] preferred = new string[] { "King", "Captain", "Pharaoh", "Zeus", "Jarl", "Knight", "Viking", "Sultan", "General" };
            string[] excluded = new string[] { "Halfling", "Peasant", "Archer", "Bard", "Squire", "Witch", "Banshee", "Balloon", "Shouter", "Cheer" };

            VoiceBundle best = null;
            int bestRank = int.MaxValue;
            VoiceBundle fallback = null;

            foreach (VoiceBundle vb in db.GetVoiceBundles())
            {
                if (vb == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = vb;
                }

                string name = (vb.Entity != null ? vb.Entity.Name : null) ?? vb.name ?? string.Empty;
                if (IsMatchAny(name, excluded))
                {
                    continue;
                }

                for (int p = 0; p < preferred.Length; p++)
                {
                    if (name.IndexOf(preferred[p], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (p < bestRank)
                        {
                            bestRank = p;
                            best = vb;
                        }
                        break;
                    }
                }
            }

            VoiceBundle chosen = best ?? fallback;
            if (chosen != null)
            {
                string chosenName = (chosen.Entity != null ? chosen.Entity.Name : null) ?? chosen.name ?? "unknown";
                TABSTrumpPlugin.Log.LogInfo("Selected fallback VoiceBundle: " + chosenName);
            }
            return chosen;
        }

        private static void RegisterUnitBlueprint(LandfallContentDatabase db, UnitBlueprint trump)
        {
            FieldInfo dictField = AccessTools.Field(typeof(LandfallContentDatabase), "m_unitBlueprints");
            Dictionary<DatabaseID, UnitBlueprint> dict =
                dictField != null ? dictField.GetValue(db) as Dictionary<DatabaseID, UnitBlueprint> : null;

            if (dict != null)
            {
                dict[trump.Entity.GUID] = trump;
            }
        }

        private static void RegisterIntoFactions(LandfallContentDatabase db, UnitBlueprint trump)
        {
            Faction legacyFaction = null;
            foreach (Faction f in db.GetFactions())
            {
                if (f == null || f.Entity == null)
                {
                    continue;
                }

                string fname = f.Entity.Name ?? string.Empty;
                if (fname.IndexOf("LEGACY", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    legacyFaction = f;
                    break;
                }
            }

            if (legacyFaction != null)
            {
                AddUnitToFaction(legacyFaction, trump);
                TABSTrumpPlugin.Log.LogInfo("Added Open Alpha Trump to faction: " + legacyFaction.Entity.Name);
            }

            Faction openAlpha = ScriptableObject.CreateInstance<Faction>();
            openAlpha.Init(TABSTrumpPlugin.OpenAlphaFactionId);
            openAlpha.Entity.Name = "FACTION_OPEN_ALPHA";
            openAlpha.m_displayFaction = true;
            openAlpha.m_FactionCatagory = Faction.FactionCatagory.Landfall;
            openAlpha.index = 99;
            openAlpha.m_FactionColor = new Color(0.86f, 0.65f, 0.18f, 1f);
            openAlpha.Units = new UnitBlueprint[] { trump };

            Sprite factionIcon = CreateTrumpPortraitSprite();
            if (factionIcon != null)
            {
                openAlpha.Entity.SetSpriteIcon(factionIcon);
            }

            FieldInfo factionsField = AccessTools.Field(typeof(LandfallContentDatabase), "m_factions");
            Dictionary<DatabaseID, Faction> factionsDict =
                factionsField != null ? factionsField.GetValue(db) as Dictionary<DatabaseID, Faction> : null;
            if (factionsDict != null)
            {
                factionsDict[TABSTrumpPlugin.OpenAlphaFactionId] = openAlpha;
            }

            FieldInfo hotbarField = AccessTools.Field(typeof(LandfallContentDatabase), "m_defaultHotbarFactionIds");
            List<DatabaseID> hotbar = hotbarField != null ? hotbarField.GetValue(db) as List<DatabaseID> : null;
            if (hotbar != null && !hotbar.Contains(TABSTrumpPlugin.OpenAlphaFactionId))
            {
                hotbar.Add(TABSTrumpPlugin.OpenAlphaFactionId);
            }

            TABSTrumpPlugin.OpenAlphaFaction = openAlpha;
        }

        private static void RegisterIntoCampaigns(LandfallContentDatabase db)
        {
            if (db == null || TABSTrumpPlugin.OpenAlphaFaction == null)
            {
                return;
            }

            int updatedLevels = 0;
            IEnumerable<TABSCampaignLevelAsset> levels = db.GetCampaignLevels();
            if (levels != null)
            {
                foreach (TABSCampaignLevelAsset level in levels)
                {
                    if (EnsureCampaignLevelAllowsTrump(level))
                    {
                        updatedLevels++;
                    }
                }
            }

            TABSTrumpPlugin.Log.LogInfo("Unlocked Open Alpha Trump across " + updatedLevels + " campaign levels.");
        }

        internal static bool EnsureCampaignLevelAllowsTrump(TABSCampaignLevelAsset level)
        {
            if (level == null || TABSTrumpPlugin.OpenAlphaFaction == null)
            {
                return false;
            }

            bool changed = false;

            if (level.AllowedFactions != null && level.AllowedFactions.Length > 0)
            {
                bool alreadyPresent = false;
                for (int i = 0; i < level.AllowedFactions.Length; i++)
                {
                    Faction f = level.AllowedFactions[i];
                    if (f == TABSTrumpPlugin.OpenAlphaFaction ||
                        (f != null && f.Entity != null && f.Entity.GUID == TABSTrumpPlugin.OpenAlphaFactionId))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    Faction[] updated = new Faction[level.AllowedFactions.Length + 1];
                    Array.Copy(level.AllowedFactions, updated, level.AllowedFactions.Length);
                    updated[level.AllowedFactions.Length] = TABSTrumpPlugin.OpenAlphaFaction;
                    level.AllowedFactions = updated;
                    changed = true;
                }
            }

            return changed;
        }

        internal static Faction[] EnsureOpenAlphaInFactionArray(Faction[] factions)
        {
            if (TABSTrumpPlugin.OpenAlphaFaction == null)
            {
                return factions;
            }

            if (factions == null || factions.Length == 0)
            {
                return new Faction[] { TABSTrumpPlugin.OpenAlphaFaction };
            }

            for (int i = 0; i < factions.Length; i++)
            {
                Faction f = factions[i];
                if (f == TABSTrumpPlugin.OpenAlphaFaction ||
                    (f != null && f.Entity != null && f.Entity.GUID == TABSTrumpPlugin.OpenAlphaFactionId))
                {
                    return factions;
                }
            }

            Faction[] updated = new Faction[factions.Length + 1];
            Array.Copy(factions, updated, factions.Length);
            updated[factions.Length] = TABSTrumpPlugin.OpenAlphaFaction;
            return updated;
        }

        private static void AddUnitToFaction(Faction faction, UnitBlueprint unit)
        {
            UnitBlueprint[] existing = faction.Units ?? new UnitBlueprint[0];
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == unit)
                {
                    return;
                }
            }

            UnitBlueprint[] updated = new UnitBlueprint[existing.Length + 1];
            Array.Copy(existing, updated, existing.Length);
            updated[existing.Length] = unit;
            faction.Units = updated;
        }

        private static Sprite CreateTrumpPortraitSprite()
        {
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color bgTop = new Color(0.16f, 0.25f, 0.45f, 1f);
            Color bgBottom = new Color(0.32f, 0.44f, 0.68f, 1f);
            Color suit = new Color(0.05f, 0.05f, 0.06f, 1f);
            Color shirt = new Color(0.95f, 0.95f, 0.96f, 1f);
            Color tie = new Color(0.84f, 0.10f, 0.10f, 1f);
            Color skin = new Color(0.95f, 0.77f, 0.62f, 1f);
            Color hair = new Color(0.95f, 0.79f, 0.24f, 1f);
            Color hairShadow = new Color(0.82f, 0.64f, 0.16f, 1f);
            Color eye = new Color(0.10f, 0.10f, 0.12f, 1f);

            for (int y = 0; y < size; y++)
            {
                float t = (float)y / (size - 1);
                Color bg = Color.Lerp(bgBottom, bgTop, t);
                for (int x = 0; x < size; x++)
                {
                    Color c = bg;

                    // Shoulders and dark suit
                    if (y < 24 && x >= 10 && x <= 53)
                    {
                        float shoulderSlope = (24 - y) * 0.4f;
                        if (Mathf.Abs(x - 31.5f) <= 13f + shoulderSlope)
                        {
                            c = suit;
                        }
                    }

                    // White shirt V-neck
                    if (y >= 4 && y <= 25)
                    {
                        float halfV = (y - 3) * 0.36f;
                        if (Mathf.Abs(x - 31.5f) <= halfV)
                        {
                            c = shirt;
                        }
                    }

                    // Red tie
                    if (y >= 0 && y <= 23 && Mathf.Abs(x - 31.5f) <= 2.4f)
                    {
                        c = tie;
                    }

                    // Head (Wobbler rounded face)
                    float dx = (x - 31.5f) / 11.5f;
                    float dy = (y - 38.0f) / 13.0f;
                    if (dx * dx + dy * dy <= 1.0f)
                    {
                        c = skin;
                    }

                    // Wobbler eyes
                    if ((Mathf.Abs(x - 27) <= 1 && Mathf.Abs(y - 37) <= 1) ||
                        (Mathf.Abs(x - 36) <= 1 && Mathf.Abs(y - 37) <= 1))
                    {
                        c = eye;
                    }

                    // Swept golden hair
                    float hdx = (x - 32.5f) / 14.0f;
                    float hdy = (y - 49.0f) / 7.0f;
                    if (hdx * hdx + hdy * hdy <= 1.0f && y >= 43)
                    {
                        c = (y > 51 || x < 34) ? hair : hairShadow;
                    }

                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            UnityEngine.Object.DontDestroyOnLoad(tex);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            return sprite;
        }
    }
}
