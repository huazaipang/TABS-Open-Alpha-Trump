using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Landfall.TABS;
using Landfall.TABS.AI;
using Landfall.TABS.GameState;
using Landfall.TABS_Input;
using Pathfinding;
using UnityEngine;

namespace TABSTrumpMod
{
    public class TrumpWallAbility : MonoBehaviour
    {
        private const float AiInitialDelay = 2.0f;
        private const float AiCooldown = 8.5f;
        private const float PossessCooldown = 5.0f;
        private const float MaxWallCastRange = 19.0f;
        private const float WallSpawnOffsetClamp = 12.0f;
        private const float WallTelegraphDuration = 0.32f;
        private const float WallSpawnHeight = 22.0f;
        private const float WallWidth = 14.0f;
        private const float WallHeight = 5.8f;
        private const float WallThickness = 0.54f;
        private const float WallImpactDamage = 320f;
        private const float WallLifetime = 12.0f;

        private Unit _unit;
        private UnitAPI _unitApi;
        private GameStateManager _gameStateManager;
        private CameraAbilityPossess _possessCamera;
        private AudioSource _voiceSource;
        private float _cooldownTimer;
        private float _battleEntryVoiceDelay = -1f;
        private float _chatterTimer;
        private int _nextClipIndex;
        private bool _wasInBattle;
        private bool _appliedFallbackVisuals;
        private static bool _loggedSpawnDiagnostics;
        private static bool _loggedOutfitDiagnostics;

        private float _strafeSign = 1f;
        private float _strafeSwitchTimer = 2.0f;
        private float _strafePhase;
        private bool _hasDesiredCombatMove;
        private Vector3 _desiredLocalMove;

        private static readonly Dictionary<int, TrumpWallAbility> _apiMap = new Dictionary<int, TrumpWallAbility>();

        private static AudioClip[] _openAlphaVoiceClips;
        private static bool _voiceClipsLoaded;
        private static float _nextGlobalVoiceUnscaledTime;

        private static Mesh _brickBoxMesh;
        private static Texture2D _brickTexture;
        private static Texture2D _softDustTexture;
        private static Material _dustParticleMaterial;
        private static Material _telegraphMaterial;
        private static Material _telegraphBeamMaterial;
        private static Material _brickBodyMaterial;
        private static Material _concreteMaterial;
        private static Material _metalMaterial;
        private static string _cachedImpactSoundKey;
        private static bool _soundKeySearched;
        private static int _cachedWallLayer = -1;
        private static readonly FieldInfo RaycastMaskField = AccessTools.Field(typeof(UnitAPI), "m_raycastMask");
        private static readonly FieldInfo DontMoveForField = AccessTools.Field(typeof(DataHandler), "dontMoveFor");

        private void Awake()
        {
            _unit = GetComponent<Unit>();
            _unitApi = GetComponent<UnitAPI>();
            _cooldownTimer = AiInitialDelay;
            _strafeSign = UnityEngine.Random.value >= 0.5f ? 1f : -1f;
            _strafeSwitchTimer = UnityEngine.Random.Range(1.6f, 2.8f);
            _strafePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _chatterTimer = UnityEngine.Random.Range(8.5f, 13.5f);
            _nextClipIndex = UnityEngine.Random.Range(0, 2);
        }

        private void Start()
        {
            _gameStateManager = ServiceLocator.GetService<GameStateManager>();
            _possessCamera = UnityEngine.Object.FindObjectOfType<CameraAbilityPossess>();
            if (_unitApi == null)
            {
                _unitApi = GetComponent<UnitAPI>();
            }
            if (_unitApi != null)
            {
                _apiMap[_unitApi.GetInstanceID()] = this;
            }

            EnsureOpenAlphaVoiceClips();
            SetupVoiceAudioSource();
            EnsureFallbackVisuals();
            StartCoroutine(EnforceMatteSuitRoutine());
        }

        private void OnDestroy()
        {
            if (_unitApi != null)
            {
                _apiMap.Remove(_unitApi.GetInstanceID());
            }
        }

        internal static void TryOverrideMovementInput(UnitAPI api)
        {
            if (api == null)
            {
                return;
            }

            TrumpWallAbility ability;
            if (!_apiMap.TryGetValue(api.GetInstanceID(), out ability) || ability == null)
            {
                return;
            }

            if (ability._hasDesiredCombatMove &&
                ability._unit != null &&
                !ability._unit.dead &&
                ability._unit.data != null &&
                ability._unit.data.input != null)
            {
                ability._unit.data.input.inputDirection = ability._desiredLocalMove;
                ClearDontMoveFor(ability._unit.data);
            }
        }

        private void FixedUpdate()
        {
            if (_hasDesiredCombatMove &&
                _unit != null &&
                !_unit.dead &&
                _unit.data != null &&
                _unit.data.input != null)
            {
                _unit.data.input.inputDirection = _desiredLocalMove;
                ClearDontMoveFor(_unit.data);
            }
        }

        private static void ClearDontMoveFor(DataHandler data)
        {
            if (data != null && DontMoveForField != null)
            {
                DontMoveForField.SetValue(data, 0f);
            }
        }

        private void Update()
        {
            if (_voiceSource != null && _voiceSource.isPlaying)
            {
                _voiceSource.pitch = Mathf.Clamp(Time.timeScale, 0.25f, 1.5f);
            }

            if (_unit == null || _unit.dead)
            {
                _hasDesiredCombatMove = false;
                if (_voiceSource != null && _voiceSource.isPlaying)
                {
                    _voiceSource.Stop();
                }
                return;
            }

            if (_gameStateManager == null)
            {
                _gameStateManager = ServiceLocator.GetService<GameStateManager>();
            }

            if (_gameStateManager == null || _gameStateManager.GameState != GameState.BattleState)
            {
                _wasInBattle = false;
                _hasDesiredCombatMove = false;
                return;
            }

            if (!_wasInBattle)
            {
                _wasInBattle = true;
                _battleEntryVoiceDelay = UnityEngine.Random.Range(0.25f, 0.65f);
            }

            if (_battleEntryVoiceDelay >= 0f)
            {
                _battleEntryVoiceDelay -= Time.deltaTime;
                if (_battleEntryVoiceDelay <= 0f)
                {
                    _battleEntryVoiceDelay = -1f;
                    PlayTrumpVoice(false);
                }
            }

            _chatterTimer -= Time.deltaTime;
            if (_chatterTimer <= 0f)
            {
                _chatterTimer = UnityEngine.Random.Range(10.5f, 16.0f);
                if (_unit.data != null && _unit.data.targetMainRig != null)
                {
                    PlayTrumpVoice(false);
                }
            }

            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
            }

            bool isPossessed = IsCurrentlyPossessed();

            if (isPossessed)
            {
                _hasDesiredCombatMove = false;
                if (_cooldownTimer <= 0f && IsPossessAbilityPressed())
                {
                    Vector3 targetPos = GetPossessTargetPosition();
                    TriggerWallCast(targetPos);
                    _cooldownTimer = PossessCooldown;
                }
                return;
            }

            UpdateCombatLocomotion();

            if (_cooldownTimer <= 0f && _unit.data != null && _unit.data.targetMainRig != null)
            {
                if (_unit.data.distanceToTarget <= MaxWallCastRange)
                {
                    TriggerWallCast(_unit.data.targetMainRig.position);
                    _cooldownTimer = AiCooldown;
                }
            }
        }

        private void UpdateCombatLocomotion()
        {
            if (_unit == null || _unit.data == null || _unit.data.targetMainRig == null || _unit.data.input == null)
            {
                _hasDesiredCombatMove = false;
                return;
            }

            Vector3 selfPos = _unit.data.mainRig != null ? _unit.data.mainRig.position : transform.position;
            Vector3 toTarget = _unit.data.targetMainRig.position - selfPos;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist < 0.05f || dist > 19.5f)
            {
                _hasDesiredCombatMove = false;
                return;
            }

            Vector3 forwardDir = toTarget / dist;
            Vector3 rightDir = new Vector3(forwardDir.z, 0f, -forwardDir.x);

            _strafePhase += Time.deltaTime * 2.7f;
            _strafeSwitchTimer -= Time.deltaTime;
            if (_strafeSwitchTimer <= 0f)
            {
                _strafeSign = -_strafeSign;
                _strafeSwitchTimer = UnityEngine.Random.Range(1.7f, 3.1f);
            }
            else
            {
                RaycastHit obstacleHit;
                int wallMask = GetWallObstacleLayerMaskForGround();
                if (Physics.SphereCast(selfPos + Vector3.up * 0.9f, 0.35f, rightDir * _strafeSign, out obstacleHit, 2.2f, wallMask))
                {
                    _strafeSign = -_strafeSign;
                    _strafeSwitchTimer = UnityEngine.Random.Range(1.5f, 2.6f);
                }
            }

            Vector3 desiredWorldDir;
            if (dist < 7.5f)
            {
                desiredWorldDir = -forwardDir * 0.85f + rightDir * (_strafeSign * 0.70f);
            }
            else if (dist < 12.5f)
            {
                float pulse = 0.22f * Mathf.Sin(_strafePhase);
                desiredWorldDir = forwardDir * (-0.20f + pulse) + rightDir * (_strafeSign * 0.92f);
            }
            else
            {
                float advancePulse = 0.28f + 0.22f * Mathf.Sin(_strafePhase);
                desiredWorldDir = forwardDir * advancePulse + rightDir * (_strafeSign * 0.88f);
            }

            if (desiredWorldDir.sqrMagnitude < 0.001f)
            {
                _hasDesiredCombatMove = false;
                return;
            }

            desiredWorldDir.Normalize();
            Transform refTransform = _unit.data.input.Direction != null ? _unit.data.input.Direction : transform;
            Vector3 localDir = refTransform.InverseTransformDirection(desiredWorldDir);
            localDir.y = 0f;
            if (localDir.sqrMagnitude > 0.001f)
            {
                localDir.Normalize();
            }

            _desiredLocalMove = localDir;
            _hasDesiredCombatMove = true;
            _unit.data.input.inputDirection = localDir;
            ClearDontMoveFor(_unit.data);
        }

        private void SetupVoiceAudioSource()
        {
            if (_voiceSource != null)
            {
                return;
            }

            Transform anchor = (_unit != null && _unit.data != null && _unit.data.head != null)
                ? _unit.data.head
                : ((_unit != null && _unit.data != null && _unit.data.torso != null) ? _unit.data.torso : transform);

            _voiceSource = anchor.gameObject.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.loop = false;
            _voiceSource.spatialBlend = 0.68f;
            _voiceSource.minDistance = 8f;
            _voiceSource.maxDistance = 68f;
            _voiceSource.rolloffMode = AudioRolloffMode.Logarithmic;
            _voiceSource.volume = 0.95f;
        }

        private void PlayTrumpVoice(bool forceOnWallCast)
        {
            EnsureOpenAlphaVoiceClips();

            if (_openAlphaVoiceClips != null && _openAlphaVoiceClips.Length > 0 && _voiceSource != null)
            {
                if (_voiceSource.isPlaying)
                {
                    if (!forceOnWallCast)
                    {
                        return;
                    }
                }

                float now = Time.unscaledTime;
                if (!forceOnWallCast && now < _nextGlobalVoiceUnscaledTime)
                {
                    return;
                }
                if (forceOnWallCast && now < _nextGlobalVoiceUnscaledTime - 0.35f)
                {
                    return;
                }

                AudioClip clip = _openAlphaVoiceClips[_nextClipIndex % _openAlphaVoiceClips.Length];
                _nextClipIndex = (_nextClipIndex + 1) % _openAlphaVoiceClips.Length;
                if (clip != null)
                {
                    _voiceSource.pitch = Mathf.Clamp(Time.timeScale, 0.25f, 1.5f);
                    _voiceSource.clip = clip;
                    _voiceSource.Play();
                    _nextGlobalVoiceUnscaledTime = now + (forceOnWallCast ? 0.85f : 1.45f);
                    return;
                }
            }

            try
            {
                UnitSounds us = _unit != null ? _unit.GetComponentInChildren<UnitSounds>(true) : null;
                if (us != null)
                {
                    Vector3 pos = _unit.data != null && _unit.data.head != null ? _unit.data.head.position : transform.position;
                    us.PlayVocalSound(0, 1.0f, pos);
                }
            }
            catch
            {
            }
        }

        private static void EnsureOpenAlphaVoiceClips()
        {
            if (_voiceClipsLoaded)
            {
                return;
            }
            _voiceClipsLoaded = true;

            List<AudioClip> clips = new List<AudioClip>();
            string[] resources = new string[]
            {
                "TABSTrumpMod.voices.donald13.wav",
                "TABSTrumpMod.voices.donald25.wav"
            };

            for (int i = 0; i < resources.Length; i++)
            {
                AudioClip clip = LoadWavFromEmbeddedResource(resources[i], "TrumpOpenAlpha_" + i);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            _openAlphaVoiceClips = clips.ToArray();
            TABSTrumpPlugin.Log.LogInfo("Loaded Open Alpha Trump voice clips: " + _openAlphaVoiceClips.Length);
        }

        private static AudioClip LoadWavFromEmbeddedResource(string resourceName, string clipName)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        if (stream.Length < 44)
                        {
                            return null;
                        }

                        string riff = new string(reader.ReadChars(4));
                        reader.ReadInt32();
                        string wave = new string(reader.ReadChars(4));
                        if (riff != "RIFF" || wave != "WAVE")
                        {
                            return null;
                        }

                        int channels = 1;
                        int sampleRate = 44100;
                        int bitsPerSample = 16;
                        byte[] pcmBytes = null;

                        while (stream.Position + 8 <= stream.Length)
                        {
                            string chunkId = new string(reader.ReadChars(4));
                            int chunkSize = reader.ReadInt32();
                            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                            {
                                break;
                            }

                            if (chunkId == "fmt ")
                            {
                                long chunkStart = stream.Position;
                                int audioFormat = reader.ReadInt16();
                                channels = reader.ReadInt16();
                                sampleRate = reader.ReadInt32();
                                reader.ReadInt32();
                                reader.ReadInt16();
                                bitsPerSample = reader.ReadInt16();
                                if (audioFormat != 1 || bitsPerSample != 16)
                                {
                                    return null;
                                }
                                stream.Position = chunkStart + chunkSize;
                            }
                            else if (chunkId == "data")
                            {
                                pcmBytes = reader.ReadBytes(chunkSize);
                                break;
                            }
                            else
                            {
                                stream.Position += chunkSize;
                            }

                            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                            {
                                stream.Position += 1;
                            }
                        }

                        if (pcmBytes == null || pcmBytes.Length < 2 || channels <= 0 || sampleRate <= 0)
                        {
                            return null;
                        }

                        int sampleCount = pcmBytes.Length / 2;
                        float[] samples = new float[sampleCount];
                        for (int i = 0; i < sampleCount; i++)
                        {
                            short s = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
                            samples[i] = s / 32768f;
                        }

                        int frames = sampleCount / channels;
                        AudioClip clip = AudioClip.Create(clipName, frames, channels, sampleRate, false);
                        clip.SetData(samples, 0);
                        UnityEngine.Object.DontDestroyOnLoad(clip);
                        return clip;
                    }
                }
            }
            catch (Exception ex)
            {
                TABSTrumpPlugin.Log.LogWarning("Failed to load embedded WAV " + resourceName + ": " + ex.Message);
                return null;
            }
        }

        private bool IsCurrentlyPossessed()
        {
            if (_possessCamera == null)
            {
                _possessCamera = UnityEngine.Object.FindObjectOfType<CameraAbilityPossess>();
            }

            return _possessCamera != null && _possessCamera.currentUnit == _unit;
        }

        private static bool IsPossessAbilityPressed()
        {
            if (PlayerActions.Instance != null && PlayerActions.Instance.m_possessAttack3 != null)
            {
                if (PlayerActions.Instance.m_possessAttack3.WasPressed)
                {
                    return true;
                }
            }

            return Input.GetKeyDown(KeyCode.Space);
        }

        private Vector3 GetPossessTargetPosition()
        {
            Camera cam = Camera.main;
            Vector3 origin = _unit.data != null && _unit.data.torso != null
                ? _unit.data.torso.position
                : transform.position + Vector3.up * 1.5f;

            if (cam != null)
            {
                Ray ray = new Ray(cam.transform.position, cam.transform.forward);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 45f))
                {
                    Vector3 flat = hit.point - origin;
                    flat.y = 0f;
                    if (flat.magnitude > WallSpawnOffsetClamp)
                    {
                        return origin + flat.normalized * WallSpawnOffsetClamp;
                    }
                    return hit.point;
                }

                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f)
                {
                    return origin + forward.normalized * 9f;
                }
            }

            return origin + transform.forward * 9f;
        }

        private void EnsureFallbackVisuals()
        {
            if (_appliedFallbackVisuals || _unit == null || _unit.data == null)
            {
                return;
            }

            _appliedFallbackVisuals = true;

            if (!TrumpAssetBuilder.HasHairProp && _unit.data.head != null)
            {
                GameObject hair = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hair.name = "TrumpFallbackHair";
                Destroy(hair.GetComponent<Collider>());
                hair.transform.SetParent(_unit.data.head, false);
                hair.transform.localPosition = new Vector3(0.03f, 0.24f, 0.06f);
                hair.transform.localRotation = Quaternion.Euler(-12f, 0f, -6f);
                hair.transform.localScale = new Vector3(0.46f, 0.20f, 0.50f);

                Renderer r = hair.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material.color = new Color(0.95f, 0.79f, 0.24f, 1f);
                }
            }

            if (!TrumpAssetBuilder.HasTieProp && _unit.data.torso != null)
            {
                GameObject tie = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tie.name = "TrumpFallbackTie";
                Destroy(tie.GetComponent<Collider>());
                tie.transform.SetParent(_unit.data.torso, false);
                tie.transform.localPosition = new Vector3(0f, -0.02f, 0.19f);
                tie.transform.localRotation = Quaternion.identity;
                tie.transform.localScale = new Vector3(0.09f, 0.44f, 0.04f);

                Renderer r = tie.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material.color = new Color(0.84f, 0.09f, 0.09f, 1f);
                }
            }
        }

        private IEnumerator EnforceMatteSuitRoutine()
        {
            SuppressSpawnEffectsAndGlow();
            yield return null;
            SuppressSpawnEffectsAndGlow();
            yield return null;
            SuppressSpawnEffectsAndGlow();
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }
            SuppressSpawnEffectsAndGlow();
        }

        internal void SuppressSpawnEffectsAndGlow()
        {
            if (_unit == null)
            {
                _unit = GetComponent<Unit>();
            }
            if (_unit == null)
            {
                return;
            }

            try
            {
                _unit.RemoveHighlight();
            }
            catch
            {
            }

            int removedPlacementFx = 0;
            int stoppedParticles = 0;
            int strippedRenderers = 0;

            PlacementSpawnEffects[] spawnFx = _unit.GetComponentsInChildren<PlacementSpawnEffects>(true);
            for (int i = 0; i < spawnFx.Length; i++)
            {
                PlacementSpawnEffects pse = spawnFx[i];
                if (pse == null)
                {
                    continue;
                }

                ParticleSystem[] pseParticles = pse.GetComponentsInChildren<ParticleSystem>(true);
                for (int p = 0; p < pseParticles.Length; p++)
                {
                    if (pseParticles[p] != null)
                    {
                        pseParticles[p].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        stoppedParticles++;
                    }
                }

                removedPlacementFx++;
                if (pse.gameObject != _unit.gameObject)
                {
                    Destroy(pse.gameObject);
                }
                else
                {
                    Destroy(pse);
                }
            }

            ParticleSystem[] unitParticles = _unit.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < unitParticles.Length; i++)
            {
                ParticleSystem ps = unitParticles[i];
                if (ps == null)
                {
                    continue;
                }

                if (ps.GetComponentInParent<Weapon>() != null || ps.GetComponentInParent<RangeWeapon>() != null)
                {
                    continue;
                }

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.EmissionModule em = ps.emission;
                em.enabled = false;
                ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.enabled = false;
                }
                ps.gameObject.SetActive(false);
                stoppedParticles++;
            }

            Light[] unitLights = _unit.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < unitLights.Length; i++)
            {
                Light l = unitLights[i];
                if (l == null)
                {
                    continue;
                }

                if (l.GetComponentInParent<Weapon>() != null || l.GetComponentInParent<RangeWeapon>() != null)
                {
                    continue;
                }

                l.enabled = false;
            }

            Shader standardShader = Shader.Find("Standard");
            Renderer[] allRenderers = _unit.GetComponentsInChildren<Renderer>(true);
            for (int rIdx = 0; rIdx < allRenderers.Length; rIdx++)
            {
                Renderer rend = allRenderers[rIdx];
                if (rend == null || rend is ParticleSystemRenderer || rend is TrailRenderer || rend is LineRenderer)
                {
                    continue;
                }

                Material[] mats = rend.materials;
                bool changed = false;
                for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                {
                    Material mat = mats[mIdx];
                    if (mat == null)
                    {
                        continue;
                    }

                    string shaderName = mat.shader != null ? (mat.shader.name ?? string.Empty) : string.Empty;
                    if (standardShader != null &&
                        (shaderName.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Emiss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Neon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Fresnel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Rim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Holo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         shaderName.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Color preserved = mat.HasProperty("_Color") ? mat.GetColor("_Color") : new Color(0.09f, 0.11f, 0.16f, 1f);
                        mat.shader = standardShader;
                        mat.color = preserved;
                        changed = true;
                    }

                    if (mat.IsKeywordEnabled("_EMISSION"))
                    {
                        mat.DisableKeyword("_EMISSION");
                        changed = true;
                    }
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

                    if (StripColorProperty(mat, "_EmissionColor")) changed = true;
                    if (StripColorProperty(mat, "_EmissiveColor")) changed = true;
                    if (StripColorProperty(mat, "_GlowColor")) changed = true;
                    if (StripColorProperty(mat, "_RimColor")) changed = true;
                    if (StripColorProperty(mat, "_FresnelColor")) changed = true;
                    if (StripColorProperty(mat, "_HighlightColor")) changed = true;
                    if (StripColorProperty(mat, "_SpecColor")) changed = true;

                    if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") != null)
                    {
                        mat.SetTexture("_EmissionMap", null);
                        changed = true;
                    }
                    if (mat.HasProperty("_EmissionPower") && mat.GetFloat("_EmissionPower") > 0f)
                    {
                        mat.SetFloat("_EmissionPower", 0f);
                        changed = true;
                    }
                    if (mat.HasProperty("_GlowStrength") && mat.GetFloat("_GlowStrength") > 0f)
                    {
                        mat.SetFloat("_GlowStrength", 0f);
                        changed = true;
                    }
                    if (mat.HasProperty("_RimIntensity") && mat.GetFloat("_RimIntensity") > 0f)
                    {
                        mat.SetFloat("_RimIntensity", 0f);
                        changed = true;
                    }
                    if (mat.HasProperty("_Metallic") && mat.GetFloat("_Metallic") > 0f)
                    {
                        mat.SetFloat("_Metallic", 0f);
                        changed = true;
                    }
                    if (mat.HasProperty("_Glossiness") && mat.GetFloat("_Glossiness") > 0.06f)
                    {
                        mat.SetFloat("_Glossiness", 0.05f);
                        changed = true;
                    }
                    if (mat.HasProperty("_Smoothness") && mat.GetFloat("_Smoothness") > 0.06f)
                    {
                        mat.SetFloat("_Smoothness", 0.05f);
                        changed = true;
                    }
                }

                if (changed)
                {
                    rend.materials = mats;
                    strippedRenderers++;
                }
            }

            if (!_loggedSpawnDiagnostics)
            {
                _loggedSpawnDiagnostics = true;
                TABSTrumpPlugin.Log.LogInfo(
                    "Trump spawn glow cleanup: removedPlacementFx=" + removedPlacementFx +
                    ", stoppedParticles=" + stoppedParticles +
                    ", strippedRenderers=" + strippedRenderers +
                    ", totalRenderers=" + allRenderers.Length);
            }

            EnforceTrumpOutfitColors(standardShader);
        }

        private void EnforceTrumpOutfitColors(Shader standardShader)
        {
            CharacterItem[] items = _unit.GetComponentsInChildren<CharacterItem>(true);
            for (int i = 0; i < items.Length; i++)
            {
                CharacterItem item = items[i];
                if (item == null)
                {
                    continue;
                }

                string itemName = item.gameObject.name ?? string.Empty;
                Color? overrideColor = null;
                bool isSuit = false;

                if (itemName.IndexOf("Tie", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideColor = new Color(0.84f, 0.08f, 0.08f, 1f);
                }
                else if (itemName.IndexOf("Shirt", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideColor = new Color(0.95f, 0.95f, 0.96f, 1f);
                }
                else if (itemName.IndexOf("Suit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Jacket", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Blazer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Tuxedo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Coat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideColor = new Color(0.06f, 0.06f, 0.07f, 1f);
                    isSuit = true;
                }
                else if (itemName.IndexOf("Pants", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Trouser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Slack", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideColor = new Color(0.05f, 0.05f, 0.06f, 1f);
                }
                else if (itemName.IndexOf("Shoe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Loafer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Boot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideColor = new Color(0.04f, 0.04f, 0.05f, 1f);
                }
                else if (itemName.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         itemName.IndexOf("Pompadour", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideColor = new Color(0.93f, 0.77f, 0.26f, 1f);
                }

                if (!overrideColor.HasValue)
                {
                    continue;
                }

                Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
                CharacterItem.RendererMaterialWrapper[] defaultWrappers = item.DefaultColors;
                if (defaultWrappers == null || defaultWrappers.Length == 0)
                {
                    defaultWrappers = item.SharedMaterials;
                }

                for (int rIdx = 0; rIdx < renderers.Length; rIdx++)
                {
                    Renderer rend = renderers[rIdx];
                    if (rend == null)
                    {
                        continue;
                    }

                    Material[] mats = rend.materials;
                    for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                    {
                        Material mat = mats[mIdx];
                        if (mat == null)
                        {
                            continue;
                        }

                        Color targetColor = overrideColor.Value;
                        if (isSuit)
                        {
                            int suitRole = ResolveSuitMaterialRole(rend, mIdx, mat, defaultWrappers);
                            if (suitRole == 1)
                            {
                                targetColor = new Color(0.95f, 0.95f, 0.96f, 1f);
                            }
                            else if (suitRole == 2)
                            {
                                targetColor = new Color(0.84f, 0.08f, 0.08f, 1f);
                            }
                        }

                        if (standardShader != null && mat.shader != standardShader)
                        {
                            mat.shader = standardShader;
                        }

                        mat.color = targetColor;
                        if (mat.HasProperty("_Color"))
                        {
                            mat.SetColor("_Color", targetColor);
                        }
                        if (mat.HasProperty("_Metallic"))
                        {
                            mat.SetFloat("_Metallic", 0f);
                        }
                        if (mat.HasProperty("_Glossiness"))
                        {
                            mat.SetFloat("_Glossiness", 0.06f);
                        }
                        if (mat.HasProperty("_Smoothness"))
                        {
                            mat.SetFloat("_Smoothness", 0.06f);
                        }
                        if (mat.IsKeywordEnabled("_EMISSION"))
                        {
                            mat.DisableKeyword("_EMISSION");
                        }
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    }

                    rend.materials = mats;
                }

                if (!_loggedOutfitDiagnostics)
                {
                    TABSTrumpPlugin.Log.LogInfo("Enforced Trump outfit item: " + itemName + " (renderers=" + renderers.Length + ")");
                }
            }

            _loggedOutfitDiagnostics = true;
        }

        private static int ResolveSuitMaterialRole(
            Renderer rend,
            int matIndex,
            Material runtimeMat,
            CharacterItem.RendererMaterialWrapper[] wrappers)
        {
            if (wrappers != null && wrappers.Length > 0)
            {
                for (int w = 0; w < wrappers.Length; w++)
                {
                    CharacterItem.RendererMaterialWrapper wrapper = wrappers[w];
                    if (wrapper == null || wrapper.m_renderers == null || wrapper.m_materialIndices == null)
                    {
                        continue;
                    }

                    int count = Mathf.Min(wrapper.m_renderers.Count, wrapper.m_materialIndices.Count);
                    for (int k = 0; k < count; k++)
                    {
                        if (wrapper.m_renderers[k] == rend && wrapper.m_materialIndices[k] == matIndex)
                        {
                            string origName = wrapper.m_material != null ? (wrapper.m_material.name ?? string.Empty) : string.Empty;
                            int role = ClassifySuitMaterialName(origName);
                            if (role != 0)
                            {
                                return role;
                            }
                            return TrumpAssetBuilder.GetSuitSlotRole(w);
                        }
                    }
                }

                if (matIndex >= 0 && matIndex < wrappers.Length && wrappers[matIndex] != null && wrappers[matIndex].m_material != null)
                {
                    int bySlot = ClassifySuitMaterialName(wrappers[matIndex].m_material.name);
                    if (bySlot != 0)
                    {
                        return bySlot;
                    }
                }
            }

            if (runtimeMat != null)
            {
                int byRuntimeName = ClassifySuitMaterialName(runtimeMat.name);
                if (byRuntimeName != 0)
                {
                    return byRuntimeName;
                }
            }

            return TrumpAssetBuilder.GetSuitSlotRole(matIndex);
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

        private static bool StripColorProperty(Material mat, string propName)
        {
            if (!mat.HasProperty(propName))
            {
                return false;
            }

            Color c = mat.GetColor(propName);
            if (c.r > 0.001f || c.g > 0.001f || c.b > 0.001f || c.a > 0.001f)
            {
                mat.SetColor(propName, Color.black);
                return true;
            }

            return false;
        }

        private void TriggerWallCast(Vector3 desiredTargetPos)
        {
            Vector3 selfPos = _unit.data != null && _unit.data.torso != null
                ? _unit.data.torso.position
                : transform.position;

            Vector3 delta = desiredTargetPos - selfPos;
            delta.y = 0f;

            if (delta.sqrMagnitude < 1.5f)
            {
                delta = transform.forward * 6f;
                delta.y = 0f;
            }

            if (delta.magnitude > WallSpawnOffsetClamp)
            {
                delta = delta.normalized * WallSpawnOffsetClamp;
            }

            Vector3 groundTarget = selfPos + delta;
            RaycastHit groundHit;
            int rayMask = GetWallObstacleLayerMaskForGround();
            if (Physics.Raycast(new Vector3(groundTarget.x, selfPos.y + 18f, groundTarget.z), Vector3.down, out groundHit, 60f, rayMask))
            {
                groundTarget.y = groundHit.point.y;
            }
            else if (Physics.Raycast(new Vector3(groundTarget.x, selfPos.y + 18f, groundTarget.z), Vector3.down, out groundHit, 60f))
            {
                groundTarget.y = groundHit.point.y;
            }
            else
            {
                groundTarget.y = selfPos.y;
            }

            Quaternion wallRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            PlayTrumpVoice(true);
            PerformCastGesture(delta.normalized);
            StartCoroutine(CastAndDropWallRoutine(groundTarget, wallRotation));
        }

        private static int GetWallObstacleLayerMaskForGround()
        {
            int mapLayer = LayerMask.NameToLayer("Map");
            if (mapLayer >= 0)
            {
                return 1 << mapLayer;
            }
            return ~0;
        }

        private int ResolveWallObstacleLayer()
        {
            if (_cachedWallLayer >= 0)
            {
                return _cachedWallLayer;
            }

            int mask = 0;
            UnitAPI api = _unit != null ? _unit.GetComponent<UnitAPI>() : null;
            if (api != null && RaycastMaskField != null)
            {
                try
                {
                    LayerMask lm = (LayerMask)RaycastMaskField.GetValue(api);
                    mask = lm.value;
                }
                catch
                {
                    mask = 0;
                }
            }

            if (mask != 0)
            {
                string[] preferredNames = new string[] { "Map", "Terrain", "Environment", "Obstacle", "Ground", "Default" };
                for (int i = 0; i < preferredNames.Length; i++)
                {
                    int layerIdx = LayerMask.NameToLayer(preferredNames[i]);
                    if (layerIdx >= 0 && (mask & (1 << layerIdx)) != 0)
                    {
                        _cachedWallLayer = layerIdx;
                        TABSTrumpPlugin.Log.LogInfo("Resolved wall obstacle layer: " + preferredNames[i] + " (" + layerIdx + "), UnitAPI mask=" + mask);
                        return _cachedWallLayer;
                    }
                }

                for (int bit = 0; bit < 32; bit++)
                {
                    if ((mask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    string ln = LayerMask.LayerToName(bit) ?? string.Empty;
                    if (ln.IndexOf("Unit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ln.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ln.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ln.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    _cachedWallLayer = bit;
                    TABSTrumpPlugin.Log.LogInfo("Resolved fallback wall obstacle layer: " + ln + " (" + bit + "), UnitAPI mask=" + mask);
                    return _cachedWallLayer;
                }
            }

            int fallbackMap = LayerMask.NameToLayer("Map");
            _cachedWallLayer = fallbackMap >= 0 ? fallbackMap : 0;
            TABSTrumpPlugin.Log.LogInfo("Resolved default wall obstacle layer: " + _cachedWallLayer + ", UnitAPI mask=" + mask);
            return _cachedWallLayer;
        }

        private void PerformCastGesture(Vector3 aimDir)
        {
            if (_unit == null || _unit.data == null)
            {
                return;
            }

            AddTransformLift(_unit.data.rightHand, (Vector3.up * 1.4f + aimDir * 0.4f) * 28f);
            AddTransformLift(_unit.data.leftHand, (Vector3.up * 1.4f + aimDir * 0.4f) * 28f);

            if (_unit.data.mainRig != null)
            {
                _unit.data.mainRig.AddForce(Vector3.up * 160f, ForceMode.Impulse);
            }
        }

        private static void AddTransformLift(Transform t, Vector3 impulse)
        {
            if (t == null)
            {
                return;
            }

            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(impulse, ForceMode.Impulse);
            }
        }

        private IEnumerator CastAndDropWallRoutine(Vector3 groundTarget, Quaternion wallRotation)
        {
            GameObject telegraphRoot = SpawnGroundTelegraph(groundTarget, wallRotation);
            float elapsed = 0f;

            while (elapsed < WallTelegraphDuration)
            {
                if (_gameStateManager != null && _gameStateManager.GameState != GameState.BattleState)
                {
                    if (telegraphRoot != null)
                    {
                        Destroy(telegraphRoot);
                    }
                    yield break;
                }

                elapsed += Time.deltaTime;
                if (telegraphRoot != null)
                {
                    float progress = Mathf.Clamp01(elapsed / WallTelegraphDuration);
                    float pulse = 0.88f + 0.14f * Mathf.Sin(progress * Mathf.PI * 4f);
                    Transform slab = telegraphRoot.transform.Find("MarkerSlab");
                    if (slab != null)
                    {
                        slab.localScale = new Vector3(
                            (WallWidth + 0.8f) * Mathf.Lerp(0.7f, 1.0f, progress),
                            0.04f,
                            (WallThickness + 1.2f) * pulse);
                    }

                    Transform beam = telegraphRoot.transform.Find("TelegraphBeam");
                    if (beam != null)
                    {
                        float beamWidth = Mathf.Lerp(WallWidth * 0.9f, WallWidth * 0.18f, progress * progress);
                        beam.localScale = new Vector3(beamWidth, 16f, 0.35f);
                    }
                }

                yield return null;
            }

            if (telegraphRoot != null)
            {
                Destroy(telegraphRoot);
            }

            if (_gameStateManager != null && _gameStateManager.GameState != GameState.BattleState)
            {
                yield break;
            }

            int wallLayer = ResolveWallObstacleLayer();
            Vector3 spawnPos = groundTarget + Vector3.up * WallSpawnHeight;
            SpawnSkyPortalVfx(spawnPos, wallRotation);

            Transform visualPivot;
            GameObject wallRoot = BuildWallGameObject(spawnPos, wallRotation, wallLayer, out visualPivot);
            DroppedTrumpWall behavior = wallRoot.AddComponent<DroppedTrumpWall>();
            behavior.Initialize(_unit, visualPivot, groundTarget.y, WallSpawnHeight, WallWidth, WallHeight, WallThickness, WallImpactDamage, WallLifetime);
        }

        private static GameObject SpawnGroundTelegraph(Vector3 groundTarget, Quaternion wallRotation)
        {
            EnsureSharedMaterials();

            GameObject root = new GameObject("TrumpWallTelegraph");
            root.transform.position = groundTarget + Vector3.up * 0.04f;
            root.transform.rotation = wallRotation;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "MarkerSlab";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = new Vector3(WallWidth + 0.8f, 0.04f, WallThickness + 1.2f);
            Renderer r = marker.GetComponent<Renderer>();
            if (r != null && _telegraphMaterial != null)
            {
                r.sharedMaterial = _telegraphMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "TelegraphBeam";
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = new Vector3(0f, 8f, 0f);
            beam.transform.localScale = new Vector3(WallWidth * 0.8f, 16f, 0.35f);
            Renderer br = beam.GetComponent<Renderer>();
            if (br != null && _telegraphBeamMaterial != null)
            {
                br.sharedMaterial = _telegraphBeamMaterial;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                br.receiveShadows = false;
            }

            GameObject fx = new GameObject("TelegraphSparks");
            fx.transform.SetParent(root.transform, false);
            ParticleSystem ps = fx.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 8.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
            main.startColor = new Color(1.0f, 0.52f, 0.16f, 0.82f);
            main.duration = 0.4f;
            main.loop = false;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 26) });
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(WallWidth * 0.92f, 0.15f, WallThickness + 0.8f);

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(5f, 12f);

            ParticleSystemRenderer psRenderer = fx.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null && _dustParticleMaterial != null)
            {
                psRenderer.sharedMaterial = _dustParticleMaterial;
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            ps.Play();
            return root;
        }

        private static void SpawnSkyPortalVfx(Vector3 skyPos, Quaternion wallRotation)
        {
            EnsureSharedMaterials();
            SpawnOfficialSmokeVfx(skyPos, wallRotation, WallWidth * 0.85f, 2, 0.9f, 1.3f);
        }

        private GameObject BuildWallGameObject(Vector3 pos, Quaternion rot, int obstacleLayer, out Transform visualPivot)
        {
            EnsureSharedMaterials();

            GameObject root = new GameObject("Trump_OpenAlpha_Wall");
            root.transform.position = pos;
            root.transform.rotation = rot;
            root.layer = obstacleLayer;

            // Extend slightly into ground so sloped terrain leaves no line-of-sight gap underneath
            BoxCollider wallCollider = root.AddComponent<BoxCollider>();
            wallCollider.center = new Vector3(0f, -0.35f, 0f);
            wallCollider.size = new Vector3(WallWidth, WallHeight + 0.7f, WallThickness);

            // Pivot at bottom center so squash-and-stretch impact stays grounded while root stays at (1, 1, 1)
            GameObject pivotObj = new GameObject("VisualPivot");
            pivotObj.transform.SetParent(root.transform, false);
            pivotObj.transform.localPosition = new Vector3(0f, -WallHeight * 0.5f, 0f);
            visualPivot = pivotObj.transform;

            if (_brickBoxMesh == null)
            {
                _brickBoxMesh = CreateWorldUvBoxMesh(WallWidth, WallHeight, WallThickness, 1.40f, 1.32f);
            }

            GameObject body = new GameObject("BrickBody");
            body.transform.SetParent(visualPivot, false);
            body.transform.localPosition = new Vector3(0f, WallHeight * 0.5f, 0f);
            body.transform.localScale = Vector3.one;
            body.layer = obstacleLayer;

            MeshFilter bodyFilter = body.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = _brickBoxMesh;

            MeshRenderer bodyRenderer = body.AddComponent<MeshRenderer>();
            if (_brickBodyMaterial != null)
            {
                bodyRenderer.sharedMaterial = _brickBodyMaterial;
            }

            GameObject basePlinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlinth.name = "ConcreteBase";
            Destroy(basePlinth.GetComponent<Collider>());
            basePlinth.transform.SetParent(visualPivot, false);
            basePlinth.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            basePlinth.transform.localScale = new Vector3(WallWidth + 0.42f, 0.55f, WallThickness + 0.22f);
            Renderer baseRenderer = basePlinth.GetComponent<Renderer>();
            if (baseRenderer != null && _concreteMaterial != null)
            {
                baseRenderer.sharedMaterial = _concreteMaterial;
            }

            float[] pilasterOffsets = new float[]
            {
                -WallWidth * 0.5f + 0.14f,
                -WallWidth * 0.25f,
                0f,
                WallWidth * 0.25f,
                WallWidth * 0.5f - 0.14f
            };
            for (int p = 0; p < pilasterOffsets.Length; p++)
            {
                bool isEndCap = (p == 0 || p == pilasterOffsets.Length - 1);
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "Pilaster_" + p;
                Destroy(pillar.GetComponent<Collider>());
                pillar.transform.SetParent(visualPivot, false);
                pillar.transform.localPosition = new Vector3(pilasterOffsets[p], WallHeight * 0.5f, 0f);
                pillar.transform.localScale = new Vector3(
                    isEndCap ? 0.56f : 0.48f,
                    WallHeight + 0.08f,
                    WallThickness + (isEndCap ? 0.16f : 0.14f));
                Renderer pr = pillar.GetComponent<Renderer>();
                if (pr != null && _concreteMaterial != null)
                {
                    pr.sharedMaterial = _concreteMaterial;
                }
            }

            GameObject topCap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topCap.name = "ConcreteCap";
            Destroy(topCap.GetComponent<Collider>());
            topCap.transform.SetParent(visualPivot, false);
            topCap.transform.localPosition = new Vector3(0f, WallHeight + 0.12f, 0f);
            topCap.transform.localScale = new Vector3(WallWidth + 0.42f, 0.26f, WallThickness + 0.24f);
            Renderer capRenderer = topCap.GetComponent<Renderer>();
            if (capRenderer != null && _concreteMaterial != null)
            {
                capRenderer.sharedMaterial = _concreteMaterial;
            }

            for (int i = -6; i <= 6; i++)
            {
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "WirePost_" + i;
                Destroy(post.GetComponent<Collider>());
                post.transform.SetParent(visualPivot, false);
                post.transform.localPosition = new Vector3(i * (WallWidth / 12.6f), WallHeight + 0.58f, 0f);
                post.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
                post.transform.localScale = new Vector3(0.07f, 0.42f, 0.07f);
                Renderer pr = post.GetComponent<Renderer>();
                if (pr != null && _metalMaterial != null)
                {
                    pr.sharedMaterial = _metalMaterial;
                }
            }

            for (int row = 0; row < 3; row++)
            {
                GameObject wire = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wire.name = "BarbedWireRail_" + row;
                Destroy(wire.GetComponent<Collider>());
                wire.transform.SetParent(visualPivot, false);
                wire.transform.localPosition = new Vector3(0f, WallHeight + 0.42f + row * 0.23f, 0.04f * row);
                wire.transform.localScale = new Vector3(WallWidth - 0.1f, 0.035f, 0.035f);
                Renderer wr = wire.GetComponent<Renderer>();
                if (wr != null && _metalMaterial != null)
                {
                    wr.sharedMaterial = _metalMaterial;
                }
            }

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 4500f;
            rb.useGravity = false;
            rb.isKinematic = true;

            return root;
        }

        internal static void EnsureSharedMaterials()
        {
            if (_brickTexture == null)
            {
                _brickTexture = CreateBrickTexture();
            }

            if (_softDustTexture == null)
            {
                _softDustTexture = CreateSoftCircleTexture(64);
            }

            Shader spriteShader = Shader.Find("Sprites/Default");
            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null)
            {
                GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Renderer tempRenderer = tempCube.GetComponent<Renderer>();
                if (tempRenderer != null && tempRenderer.sharedMaterial != null)
                {
                    standardShader = tempRenderer.sharedMaterial.shader;
                }
                Destroy(tempCube);
            }

            if (_brickBodyMaterial == null && standardShader != null)
            {
                _brickBodyMaterial = new Material(standardShader);
                _brickBodyMaterial.mainTexture = _brickTexture;
                _brickBodyMaterial.mainTextureScale = Vector2.one;
                _brickBodyMaterial.color = new Color(0.88f, 0.46f, 0.35f, 1f);
                if (_brickBodyMaterial.HasProperty("_Glossiness"))
                {
                    _brickBodyMaterial.SetFloat("_Glossiness", 0.15f);
                }
                UnityEngine.Object.DontDestroyOnLoad(_brickBodyMaterial);
            }

            if (_concreteMaterial == null && standardShader != null)
            {
                _concreteMaterial = new Material(standardShader);
                _concreteMaterial.color = new Color(0.68f, 0.67f, 0.64f, 1f);
                if (_concreteMaterial.HasProperty("_Glossiness"))
                {
                    _concreteMaterial.SetFloat("_Glossiness", 0.12f);
                }
                UnityEngine.Object.DontDestroyOnLoad(_concreteMaterial);
            }

            if (_metalMaterial == null && standardShader != null)
            {
                _metalMaterial = new Material(standardShader);
                _metalMaterial.color = new Color(0.24f, 0.26f, 0.29f, 1f);
                UnityEngine.Object.DontDestroyOnLoad(_metalMaterial);
            }

            Shader fxShader = spriteShader ?? standardShader;

            if (_dustParticleMaterial == null && fxShader != null)
            {
                _dustParticleMaterial = new Material(fxShader);
                _dustParticleMaterial.mainTexture = _softDustTexture;
                _dustParticleMaterial.color = Color.white;
                UnityEngine.Object.DontDestroyOnLoad(_dustParticleMaterial);
            }

            if (_telegraphMaterial == null && fxShader != null)
            {
                _telegraphMaterial = new Material(fxShader);
                _telegraphMaterial.color = new Color(0.96f, 0.34f, 0.12f, 0.55f);
                UnityEngine.Object.DontDestroyOnLoad(_telegraphMaterial);
            }

            if (_telegraphBeamMaterial == null && fxShader != null)
            {
                _telegraphBeamMaterial = new Material(fxShader);
                _telegraphBeamMaterial.color = new Color(1.0f, 0.62f, 0.22f, 0.16f);
                UnityEngine.Object.DontDestroyOnLoad(_telegraphBeamMaterial);
            }
        }

        private static Mesh CreateWorldUvBoxMesh(float width, float height, float depth, float uScale, float vScale)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            float hz = depth * 0.5f;

            Vector3 p000 = new Vector3(-hx, -hy, -hz);
            Vector3 p100 = new Vector3(hx, -hy, -hz);
            Vector3 p110 = new Vector3(hx, hy, -hz);
            Vector3 p010 = new Vector3(-hx, hy, -hz);
            Vector3 p001 = new Vector3(-hx, -hy, hz);
            Vector3 p101 = new Vector3(hx, -hy, hz);
            Vector3 p111 = new Vector3(hx, hy, hz);
            Vector3 p011 = new Vector3(-hx, hy, hz);

            Vector3[] vertices = new Vector3[]
            {
                // Front (-Z)
                p000, p010, p110, p100,
                // Back (+Z)
                p101, p111, p011, p001,
                // Left (-X)
                p001, p011, p010, p000,
                // Right (+X)
                p100, p110, p111, p101,
                // Top (+Y)
                p010, p011, p111, p110,
                // Bottom (-Y)
                p001, p000, p100, p101
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down
            };

            float wu = width * uScale;
            float hu = height * vScale;
            float du = depth * uScale;
            float dv = depth * vScale;

            Vector2[] uvs = new Vector2[]
            {
                // Front (-Z): width x height
                new Vector2(0f, 0f), new Vector2(0f, hu), new Vector2(wu, hu), new Vector2(wu, 0f),
                // Back (+Z): width x height
                new Vector2(0f, 0f), new Vector2(0f, hu), new Vector2(wu, hu), new Vector2(wu, 0f),
                // Left (-X): depth x height
                new Vector2(0f, 0f), new Vector2(0f, hu), new Vector2(du, hu), new Vector2(du, 0f),
                // Right (+X): depth x height
                new Vector2(0f, 0f), new Vector2(0f, hu), new Vector2(du, hu), new Vector2(du, 0f),
                // Top (+Y): width x depth
                new Vector2(0f, 0f), new Vector2(0f, dv), new Vector2(wu, dv), new Vector2(wu, 0f),
                // Bottom (-Y): width x depth
                new Vector2(0f, 0f), new Vector2(0f, dv), new Vector2(wu, dv), new Vector2(wu, 0f)
            };

            int[] triangles = new int[]
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23
            };

            Mesh mesh = new Mesh();
            mesh.name = "TrumpWorldUvBrickBox";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            UnityEngine.Object.DontDestroyOnLoad(mesh);
            return mesh;
        }

        internal static Material GetDustMaterial()
        {
            EnsureSharedMaterials();
            return _dustParticleMaterial;
        }

        internal static Material GetBrickMaterial()
        {
            EnsureSharedMaterials();
            return _brickBodyMaterial;
        }

        internal static Material GetConcreteMaterial()
        {
            EnsureSharedMaterials();
            return _concreteMaterial;
        }

        internal static void SpawnOfficialImpactVfx(
            Vector3 center,
            Quaternion rot,
            float width,
            int count,
            float scale)
        {
            GameObject template = TrumpAssetBuilder.WallImpactVfxTemplate ?? TrumpAssetBuilder.WallSmokeVfxTemplate;
            Vector3 right = rot * Vector3.right;
            int actualCount = Mathf.Max(1, count);

            if (template != null)
            {
                for (int i = 0; i < actualCount; i++)
                {
                    float t = actualCount == 1 ? 0f : Mathf.Lerp(-0.5f, 0.5f, i / (float)(actualCount - 1));
                    Vector3 spawnPos = center + right * (t * width) + Vector3.up * 0.18f;
                    SpawnOfficialVfxInstance(template, spawnPos, rot, scale, 4.2f);
                }
                return;
            }

            SpawnOfficialSmokeVfx(center, rot, width, actualCount + 1, scale * 0.85f, scale * 1.15f);
        }

        internal static void SpawnOfficialSmokeVfx(
            Vector3 center,
            Quaternion rot,
            float width,
            int count,
            float minScale,
            float maxScale)
        {
            GameObject template = TrumpAssetBuilder.WallSmokeVfxTemplate ?? TrumpAssetBuilder.WallImpactVfxTemplate;
            Vector3 right = rot * Vector3.right;
            int actualCount = Mathf.Max(1, count);

            if (template != null)
            {
                for (int i = 0; i < actualCount; i++)
                {
                    float t = actualCount == 1 ? 0f : Mathf.Lerp(-0.48f, 0.48f, i / (float)(actualCount - 1));
                    float jitter = UnityEngine.Random.Range(-0.45f, 0.45f);
                    Vector3 spawnPos = center + right * (t * width + jitter) + Vector3.up * 0.14f;
                    float scale = UnityEngine.Random.Range(minScale, maxScale);
                    SpawnOfficialVfxInstance(template, spawnPos, rot, scale, 3.8f);
                }
                return;
            }

            SpawnFallbackBillboardDust(center, rot, width, actualCount * 14);
        }

        private static void SpawnOfficialVfxInstance(
            GameObject template,
            Vector3 position,
            Quaternion rotation,
            float scale,
            float destroyDelay)
        {
            if (template == null)
            {
                return;
            }

            GameObject instance = UnityEngine.Object.Instantiate(template, position, rotation);
            instance.name = "TrumpOfficialVfx";
            instance.transform.localScale = Vector3.one * Mathf.Max(0.25f, scale);
            instance.SetActive(true);

            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                ps.gameObject.SetActive(true);
                ParticleSystem.MainModule main = ps.main;
                main.loop = false;
                main.playOnAwake = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;

                ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.enabled = true;
                }

                ps.Clear(true);
                ps.Play(true);
            }

            TrumpAutoDestroyVfx cleaner = instance.AddComponent<TrumpAutoDestroyVfx>();
            cleaner.Initialize(destroyDelay);
        }

        private static void SpawnFallbackBillboardDust(Vector3 center, Quaternion rot, float width, int burstCount)
        {
            EnsureSharedMaterials();
            if (_dustParticleMaterial == null)
            {
                return;
            }

            GameObject fx = new GameObject("TrumpFallbackDust");
            fx.transform.position = center + Vector3.up * 0.25f;
            fx.transform.rotation = rot;

            ParticleSystem ps = fx.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.duration = 1.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
            main.startColor = new Color(0.84f, 0.78f, 0.70f, 0.65f);
            main.gravityModifier = -0.06f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(burstCount, 12, 80)) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Mathf.Max(1.0f, width), 0.35f, 1.1f);

            ParticleSystemRenderer psr = fx.GetComponent<ParticleSystemRenderer>();
            if (psr != null)
            {
                psr.sharedMaterial = _dustParticleMaterial;
                psr.renderMode = ParticleSystemRenderMode.Billboard;
            }

            ps.Play();
            TrumpAutoDestroyVfx cleaner = fx.AddComponent<TrumpAutoDestroyVfx>();
            cleaner.Initialize(2.2f);
        }

        internal static void PlayWallImpactSound(Vector3 position)
        {
            try
            {
                SoundPlayer sp = ServiceLocator.GetService<SoundPlayer>();
                if (sp == null)
                {
                    return;
                }

                if (!_soundKeySearched)
                {
                    _soundKeySearched = true;
                    _cachedImpactSoundKey = FindImpactSoundKey(sp);
                }

                if (!string.IsNullOrEmpty(_cachedImpactSoundKey))
                {
                    sp.PlaySoundEffect(_cachedImpactSoundKey, 1.45f, position, SoundEffectVariations.MaterialType.Default, null, 0.76f);
                }
            }
            catch
            {
            }
        }

        private static string FindImpactSoundKey(SoundPlayer sp)
        {
            FieldInfo dictField = typeof(SoundPlayer).GetField("soundEffectDictionary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (dictField == null)
            {
                return null;
            }

            System.Collections.IDictionary dict = dictField.GetValue(sp) as System.Collections.IDictionary;
            if (dict == null)
            {
                return null;
            }

            string[] priorities = new string[] { "Stone", "Rock", "Explosion", "Stomp", "Hammer", "Smash", "Impact", "Thud" };
            string best = null;
            int bestRank = int.MaxValue;

            foreach (object k in dict.Keys)
            {
                string key = k as string;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                for (int p = 0; p < priorities.Length; p++)
                {
                    if (key.IndexOf(priorities[p], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (p < bestRank)
                        {
                            bestRank = p;
                            best = key;
                        }
                        break;
                    }
                }
            }

            return best;
        }

        private static Texture2D CreateBrickTexture()
        {
            int w = 128;
            int h = 128;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;

            Color mortar = new Color(0.76f, 0.72f, 0.67f, 1f);
            Color brickA = new Color(0.74f, 0.32f, 0.23f, 1f);
            Color brickB = new Color(0.65f, 0.27f, 0.20f, 1f);
            Color brickC = new Color(0.80f, 0.39f, 0.29f, 1f);

            int rows = 8;
            int rowHeight = h / rows;
            int brickWidth = 32;

            for (int y = 0; y < h; y++)
            {
                int row = y / rowHeight;
                int yInRow = y % rowHeight;
                int xOffset = (row % 2 == 1) ? (brickWidth / 2) : 0;

                for (int x = 0; x < w; x++)
                {
                    int shiftedX = (x + xOffset) % w;
                    int xInBrick = shiftedX % brickWidth;
                    int col = shiftedX / brickWidth;

                    if (yInRow < 2 || xInBrick < 2)
                    {
                        tex.SetPixel(x, y, mortar);
                    }
                    else
                    {
                        int hash = (row * 7 + col * 13) % 3;
                        Color c = hash == 0 ? brickA : (hash == 1 ? brickB : brickC);
                        tex.SetPixel(x, y, c);
                    }
                }
            }

            tex.Apply();
            UnityEngine.Object.DontDestroyOnLoad(tex);
            return tex;
        }

        private static Texture2D CreateSoftCircleTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - r);
                    alpha = alpha * alpha;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            UnityEngine.Object.DontDestroyOnLoad(tex);
            return tex;
        }
    }

    internal class DroppedTrumpWall : MonoBehaviour
    {
        private enum LandingPhase
        {
            Falling,
            CornerSlam,
            SlabClatter,
            Settled
        }

        private enum CollapseState
        {
            Standing,
            WobbleWarning,
            Toppling,
            Slammed,
            CrumbleSink
        }

        private const float HangDuration = 0.20f;
        private const float PlungeDuration = 0.27f;
        private const float CornerSlamDuration = 0.14f;
        private const float SlabClatterDuration = 0.09f;
        private const float WobbleDuration = 0.44f;
        private const float SlamRestDuration = 0.85f;
        private const float CrumbleSinkDuration = 0.90f;
        private const float ToppleCrushDamage = 220f;

        private Unit _owner;
        private Team _ownerTeam;
        private Transform _visualPivot;
        private float _groundY;
        private float _startCenterY;
        private float _targetCenterY;
        private float _wallWidth;
        private float _wallHeight;
        private float _wallThickness;
        private float _impactDamage;
        private float _lifetime;
        private float _dropElapsed;
        private float _plungePuffTimer;
        private float _postImpactAge;

        private float _spawnTiltZ;
        private float _spawnTiltX;
        private float _impactTiltZ;
        private float _impactTiltX;
        private LandingPhase _landingPhase = LandingPhase.Falling;
        private float _landingTimer;

        private CollapseState _collapseState = CollapseState.Standing;
        private float _collapseTimer;
        private int _toppleSign = 1;
        private float _toppleAngle;
        private float _toppleAngularVel;
        private Vector3 _topplePivotWorld;
        private Vector3 _toppleAxisRight;
        private Vector3 _toppleForwardFlat;
        private float _bounceOffsetDeg;
        private bool _didToppleSlam;

        private GameStateManager _gsm;
        private NavmeshCut _navCut;
        private Bounds _cutBounds;
        private readonly HashSet<int> _damagedUnits = new HashSet<int>();
        private readonly HashSet<int> _toppleDamagedUnits = new HashSet<int>();

        public void Initialize(
            Unit owner,
            Transform visualPivot,
            float groundY,
            float spawnHeight,
            float wallWidth,
            float wallHeight,
            float wallThickness,
            float impactDamage,
            float lifetime)
        {
            _owner = owner;
            _ownerTeam = owner != null ? owner.Team : Team.Red;
            _visualPivot = visualPivot;
            _groundY = groundY;
            _wallWidth = wallWidth;
            _wallHeight = wallHeight;
            _wallThickness = wallThickness;
            _impactDamage = impactDamage;
            _lifetime = lifetime;
            _targetCenterY = _groundY + _wallHeight * 0.5f - 0.16f;
            _startCenterY = _groundY + spawnHeight;
            _gsm = ServiceLocator.GetService<GameStateManager>();

            float side = UnityEngine.Random.value >= 0.5f ? 1f : -1f;
            _spawnTiltZ = side * UnityEngine.Random.Range(5.2f, 8.2f);
            _spawnTiltX = UnityEngine.Random.Range(-3.2f, 3.2f);
            _impactTiltZ = _spawnTiltZ * 1.14f;
            _impactTiltX = _spawnTiltX;

            if (_visualPivot != null)
            {
                _visualPivot.localScale = Vector3.one;
                _visualPivot.localRotation = Quaternion.Euler(_spawnTiltX, 0f, _spawnTiltZ);
            }
        }

        private float ComputeTiltHeightOffset(float tiltXDeg, float tiltZDeg)
        {
            return (_wallWidth * 0.5f) * Mathf.Abs(Mathf.Sin(tiltZDeg * Mathf.Deg2Rad))
                + (_wallThickness * 0.5f) * Mathf.Abs(Mathf.Sin(tiltXDeg * Mathf.Deg2Rad));
        }

        private void Update()
        {
            if (_gsm == null)
            {
                _gsm = ServiceLocator.GetService<GameStateManager>();
            }

            if (_gsm != null && _gsm.GameState != GameState.BattleState)
            {
                Destroy(gameObject);
                return;
            }

            if (_landingPhase == LandingPhase.Falling)
            {
                UpdateSlapstickRigidDrop();
                return;
            }

            if (_landingPhase == LandingPhase.CornerSlam || _landingPhase == LandingPhase.SlabClatter)
            {
                UpdateCornerSlamSettle();
                return;
            }

            _postImpactAge += Time.deltaTime;

            if (_collapseState == CollapseState.Standing && _postImpactAge >= _lifetime)
            {
                BeginWobbleWarning();
            }
            else if (_collapseState != CollapseState.Standing)
            {
                UpdateToppleCollapse();
            }
        }

        private void UpdateSlapstickRigidDrop()
        {
            _dropElapsed += Time.deltaTime;

            if (_visualPivot != null)
            {
                _visualPivot.localScale = Vector3.one;
            }

            Vector3 pos = transform.position;
            if (_dropElapsed <= HangDuration)
            {
                float h = Mathf.Clamp01(_dropElapsed / HangDuration);
                float teeterZ = Mathf.Sin(h * Mathf.PI * 3.5f) * 1.65f;
                float teeterX = Mathf.Cos(h * Mathf.PI * 2.5f) * 0.95f;
                pos.y = _startCenterY + Mathf.Sin(h * Mathf.PI) * 0.32f;
                transform.position = pos;

                if (_visualPivot != null)
                {
                    _visualPivot.localRotation = Quaternion.Euler(_spawnTiltX + teeterX, 0f, _spawnTiltZ + teeterZ);
                }
                return;
            }

            float plungeT = Mathf.Clamp01((_dropElapsed - HangDuration) / PlungeDuration);
            float eased = 0.15f * plungeT + 0.85f * plungeT * plungeT * plungeT;
            float curZ = Mathf.Lerp(_spawnTiltZ, _impactTiltZ, plungeT);
            float curX = Mathf.Lerp(_spawnTiltX, _impactTiltX, plungeT);
            float impactCenterY = _targetCenterY + ComputeTiltHeightOffset(_impactTiltX, _impactTiltZ);

            pos.y = Mathf.Lerp(_startCenterY, impactCenterY, eased);
            transform.position = pos;

            if (_visualPivot != null)
            {
                _visualPivot.localRotation = Quaternion.Euler(curX, 0f, curZ);
            }

            _plungePuffTimer += Time.deltaTime;
            if (_plungePuffTimer >= 0.085f && plungeT < 0.90f)
            {
                _plungePuffTimer = 0f;
                TrumpWallAbility.SpawnOfficialSmokeVfx(
                    transform.position + Vector3.up * (_wallHeight * 0.15f),
                    transform.rotation,
                    _wallWidth * 0.75f,
                    1,
                    0.55f,
                    0.75f);
            }

            if (plungeT >= 1f)
            {
                TriggerFirstCornerImpact();
            }
        }

        private void TriggerFirstCornerImpact()
        {
            _landingPhase = LandingPhase.CornerSlam;
            _landingTimer = 0f;

            float impactCenterY = _targetCenterY + ComputeTiltHeightOffset(_impactTiltX, _impactTiltZ);
            transform.position = new Vector3(transform.position.x, impactCenterY, transform.position.z);
            if (_visualPivot != null)
            {
                _visualPivot.localRotation = Quaternion.Euler(_impactTiltX, 0f, _impactTiltZ);
                _visualPivot.localScale = Vector3.one;
            }

            float lowSign = _impactTiltZ >= 0f ? -1f : 1f;
            Vector3 right = transform.rotation * Vector3.right;
            Vector3 lowCornerPos = new Vector3(transform.position.x, _groundY + 0.14f, transform.position.z)
                + right * (lowSign * _wallWidth * 0.40f);

            if (ScreenShake.Instance != null)
            {
                try
                {
                    ScreenShake.Instance.AddForce(new Vector3(0f, -1.55f, 0f), lowCornerPos);
                }
                catch
                {
                }
            }

            TrumpWallAbility.PlayWallImpactSound(lowCornerPos);
            TrumpWallAbility.SpawnOfficialImpactVfx(lowCornerPos, transform.rotation, 3.0f, 1, 1.05f);
            TrumpWallAbility.SpawnOfficialSmokeVfx(lowCornerPos, transform.rotation, 3.8f, 2, 0.9f, 1.25f);
            SpawnBrickDebris(lowCornerPos, transform.rotation, 10, 0.72f);
            ApplyPartialSideCrushDamage(lowSign);
        }

        private void UpdateCornerSlamSettle()
        {
            _landingTimer += Time.deltaTime;
            if (_visualPivot != null)
            {
                _visualPivot.localScale = Vector3.one;
            }

            if (_landingPhase == LandingPhase.CornerSlam)
            {
                float u = Mathf.Clamp01(_landingTimer / CornerSlamDuration);
                float remain = 1f - u * u;
                float curZ = _impactTiltZ * remain;
                float curX = _impactTiltX * remain;

                float yOff = ComputeTiltHeightOffset(curX, curZ);
                transform.position = new Vector3(transform.position.x, _targetCenterY + yOff, transform.position.z);

                if (_visualPivot != null)
                {
                    _visualPivot.localRotation = Quaternion.Euler(curX, 0f, curZ);
                }

                if (u >= 1f)
                {
                    TriggerSecondCornerSlam();
                }
                return;
            }

            if (_landingPhase == LandingPhase.SlabClatter)
            {
                float u = Mathf.Clamp01(_landingTimer / SlabClatterDuration);
                float reboundSign = _impactTiltZ >= 0f ? -1f : 1f;
                float clatterZ = reboundSign * 0.95f * Mathf.Sin(u * Mathf.PI);
                float yOff = ComputeTiltHeightOffset(0f, clatterZ);

                transform.position = new Vector3(transform.position.x, _targetCenterY + yOff, transform.position.z);
                if (_visualPivot != null)
                {
                    _visualPivot.localRotation = Quaternion.Euler(0f, 0f, clatterZ);
                }

                if (u >= 1f)
                {
                    transform.position = new Vector3(transform.position.x, _targetCenterY, transform.position.z);
                    if (_visualPivot != null)
                    {
                        _visualPivot.localRotation = Quaternion.identity;
                    }
                    _landingPhase = LandingPhase.Settled;
                }
            }
        }

        private void TriggerSecondCornerSlam()
        {
            _landingPhase = LandingPhase.SlabClatter;
            _landingTimer = 0f;

            transform.position = new Vector3(transform.position.x, _targetCenterY, transform.position.z);
            if (_visualPivot != null)
            {
                _visualPivot.localRotation = Quaternion.identity;
                _visualPivot.localScale = Vector3.one;
            }

            Vector3 groundImpactCenter = new Vector3(transform.position.x, _groundY + 0.12f, transform.position.z);

            if (ScreenShake.Instance != null)
            {
                try
                {
                    ScreenShake.Instance.AddForce(new Vector3(0f, -2.5f, 0f), groundImpactCenter);
                }
                catch
                {
                }
            }

            TrumpWallAbility.PlayWallImpactSound(groundImpactCenter);
            TrumpWallAbility.SpawnOfficialImpactVfx(groundImpactCenter, transform.rotation, _wallWidth * 0.78f, 3, 1.15f);
            TrumpWallAbility.SpawnOfficialSmokeVfx(groundImpactCenter, transform.rotation, _wallWidth * 0.92f, 4, 1.05f, 1.45f);
            SpawnBrickDebris(groundImpactCenter, transform.rotation, 24, 1.0f);
            ApplyAreaCrushDamage();
            AttachNavmeshObstacle();
        }

        private void AttachNavmeshObstacle()
        {
            try
            {
                BoxCollider col = GetComponent<BoxCollider>();
                if (col != null)
                {
                    _cutBounds = col.bounds;
                    _cutBounds.Expand(new Vector3(1.2f, 2.0f, 1.8f));
                }

                _navCut = gameObject.AddComponent<NavmeshCut>();
                _navCut.type = NavmeshCut.MeshType.Rectangle;
                _navCut.rectangleSize = new Vector2(_wallWidth + 1.4f, _wallThickness + 2.2f);
                _navCut.height = _wallHeight + 3.0f;
                _navCut.center = Vector3.zero;
                _navCut.useRotationAndScale = true;
                _navCut.cutsAddedGeom = true;
                _navCut.isDual = false;
                _navCut.updateDistance = 0.25f;
                _navCut.ForceUpdate();

                RefreshAstarGraphs();
            }
            catch (Exception ex)
            {
                TABSTrumpPlugin.Log.LogWarning("NavmeshCut setup skipped: " + ex.Message);
            }
        }

        private void ClearNavmeshObstacle()
        {
            try
            {
                if (_navCut != null)
                {
                    Destroy(_navCut);
                    _navCut = null;
                }

                RefreshAstarGraphs();
            }
            catch
            {
            }
        }

        private void RefreshAstarGraphs()
        {
            if (AstarPath.active == null)
            {
                return;
            }

            if (AstarPath.active.navmeshUpdates != null)
            {
                AstarPath.active.navmeshUpdates.updateInterval = 0.2f;
                AstarPath.active.navmeshUpdates.ForceUpdate();
            }

            if (AstarPath.active.data != null && AstarPath.active.data.graphs != null)
            {
                for (int i = 0; i < AstarPath.active.data.graphs.Length; i++)
                {
                    if (AstarPath.active.data.graphs[i] is GridGraph && _cutBounds.size.sqrMagnitude > 0.01f)
                    {
                        AstarPath.active.UpdateGraphs(_cutBounds);
                        break;
                    }
                }
            }
        }

        private void BeginWobbleWarning()
        {
            _collapseState = CollapseState.WobbleWarning;
            _collapseTimer = 0f;
            _toppleSign = ChooseToppleDirection();
            _toppleAngle = 0f;
            _toppleAngularVel = 24f;
            _bounceOffsetDeg = 0f;
            _didToppleSlam = false;

            Vector3 forward = transform.rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            _toppleForwardFlat = forward;

            Vector3 bottomCenter = new Vector3(transform.position.x, _groundY, transform.position.z);
            _topplePivotWorld = bottomCenter + _toppleForwardFlat * (_toppleSign * _wallThickness * 0.48f);
            _toppleAxisRight = transform.rotation * Vector3.right;

            ClearNavmeshObstacle();

            Vector3 basePos = new Vector3(transform.position.x, _groundY + 0.14f, transform.position.z);
            TrumpWallAbility.SpawnOfficialSmokeVfx(basePos, transform.rotation, _wallWidth * 0.8f, 2, 0.75f, 1.0f);
            SpawnBrickDebris(basePos + Vector3.up * (_wallHeight * 0.72f), transform.rotation, 6, 0.35f);
        }

        private int ChooseToppleDirection()
        {
            Vector3 forward = transform.rotation * Vector3.forward;
            int frontEnemies = 0;
            int backEnemies = 0;

            Vector3 scanHalf = new Vector3(_wallWidth * 0.5f + 1.0f, 3.0f, _wallHeight + 2.0f);
            Collider[] nearby = Physics.OverlapBox(
                new Vector3(transform.position.x, _groundY + 1.5f, transform.position.z),
                scanHalf,
                transform.rotation);

            HashSet<int> counted = new HashSet<int>();
            for (int i = 0; i < nearby.Length; i++)
            {
                Unit u = nearby[i].GetComponentInParent<Unit>();
                if (u == null || u.dead || u.Team == _ownerTeam)
                {
                    continue;
                }

                int id = u.GetInstanceID();
                if (!counted.Add(id))
                {
                    continue;
                }

                float side = Vector3.Dot(u.transform.position - transform.position, forward);
                if (side >= 0f)
                {
                    frontEnemies++;
                }
                else
                {
                    backEnemies++;
                }
            }

            if (frontEnemies > backEnemies)
            {
                return 1;
            }
            if (backEnemies > frontEnemies)
            {
                return -1;
            }
            return UnityEngine.Random.value >= 0.5f ? 1 : -1;
        }

        private void UpdateToppleCollapse()
        {
            _collapseTimer += Time.deltaTime;

            if (_collapseState == CollapseState.WobbleWarning)
            {
                float t = Mathf.Clamp01(_collapseTimer / WobbleDuration);
                float leanDeg = Mathf.Lerp(0f, 4.5f, t * t) + Mathf.Sin(t * Mathf.PI * 5.0f) * (1.8f * (1f - t * 0.4f));
                if (_visualPivot != null)
                {
                    _visualPivot.localRotation = Quaternion.Euler(_toppleSign * leanDeg, 0f, 0f);
                }

                if (_collapseTimer >= WobbleDuration)
                {
                    if (_visualPivot != null)
                    {
                        _visualPivot.localRotation = Quaternion.identity;
                    }
                    _toppleAngle = 4.5f;
                    transform.RotateAround(_topplePivotWorld, _toppleAxisRight, _toppleSign * _toppleAngle);
                    _collapseState = CollapseState.Toppling;
                    _collapseTimer = 0f;
                }
                return;
            }

            if (_collapseState == CollapseState.Toppling)
            {
                float angleRad = _toppleAngle * Mathf.Deg2Rad;
                _toppleAngularVel += (260f * Mathf.Sin(Mathf.Max(0.14f, angleRad)) + 45f) * Time.deltaTime;
                float stepDeg = _toppleAngularVel * Time.deltaTime;

                if (_toppleAngle + stepDeg >= 90f)
                {
                    stepDeg = 90f - _toppleAngle;
                    transform.RotateAround(_topplePivotWorld, _toppleAxisRight, _toppleSign * stepDeg);
                    _toppleAngle = 90f;
                    TriggerToppleSlam();
                    return;
                }

                transform.RotateAround(_topplePivotWorld, _toppleAxisRight, _toppleSign * stepDeg);
                _toppleAngle += stepDeg;

                if (_toppleAngle >= 18f)
                {
                    ApplyToppleSweepDamage();
                }
                return;
            }

            if (_collapseState == CollapseState.Slammed)
            {
                float bounceDuration = 0.24f;
                if (_collapseTimer <= bounceDuration)
                {
                    float u = _collapseTimer / bounceDuration;
                    float targetBounce = -3.8f * Mathf.Sin(u * Mathf.PI) * (1f - u * 0.5f);
                    float delta = targetBounce - _bounceOffsetDeg;
                    transform.RotateAround(_topplePivotWorld, _toppleAxisRight, _toppleSign * delta);
                    _bounceOffsetDeg = targetBounce;
                }
                else if (Mathf.Abs(_bounceOffsetDeg) > 0.001f)
                {
                    transform.RotateAround(_topplePivotWorld, _toppleAxisRight, _toppleSign * (-_bounceOffsetDeg));
                    _bounceOffsetDeg = 0f;
                }

                if (_collapseTimer >= SlamRestDuration)
                {
                    BoxCollider col = GetComponent<BoxCollider>();
                    if (col != null)
                    {
                        col.enabled = false;
                    }
                    _collapseState = CollapseState.CrumbleSink;
                    _collapseTimer = 0f;

                    Vector3 forward = _toppleForwardFlat.sqrMagnitude > 0.01f ? _toppleForwardFlat : Vector3.forward;
                    Vector3 sinkCenter = _topplePivotWorld + forward * (_toppleSign * _wallHeight * 0.5f);
                    sinkCenter.y = _groundY + 0.14f;
                    TrumpWallAbility.SpawnOfficialSmokeVfx(
                        sinkCenter,
                        Quaternion.LookRotation(forward, Vector3.up),
                        _wallWidth * 0.85f,
                        3,
                        0.9f,
                        1.25f);
                }
                return;
            }

            if (_collapseState == CollapseState.CrumbleSink)
            {
                transform.position += Vector3.down * (1.25f * Time.deltaTime);

                if (_collapseTimer >= CrumbleSinkDuration)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void ApplyToppleSweepDamage()
        {
            Vector3 forward = _toppleForwardFlat.sqrMagnitude > 0.01f ? _toppleForwardFlat : Vector3.forward;
            Vector3 sweepHalf = new Vector3(_wallWidth * 0.5f + 0.35f, _wallHeight * 0.5f + 0.25f, _wallThickness * 0.5f + 0.65f);
            Collider[] cols = Physics.OverlapBox(transform.position, sweepHalf, transform.rotation);

            for (int i = 0; i < cols.Length; i++)
            {
                Unit target = cols[i].GetComponentInParent<Unit>();
                if (target == null || target.dead || target.Team == _ownerTeam)
                {
                    continue;
                }

                int id = target.GetInstanceID();
                if (!_toppleDamagedUnits.Add(id))
                {
                    continue;
                }

                Vector3 slamDir = (forward * (_toppleSign * 0.72f) + Vector3.down * 0.69f).normalized;
                if (target.data != null && target.data.healthHandler != null)
                {
                    target.data.healthHandler.TakeDamage(ToppleCrushDamage, slamDir * 18f, _owner, DamageType.Blunt);
                }

                if (target.data != null && target.data.mainRig != null)
                {
                    target.data.mainRig.AddForce(slamDir * 1020f, ForceMode.Impulse);
                }

                TrumpWallAbility.SpawnOfficialSmokeVfx(
                    target.transform.position + Vector3.up * 0.35f,
                    Quaternion.LookRotation(forward, Vector3.up),
                    1.0f,
                    1,
                    0.55f,
                    0.75f);
            }
        }

        private void TriggerToppleSlam()
        {
            if (_didToppleSlam)
            {
                return;
            }

            _didToppleSlam = true;
            _collapseState = CollapseState.Slammed;
            _collapseTimer = 0f;

            Vector3 forward = _toppleForwardFlat.sqrMagnitude > 0.01f ? _toppleForwardFlat : Vector3.forward;

            Vector3 fallenCenter = _topplePivotWorld + forward * (_toppleSign * _wallHeight * 0.5f);
            fallenCenter.y = _groundY + 0.14f;

            Quaternion flatRot = Quaternion.LookRotation(forward, Vector3.up);

            if (ScreenShake.Instance != null)
            {
                try
                {
                    ScreenShake.Instance.AddForce(new Vector3(0f, -2.0f, 0f), fallenCenter);
                }
                catch
                {
                }
            }

            TrumpWallAbility.PlayWallImpactSound(fallenCenter);
            TrumpWallAbility.SpawnOfficialImpactVfx(fallenCenter, flatRot, _wallWidth * 0.8f, 3, 1.2f);
            TrumpWallAbility.SpawnOfficialSmokeVfx(fallenCenter, flatRot, _wallWidth * 0.92f, 4, 1.05f, 1.45f);
            SpawnBrickDebris(fallenCenter, flatRot, 20, 0.85f);
            ApplyToppleCrushDamage(fallenCenter, flatRot, forward);
        }

        private void ApplyToppleCrushDamage(Vector3 fallenCenter, Quaternion flatRot, Vector3 forward)
        {
            Vector3 halfExtents = new Vector3(_wallWidth * 0.5f + 0.35f, 1.45f, _wallHeight * 0.5f + 0.35f);
            Collider[] cols = Physics.OverlapBox(fallenCenter + Vector3.up * 0.55f, halfExtents, flatRot);

            for (int i = 0; i < cols.Length; i++)
            {
                Unit target = cols[i].GetComponentInParent<Unit>();
                if (target == null || target.dead || target.Team == _ownerTeam)
                {
                    continue;
                }

                int id = target.GetInstanceID();
                if (!_toppleDamagedUnits.Add(id))
                {
                    continue;
                }

                Vector3 slamDir = (Vector3.down * 0.7f + forward * (_toppleSign * 0.65f)).normalized;
                if (target.data != null && target.data.healthHandler != null)
                {
                    target.data.healthHandler.TakeDamage(ToppleCrushDamage, slamDir * 18f, _owner, DamageType.Blunt);
                }

                if (target.data != null && target.data.mainRig != null)
                {
                    target.data.mainRig.AddForce(slamDir * 950f, ForceMode.Impulse);
                }
            }
        }

        private void OnDestroy()
        {
            if (_navCut != null)
            {
                ClearNavmeshObstacle();
            }
        }

        private void ApplyPartialSideCrushDamage(float lowSideSign)
        {
            Vector3 right = transform.rotation * Vector3.right;
            Vector3 halfCenter = new Vector3(transform.position.x, _groundY + _wallHeight * 0.40f, transform.position.z)
                + right * (lowSideSign * _wallWidth * 0.25f);
            Vector3 halfExtents = new Vector3(_wallWidth * 0.30f, _wallHeight * 0.45f + 0.6f, _wallThickness * 0.5f + 0.75f);
            Collider[] cols = Physics.OverlapBox(halfCenter, halfExtents, transform.rotation);

            for (int i = 0; i < cols.Length; i++)
            {
                Unit target = cols[i].GetComponentInParent<Unit>();
                if (target != null)
                {
                    DamageUnitIfEnemy(target, _impactDamage, 780f);
                }
            }
        }

        private void ApplyAreaCrushDamage()
        {
            Vector3 boxCenter = new Vector3(transform.position.x, _groundY + _wallHeight * 0.45f, transform.position.z);
            Vector3 directHalf = new Vector3(_wallWidth * 0.5f + 0.25f, _wallHeight * 0.5f + 0.8f, _wallThickness * 0.5f + 0.75f);
            Collider[] directCols = Physics.OverlapBox(boxCenter, directHalf, transform.rotation);

            for (int i = 0; i < directCols.Length; i++)
            {
                Unit target = directCols[i].GetComponentInParent<Unit>();
                if (target != null)
                {
                    DamageUnitIfEnemy(target, _impactDamage, 780f);
                }
            }

            Vector3 shockCenter = new Vector3(transform.position.x, _groundY + 1.0f, transform.position.z);
            Vector3 shockHalf = new Vector3(_wallWidth * 0.5f + 0.55f, 1.55f, _wallThickness * 0.5f + 1.95f);
            Collider[] shockCols = Physics.OverlapBox(shockCenter, shockHalf, transform.rotation);

            for (int i = 0; i < shockCols.Length; i++)
            {
                Unit target = shockCols[i].GetComponentInParent<Unit>();
                if (target != null)
                {
                    DamageUnitIfEnemy(target, 55f, 440f);
                }
            }
        }

        private void DamageUnitIfEnemy(Unit target, float damageAmount, float knockbackForce)
        {
            if (target == null || target.dead || target.Team == _ownerTeam)
            {
                return;
            }

            int id = target.GetInstanceID();
            if (!_damagedUnits.Add(id))
            {
                return;
            }

            Vector3 wallForward = transform.rotation * Vector3.forward;
            Vector3 offset = target.transform.position - transform.position;
            float sideSign = Vector3.Dot(offset, wallForward) >= 0f ? 1f : -1f;
            Vector3 pushDir = (wallForward * sideSign + Vector3.up * 0.42f).normalized;

            if (target.data != null && target.data.healthHandler != null)
            {
                target.data.healthHandler.TakeDamage(damageAmount, pushDir * 14f, _owner, DamageType.Blunt);
            }

            if (target.data != null && target.data.mainRig != null)
            {
                target.data.mainRig.AddForce((pushDir + Vector3.up * 0.30f) * knockbackForce, ForceMode.Impulse);
            }
        }

        private void SpawnBrickDebris(Vector3 pos, Quaternion rot, int count, float forceScale)
        {
            Material brickMat = TrumpWallAbility.GetBrickMaterial();
            Material concreteMat = TrumpWallAbility.GetConcreteMaterial();
            Vector3 right = rot * Vector3.right;
            Vector3 forward = rot * Vector3.forward;

            for (int i = 0; i < count; i++)
            {
                GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "TrumpBrickDebris_" + i;
                chunk.layer = 0;

                float xOff = UnityEngine.Random.Range(-_wallWidth * 0.48f, _wallWidth * 0.48f);
                float zSign = (i % 2 == 0) ? 1f : -1f;
                chunk.transform.position = pos + right * xOff + forward * (zSign * (_wallThickness * 0.5f + 0.35f)) + Vector3.up * 0.3f;
                chunk.transform.rotation = UnityEngine.Random.rotation;

                float s = UnityEngine.Random.Range(0.18f, 0.44f);
                chunk.transform.localScale = new Vector3(s * 1.45f, s * 0.68f, s);

                Renderer r = chunk.GetComponent<Renderer>();
                if (r != null)
                {
                    if (i % 4 == 0 && concreteMat != null)
                    {
                        r.sharedMaterial = concreteMat;
                    }
                    else if (brickMat != null)
                    {
                        r.sharedMaterial = brickMat;
                    }
                    else
                    {
                        r.material.color = new Color(0.74f, 0.32f, 0.23f, 1f);
                    }
                }

                Rigidbody rb = chunk.AddComponent<Rigidbody>();
                rb.mass = 2.0f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                Vector3 eject = forward * (zSign * UnityEngine.Random.Range(4.0f, 10.5f) * forceScale)
                    + right * UnityEngine.Random.Range(-3.5f, 3.5f) * forceScale
                    + Vector3.up * UnityEngine.Random.Range(4.5f, 9.5f) * forceScale;
                rb.AddForce(eject, ForceMode.VelocityChange);
                rb.AddTorque(UnityEngine.Random.insideUnitSphere * 14f, ForceMode.VelocityChange);

                TrumpDebrisPiece piece = chunk.AddComponent<TrumpDebrisPiece>();
                piece.Initialize(2.3f);
            }
        }
    }

    internal class TrumpAutoDestroyVfx : MonoBehaviour
    {
        private float _lifetime;
        private float _age;

        public void Initialize(float lifetime)
        {
            _lifetime = Mathf.Max(0.5f, lifetime);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _lifetime)
            {
                Destroy(gameObject);
            }
        }
    }

    internal class TrumpDebrisPiece : MonoBehaviour
    {
        private float _lifetime;
        private float _age;
        private Vector3 _initialScale;

        public void Initialize(float lifetime)
        {
            _lifetime = lifetime;
            _initialScale = transform.localScale;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age > _lifetime * 0.7f)
            {
                float fadeT = Mathf.Clamp01((_age - _lifetime * 0.7f) / (_lifetime * 0.3f));
                transform.localScale = Vector3.Lerp(_initialScale, Vector3.zero, fadeT);
            }

            if (_age >= _lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
