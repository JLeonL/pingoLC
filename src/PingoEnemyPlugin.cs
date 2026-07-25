using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace PingoEnemy;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("evaisa.lethallib", BepInDependency.DependencyFlags.HardDependency)]
public sealed class PingoEnemyPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "JLeonL.PingoEnemy";
    public const string PluginName = "Pingo Enemy";
    public const string PluginVersion = "1.0.1";

    internal static ManualLogSource Log = null!;
    internal static PingoEnemyPlugin Instance = null!;
    internal static AssetBundle? Bundle;
    internal static AudioClip? PingoClip;
    internal static EnemyType? RegisteredEnemyType;
    internal static GameObject? RegisteredEnemyPrefab;
    internal static Texture2D? LuigiBodyTexture;
    internal static Texture2D? LuigiBodyNormalTexture;
    internal static Texture2D? LuigiEyeTexture;
    internal static Texture2D? LuigiEye1Texture;
    internal static Texture2D? LuigiEye2Texture;

    private static ConfigEntry<int> spawnWeight = null!;
    private static ConfigEntry<bool> enableDebugSpawnKey = null!;
    private static ConfigEntry<bool> forceSpawnAfterLanding = null!;
    private static ConfigEntry<float> minimumVisualHeight = null!;
    private static ConfigEntry<bool> forceTestSpawnInFrontOfPlayer = null!;
    private static ConfigEntry<bool> forceVisibleFallbackBody = null!;

    private static bool spawnedAfterLanding;
    private float nextDebugSpawnAllowedAt;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        BindConfig();
        Harmony.CreateAndPatchAll(typeof(PingoEnemyPlugin).Assembly, PluginGuid);

        try
        {
            LoadAssets();
            StartCoroutine(LoadAudioFromDisk());
            RegisterEnemy();
            Logger.LogInfo($"{PluginName} loaded.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load {PluginName}: {ex}");
        }
    }

    private void Update()
    {
        if (enableDebugSpawnKey.Value && DebugSpawnKeyPressed() && Time.time >= nextDebugSpawnAllowedAt)
        {
            nextDebugSpawnAllowedAt = Time.time + 1f;
            SpawnPingoNearLocalPlayer("debug hotkey");
        }

        if (!forceSpawnAfterLanding.Value || spawnedAfterLanding)
        {
            return;
        }

        if (StartOfRound.Instance == null || !StartOfRound.Instance.shipHasLanded)
        {
            return;
        }

        spawnedAfterLanding = true;
        SpawnPingoNearLocalPlayer("force spawn after landing");
    }

    internal static bool ForceSpawnAfterLandingEnabled => forceSpawnAfterLanding.Value;

    internal static void ResetForcedSpawnForNewRound()
    {
        spawnedAfterLanding = false;
        Log.LogInfo("Reset Pingo forced spawn state for new round.");
    }

    internal static void TryForceSpawnAfterLanding(string reason)
    {
        if (!ForceSpawnAfterLandingEnabled)
        {
            Log.LogInfo($"Skipped forced Pingo spawn for {reason}: config disabled.");
            return;
        }

        if (spawnedAfterLanding)
        {
            Log.LogInfo($"Skipped forced Pingo spawn for {reason}: already spawned this round.");
            return;
        }

        spawnedAfterLanding = true;
        SpawnPingoNearLocalPlayer(reason);
    }

    private void BindConfig()
    {
        spawnWeight = Config.Bind("Spawning", "SpawnWeight", 175, "Normal LethalLib spawn weight for Pingo. Default is roughly x5 a common enemy weight.");
        enableDebugSpawnKey = Config.Bind("Testing", "EnableDebugSpawnKey", false, "If true, the host can press F6 in a round to spawn Pingo near the local player.");
        forceSpawnAfterLanding = Config.Bind("Testing", "ForceSpawnAfterLanding", false, "If true, the host spawns one Pingo near the local player after the ship lands.");
        forceTestSpawnInFrontOfPlayer = Config.Bind("Testing", "ForceTestSpawnInFrontOfPlayer", true, "If true, forced/debug spawns appear directly in front of the local player instead of snapping to dungeon NavMesh.");
        minimumVisualHeight = Config.Bind("Visuals", "MinimumVisualHeight", 2.1f, "Minimum world-space height for Pingo's visible model. A player is roughly this height.");
        forceVisibleFallbackBody = Config.Bind("Visuals", "ForceVisibleFallbackBody", false, "If true, adds a simple player-sized visible body so Pingo can never be invisible while testing.");
    }

    internal static float MinimumVisualHeight => Mathf.Max(1.8f, minimumVisualHeight.Value);
    internal static bool ForceTestSpawnInFrontOfPlayer => forceTestSpawnInFrontOfPlayer.Value;
    internal static bool ForceVisibleFallbackBody => forceVisibleFallbackBody.Value;

    private static bool DebugSpawnKeyPressed()
    {
        if (Input.GetKeyDown(KeyCode.F6))
        {
            return true;
        }

        var keyboard = Keyboard.current;
        return keyboard?.f6Key.wasPressedThisFrame == true;
    }

    private static void LoadAssets()
    {
        var pluginDir = Path.GetDirectoryName(typeof(PingoEnemyPlugin).Assembly.Location)!;
        var bundlePath = Path.Combine(pluginDir, "pingoenemyassets");

        if (File.Exists(bundlePath))
        {
            Bundle = AssetBundle.LoadFromFile(bundlePath);
            PingoClip = Bundle?.LoadAsset<AudioClip>("pingo");
            LuigiBodyTexture = Bundle?.LoadAsset<Texture2D>("pc02_body");
            LuigiBodyNormalTexture = Bundle?.LoadAsset<Texture2D>("pc02_body_nml");
            LuigiEyeTexture = Bundle?.LoadAsset<Texture2D>("pc02_eye");
            LuigiEye1Texture = Bundle?.LoadAsset<Texture2D>("Luigi_eye_1");
            LuigiEye2Texture = Bundle?.LoadAsset<Texture2D>("Luigi_Eye2");
            Log.LogInfo(Bundle == null
                ? $"Failed to load AssetBundle from {bundlePath}"
                : $"Loaded AssetBundle from {bundlePath}");
            Log.LogInfo(PingoClip == null
                ? "AssetBundle did not contain an AudioClip named 'pingo'."
                : $"Loaded AudioClip '{PingoClip.name}' from AssetBundle.");
            Log.LogInfo($"Loaded texture assets: body={LuigiBodyTexture != null}; normal={LuigiBodyNormalTexture != null}; pcEye={LuigiEyeTexture != null}; luigiEye1={LuigiEye1Texture != null}; luigiEye2={LuigiEye2Texture != null}.");
        }
        else
        {
            Log.LogWarning($"Missing AssetBundle at {bundlePath}; Pingo will use the fallback prefab.");
        }

        if (PingoClip == null)
        {
            Log.LogInfo("No AssetBundle audio found. Falling back to direct pingo.mp3 loading.");
        }
    }

    private static IEnumerator LoadAudioFromDisk()
    {
        if (PingoClip != null)
        {
            yield break;
        }

        var pluginDir = Path.GetDirectoryName(typeof(PingoEnemyPlugin).Assembly.Location)!;
        var audioPath = Path.Combine(pluginDir, "pingo.mp3");
        if (!File.Exists(audioPath))
        {
            Log.LogWarning($"Missing audio file: {audioPath}");
            yield break;
        }

        using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(audioPath).AbsoluteUri, AudioType.MPEG);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Log.LogWarning($"Could not load pingo.mp3: {request.error}");
            yield break;
        }

        PingoClip = DownloadHandlerAudioClip.GetContent(request);
        PingoClip.name = "pingo";
        Log.LogInfo("Loaded pingo.mp3 directly from plugin folder.");
    }

    private static void RegisterEnemy()
    {
        var prefab = Bundle?.LoadAsset<GameObject>("PingoEnemy");
        if (prefab == null)
        {
            Log.LogWarning("AssetBundle did not contain a GameObject named 'PingoEnemy'; using fallback prefab.");
            prefab = CreatePlaceholderPrefab();
        }
        else
        {
            Log.LogInfo($"Loaded enemy prefab '{prefab.name}' from AssetBundle.");
        }

        EnsureEnemyComponents(prefab);
        prefab.name = "Pingo";
        RegisteredEnemyPrefab = prefab;

        var enemyType = ScriptableObject.CreateInstance<EnemyType>();
        enemyType.name = "Pingo";
        enemyType.enemyName = "Pingo";
        enemyType.enemyPrefab = prefab;
        enemyType.MaxCount = 1;
        enemyType.PowerLevel = 0;
        enemyType.probabilityCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        enemyType.numberSpawnedFalloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        enemyType.isOutsideEnemy = false;
        enemyType.isDaytimeEnemy = false;
        enemyType.normalizedTimeInDayToLeave = 1f;
        enemyType.stunTimeMultiplier = 1f;
        enemyType.canSeeThroughFog = false;
        prefab.GetComponent<PingoEnemyAI>().enemyType = enemyType;

        var terminalNode = ScriptableObject.CreateInstance<TerminalNode>();
        terminalNode.creatureName = "Pingo";
        terminalNode.displayText = "Pingo\n\nNo mata, no persigue y no se mueve. Solo se queda en la sala haciendo ruido con cada vez mas insistencia.\n";
        terminalNode.clearPreviousText = true;
        terminalNode.maxCharactersToType = 2000;

        var terminalKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
        terminalKeyword.word = "pingo";
        terminalKeyword.isVerb = false;

        RegisteredEnemyType = enemyType;
        Enemies.RegisterEnemy(enemyType, Mathf.Max(0, spawnWeight.Value), Levels.LevelTypes.All, terminalNode, terminalKeyword);
        TryRegisterNetworkPrefab("RegisterEnemy");
    }

    internal static void TryRegisterNetworkPrefab(string reason)
    {
        if (RegisteredEnemyPrefab == null)
        {
            Log.LogWarning($"Cannot register Pingo network prefab for {reason}: prefab is null.");
            return;
        }

        var networkObject = RegisteredEnemyPrefab.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Log.LogWarning($"Cannot register Pingo network prefab for {reason}: prefab has no NetworkObject.");
            return;
        }

        if (NetworkManager.Singleton == null)
        {
            Log.LogInfo($"Deferred Pingo network prefab registration for {reason}: NetworkManager.Singleton is null.");
            return;
        }

        try
        {
            NetworkManager.Singleton.AddNetworkPrefab(RegisteredEnemyPrefab);
            Log.LogInfo($"Registered Pingo network prefab with Netcode for {reason}. prefab={RegisteredEnemyPrefab.name}; networkObject={networkObject != null}");
        }
        catch (Exception ex)
        {
            var message = ex.Message ?? string.Empty;
            if (message.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log.LogInfo($"Pingo network prefab was already registered for {reason}.");
                return;
            }

            Log.LogWarning($"Could not register Pingo network prefab for {reason}: {ex}");
        }
    }

    internal static bool SpawnPingoNearLocalPlayer(string reason)
    {
        if (NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer))
        {
            Log.LogInfo($"Ignored Pingo spawn request from non-host client: {reason}");
            return false;
        }

        if (RoundManager.Instance == null)
        {
            Log.LogInfo($"Ignored Pingo spawn request before a round manager exists: {reason}");
            return false;
        }

        if (RegisteredEnemyType == null)
        {
            Log.LogWarning($"Cannot spawn Pingo for {reason}: enemy type is not registered.");
            return false;
        }

        var player = GameNetworkManager.Instance?.localPlayerController;
        if (player == null)
        {
            Log.LogWarning($"Cannot spawn Pingo for {reason}: no local player.");
            return false;
        }

        var spawnPosition = GetSafeSpawnPositionInFrontOfPlayer(player);
        if (!ForceTestSpawnInFrontOfPlayer && NavMesh.SamplePosition(spawnPosition, out var hit, 8f, NavMesh.AllAreas))
        {
            spawnPosition = hit.position;
        }
        else if (!ForceTestSpawnInFrontOfPlayer)
        {
            Log.LogWarning($"Could not find nearby NavMesh for Pingo debug spawn; using raw position {spawnPosition}.");
        }

        var yRotation = player.transform.eulerAngles.y + 180f;

        RoundManager.Instance.SpawnEnemyGameObject(spawnPosition, yRotation, -1, RegisteredEnemyType);
        Log.LogInfo($"Spawned Pingo near local player for {reason}.");
        return true;
    }

    private static Vector3 GetSafeSpawnPositionInFrontOfPlayer(PlayerControllerB player)
    {
        var camera = player.gameplayCamera;
        var origin = camera != null ? camera.transform.position : player.transform.position + Vector3.up * 1.6f;
        var forward = camera != null ? camera.transform.forward : player.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = player.transform.forward;
        }

        var desired = origin + forward.normalized * 3f;
        desired.y = origin.y + 1f;

        if (Physics.Raycast(desired, Vector3.down, out var hit, 12f, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
        {
            desired = hit.point + Vector3.up * 0.05f;
        }
        else
        {
            desired.y = player.transform.position.y + 0.05f;
        }

        Log.LogInfo($"Pingo safe test spawn position: player={player.transform.position}; spawn={desired}; forceInFront={ForceTestSpawnInFrontOfPlayer}.");
        return desired;
    }

    private static GameObject CreatePlaceholderPrefab()
    {
        var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemy.name = "Pingo";
        enemy.transform.localScale = new Vector3(0.8f, 1.25f, 0.8f);
        UnityEngine.Object.DontDestroyOnLoad(enemy);
        enemy.SetActive(false);
        return enemy;
    }

    private static void EnsureEnemyComponents(GameObject prefab)
    {
        if (prefab.GetComponent<NetworkObject>() == null)
        {
            prefab.AddComponent<NetworkObject>();
        }

        var ai = prefab.GetComponent<PingoEnemyAI>() ?? prefab.AddComponent<PingoEnemyAI>();
        ai.enemyType = null;

        var agent = prefab.GetComponent<NavMeshAgent>() ?? prefab.AddComponent<NavMeshAgent>();
        agent.speed = 0f;
        agent.angularSpeed = 0f;
        agent.acceleration = 0f;
        agent.stoppingDistance = 0f;
        agent.updatePosition = false;
        agent.updateRotation = false;

        var source = prefab.GetComponent<AudioSource>() ?? prefab.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialize = false;
        source.spatializePostEffects = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 4f;
        source.maxDistance = 35f;

        EnsureScanNode(prefab);
    }

    private static void EnsureScanNode(GameObject prefab)
    {
        var scanNode = prefab.GetComponentInChildren<ScanNodeProperties>(true);
        GameObject scanObject;
        if (scanNode == null)
        {
            scanObject = new GameObject("PingoScanNode");
            scanObject.transform.SetParent(prefab.transform, false);
            scanNode = scanObject.AddComponent<ScanNodeProperties>();
        }
        else
        {
            scanObject = scanNode.gameObject;
        }

        scanObject.name = "PingoScanNode";
        scanObject.transform.localPosition = Vector3.up * 1.15f;
        scanObject.transform.localRotation = Quaternion.identity;
        scanObject.transform.localScale = Vector3.one;

        var scanLayer = LayerMask.NameToLayer("ScanNode");
        if (scanLayer >= 0)
        {
            scanObject.layer = scanLayer;
        }

        var collider = scanObject.GetComponent<SphereCollider>() ?? scanObject.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 1.25f;

        scanNode.headerText = "Pingo";
        scanNode.subText = "Enemy";
        scanNode.nodeType = 1;
        scanNode.creatureScanID = -1;
        scanNode.minRange = 1;
        scanNode.maxRange = 80;
        scanNode.requiresLineOfSight = false;
    }
}

public sealed class PingoEnemyAI : EnemyAI
{
    private const float BaseInterval = 16f;
    private const float MinimumInterval = 1.25f;
    private const float SameRoomMultiplier = 0.45f;
    private const float LookingMultiplier = 0.55f;
    private const float NearVolumeRadius = 18f;
    private const float NearVolumeStepSeconds = 10f;
    private const float NearVolumeStepGain = 0.1f;
    private const float OverlapRampStartSeconds = 60f;
    private const float IntervalReductionPerNearPlay = 0.1f;
    private const float OverlapMinimumInterval = 0.1f;
    private const int MinimumIntervalPlaysBeforeReset = 30;

    private AudioSource? source;
    private float nextNoiseAt;
    private float aliveForSeconds;
    private float nearPlayerSeconds;
    private float accumulatedIntervalReduction;
    private int minimumIntervalPlayCount;

    public override void Start()
    {
        base.Start();
        source = GetComponent<AudioSource>();
        if (source != null && PingoEnemyPlugin.PingoClip != null)
        {
            source.clip = PingoEnemyPlugin.PingoClip;
            source.spatialize = false;
            source.spatializePostEffects = false;
            source.volume = 1f;
        }

        gameObject.name = "Pingo";
        EnsureRuntimeScanNode();
        movingTowardsTargetPlayer = false;
        nextNoiseAt = Time.time + 3f;
        EnsureVisibleFallbackBody();
        ApplyLuigiMaterials();
        PingoEnemyPlugin.Log.LogInfo($"PingoEnemyAI started at {transform.position}; hasAudio={PingoEnemyPlugin.PingoClip != null}; isOwner={IsOwner}.");
        StartCoroutine(NormalizeVisualsAfterSpawn());
    }

    private void ApplyLuigiMaterials()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            PingoEnemyPlugin.Log.LogWarning("Cannot apply Luigi materials: Pingo has no renderers.");
            return;
        }

        var bodyMaterial = CreateOpaqueMaterial("Pingo_Luigi_Body_Runtime", PingoEnemyPlugin.LuigiBodyTexture, PingoEnemyPlugin.LuigiBodyNormalTexture, Color.white);
        var eyeMaterial = CreateEyeMaterial("Pingo_Luigi_Eyes_Runtime", PingoEnemyPlugin.LuigiEyeTexture);
        var skinMaterial = CreateOpaqueMaterial("Pingo_Luigi_Skin_Runtime", null, null, new Color(1f, 0.72f, 0.52f, 1f));
        var gloveMaterial = CreateOpaqueMaterial("Pingo_Luigi_Gloves_Runtime", null, null, Color.white);
        var shoeMaterial = CreateOpaqueMaterial("Pingo_Luigi_Shoes_Runtime", null, null, new Color(0.35f, 0.16f, 0.06f, 1f));

        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                var slotName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                var rendererName = renderer.name.ToLowerInvariant();
                materials[i] = ChooseLuigiMaterial(rendererName, slotName, i, bodyMaterial, eyeMaterial, skinMaterial, gloveMaterial, shoeMaterial);
            }

            renderer.sharedMaterials = materials;
            renderer.enabled = true;
        }

        PingoEnemyPlugin.Log.LogInfo($"Applied runtime Luigi materials to {renderers.Length} renderer(s).");
    }

    private static Material ChooseLuigiMaterial(string rendererName, string slotName, int materialIndex, Material body, Material eyes, Material skin, Material gloves, Material shoes)
    {
        var materialKey = rendererName + " " + slotName;

        if (rendererName == "eye" || materialKey.Contains("eye"))
        {
            return eyes;
        }

        if (rendererName.StartsWith("newluigi_m1shape") && materialIndex >= 3 && materialIndex <= 6)
        {
            return eyes;
        }

        if (materialKey.Contains("hand") || materialKey.Contains("glove"))
        {
            return gloves;
        }

        if (materialKey.Contains("shoe") || materialKey.Contains("boot"))
        {
            return shoes;
        }

        if (materialKey.Contains("skin") || materialKey.Contains("face") || materialKey.Contains("head") || materialKey.Contains("nose") || materialKey.Contains("ear"))
        {
            return skin;
        }

        return body;
    }

    private static Material CreateOpaqueMaterial(string name, Texture2D? baseTexture, Texture2D? normalTexture, Color color)
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader)
        {
            name = name,
            color = color
        };

        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_TransparentSortPriority", 0f);
        material.renderQueue = -1;

        if (baseTexture != null)
        {
            SetTexture(material, "_BaseColorMap", baseTexture);
            SetTexture(material, "_MainTex", baseTexture);
        }

        if (normalTexture != null)
        {
            SetTexture(material, "_NormalMap", normalTexture);
            SetTexture(material, "_BumpMap", normalTexture);
            material.EnableKeyword("_NORMALMAP");
        }

        return material;
    }

    private static Material CreateEyeMaterial(string name, Texture2D? eyeTexture)
    {
        ConfigureEyeTexture(eyeTexture);
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var eyeColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        var material = new Material(shader)
        {
            name = name,
            color = eyeColor
        };

        SetColor(material, "_BaseColor", eyeColor);
        SetColor(material, "_Color", eyeColor);
        SetColor(material, "_UnlitColor", eyeColor);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_Smoothness", 0.35f);
        SetFloat(material, "_Metallic", 0f);
        material.renderQueue = -1;

        if (eyeTexture != null)
        {
            SetTexture(material, "_BaseColorMap", eyeTexture);
            SetTexture(material, "_UnlitColorMap", eyeTexture);
            SetTexture(material, "_MainTex", eyeTexture);
            SetTexture(material, "_EmissionMap", eyeTexture);
            SetTexture(material, "_EmissiveColorMap", eyeTexture);
            SetTextureScale(material, "_BaseColorMap", Vector2.one);
            SetTextureScale(material, "_UnlitColorMap", Vector2.one);
            SetTextureScale(material, "_MainTex", Vector2.one);
            SetTextureOffset(material, "_BaseColorMap", Vector2.zero);
            SetTextureOffset(material, "_UnlitColorMap", Vector2.zero);
            SetTextureOffset(material, "_MainTex", Vector2.zero);
        }

        if (material.HasProperty("_EmissiveColor"))
        {
            material.SetColor("_EmissiveColor", new Color(0.18f, 0.18f, 0.18f, 1f));
        }
        SetColor(material, "_EmissionColor", new Color(0.18f, 0.18f, 0.18f, 1f));
        if (material.HasProperty("_EmissiveIntensity"))
        {
            material.SetFloat("_EmissiveIntensity", 0.2f);
        }
        material.EnableKeyword("_EMISSION");

        return material;
    }

    private static void ConfigureEyeTexture(Texture2D? texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.wrapModeU = TextureWrapMode.Repeat;
        texture.wrapModeV = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
    }

    private static void SetTexture(Material material, string property, Texture texture)
    {
        if (material.HasProperty(property))
        {
            material.SetTexture(property, texture);
        }
    }

    private static void SetTextureScale(Material material, string property, Vector2 scale)
    {
        if (material.HasProperty(property))
        {
            material.SetTextureScale(property, scale);
        }
    }

    private static void SetTextureOffset(Material material, string property, Vector2 offset)
    {
        if (material.HasProperty(property))
        {
            material.SetTextureOffset(property, offset);
        }
    }

    private static void SetColor(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
        }
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private void EnsureVisibleFallbackBody()
    {
        if (!PingoEnemyPlugin.ForceVisibleFallbackBody || transform.Find("PingoVisibleFallbackBody") != null)
        {
            return;
        }

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "PingoVisibleFallbackBody";
        body.transform.SetParent(transform, false);
        body.transform.localPosition = Vector3.up * (PingoEnemyPlugin.MinimumVisualHeight * 0.5f);
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = new Vector3(0.75f, PingoEnemyPlugin.MinimumVisualHeight * 0.5f, 0.75f);

        var collider = body.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.Destroy(collider);
        }

        var renderer = body.GetComponent<Renderer>();
        if (renderer != null)
        {
            var material = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
            material.color = new Color(0.1f, 0.8f, 0.25f, 1f);
            renderer.material = material;
            renderer.enabled = true;
        }

        PingoEnemyPlugin.Log.LogInfo($"Added visible fallback body to Pingo. height={PingoEnemyPlugin.MinimumVisualHeight:0.00}; root={transform.position}.");
    }

    private IEnumerator NormalizeVisualsAfterSpawn()
    {
        yield return null;

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            PingoEnemyPlugin.Log.LogWarning("Pingo spawned with no renderers. The model will not be visible.");
            yield break;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        PingoEnemyPlugin.Log.LogInfo($"Pingo visual bounds before normalize: center={bounds.center}; size={bounds.size}; rootScale={transform.localScale}.");

        if (bounds.size.y > 0.01f && bounds.size.y < PingoEnemyPlugin.MinimumVisualHeight)
        {
            var scaleFactor = PingoEnemyPlugin.MinimumVisualHeight / bounds.size.y;
            transform.localScale *= scaleFactor;
            yield return null;

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            PingoEnemyPlugin.Log.LogInfo($"Scaled Pingo visual by {scaleFactor:0.00} to meet minimum height {PingoEnemyPlugin.MinimumVisualHeight:0.00}. New size={bounds.size}; rootScale={transform.localScale}.");
        }

        var targetBottomY = transform.position.y;
        if (bounds.min.y < targetBottomY - 0.05f)
        {
            var lift = targetBottomY - bounds.min.y;
            foreach (var renderer in renderers)
            {
                var rendererTransform = renderer.transform;
                if (rendererTransform == transform)
                {
                    continue;
                }

                rendererTransform.position += Vector3.up * lift;
            }

            PingoEnemyPlugin.Log.LogInfo($"Lifted Pingo visual renderers by {lift:0.00} so the model is not below the floor.");
        }
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        StopMoving();
    }

    public override void Update()
    {
        base.Update();
        StopMoving();

        if (!IsOwner || source == null || PingoEnemyPlugin.PingoClip == null)
        {
            return;
        }

        aliveForSeconds += Time.deltaTime;
        UpdateNearPlayerVolumeTimer();
        if (Time.time < nextNoiseAt)
        {
            return;
        }

        var interval = CalculateNoiseInterval();
        var nearOverlapActive = nearPlayerSeconds >= OverlapRampStartSeconds;
        var resetOverlapRampAfterPlay = false;
        if (nearOverlapActive)
        {
            interval = Mathf.Max(OverlapMinimumInterval, interval - accumulatedIntervalReduction);
            accumulatedIntervalReduction += IntervalReductionPerNearPlay;
            if (interval <= OverlapMinimumInterval + 0.001f)
            {
                minimumIntervalPlayCount++;
                if (minimumIntervalPlayCount >= MinimumIntervalPlaysBeforeReset)
                {
                    resetOverlapRampAfterPlay = true;
                }
            }
            else
            {
                minimumIntervalPlayCount = 0;
            }
        }
        else
        {
            accumulatedIntervalReduction = 0f;
            minimumIntervalPlayCount = 0;
        }

        nextNoiseAt = Time.time + interval;
        PlayPingoClientRpc(Mathf.Clamp01(1f - interval / BaseInterval), CalculateNearVolumeScale(), interval, nearPlayerSeconds);
        if (resetOverlapRampAfterPlay)
        {
            accumulatedIntervalReduction = 0f;
            minimumIntervalPlayCount = 0;
            PingoEnemyPlugin.Log.LogInfo("Pingo overlap loop played 30 times at minimum interval; resetting interval ramp.");
        }
    }

    public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        // Pingo is intentionally harmless and effectively indestructible.
    }

    [ClientRpc]
    private void PlayPingoClientRpc(float intensity, float volumeScale, float nextInterval, float nearbySeconds)
    {
        if (source == null || PingoEnemyPlugin.PingoClip == null)
        {
            return;
        }

        source.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
        source.volume = 1f;
        var baseVolume = Mathf.Lerp(0.45f, 1f, intensity);
        var finalVolume = baseVolume * Mathf.Max(1f, volumeScale);
        source.PlayOneShot(PingoEnemyPlugin.PingoClip, finalVolume);
        PingoEnemyPlugin.Log.LogInfo($"Pingo played sound. intensity={intensity:0.00}; volumeScale={volumeScale:0.00}; finalVolume={finalVolume:0.00}; nextInterval={nextInterval:0.00}; nearbySeconds={nearbySeconds:0.0}; position={transform.position}.");
    }

    private void UpdateNearPlayerVolumeTimer()
    {
        if (AnyPlayerWithinDistance(NearVolumeRadius))
        {
            nearPlayerSeconds += Time.deltaTime;
        }
    }

    private void EnsureRuntimeScanNode()
    {
        var scanNode = GetComponentInChildren<ScanNodeProperties>(true);
        GameObject scanObject;
        if (scanNode == null)
        {
            scanObject = new GameObject("PingoScanNode");
            scanObject.transform.SetParent(transform, false);
            scanNode = scanObject.AddComponent<ScanNodeProperties>();
        }
        else
        {
            scanObject = scanNode.gameObject;
        }

        scanObject.name = "PingoScanNode";
        scanObject.transform.localPosition = Vector3.up * 1.15f;
        scanObject.transform.localRotation = Quaternion.identity;
        scanObject.transform.localScale = Vector3.one;

        var scanLayer = LayerMask.NameToLayer("ScanNode");
        if (scanLayer >= 0)
        {
            scanObject.layer = scanLayer;
        }

        var collider = scanObject.GetComponent<SphereCollider>() ?? scanObject.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 1.25f;

        scanNode.headerText = "Pingo";
        scanNode.subText = "Enemy";
        scanNode.nodeType = 1;
        scanNode.creatureScanID = -1;
        scanNode.minRange = 1;
        scanNode.maxRange = 80;
        scanNode.requiresLineOfSight = false;
    }

    private float CalculateNearVolumeScale()
    {
        var completedSteps = Mathf.Floor(nearPlayerSeconds / NearVolumeStepSeconds);
        return 1f + completedSteps * NearVolumeStepGain;
    }

    private float CalculateNoiseInterval()
    {
        var progress = Mathf.Clamp01(aliveForSeconds / 600f);
        var interval = Mathf.Lerp(BaseInterval, 4f, progress);

        foreach (var player in StartOfRound.Instance.allPlayerScripts)
        {
            if (player == null || !player.isPlayerControlled || player.isPlayerDead)
            {
                continue;
            }

            var distance = Vector3.Distance(player.transform.position, transform.position);
            if (distance <= NearVolumeRadius)
            {
                interval *= SameRoomMultiplier;
            }

            if (distance <= 35f && PlayerIsLookingAtPingo(player))
            {
                interval *= LookingMultiplier;
            }
        }

        return Mathf.Max(MinimumInterval, interval);
    }

    private bool AnyPlayerWithinDistance(float maxDistance)
    {
        if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null)
        {
            return false;
        }

        foreach (var player in StartOfRound.Instance.allPlayerScripts)
        {
            if (player == null || !player.isPlayerControlled || player.isPlayerDead)
            {
                continue;
            }

            if (Vector3.Distance(player.transform.position, transform.position) <= maxDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool PlayerIsLookingAtPingo(PlayerControllerB player)
    {
        var playerCamera = player.gameplayCamera;
        if (playerCamera == null)
        {
            return false;
        }

        var toPingo = (transform.position + Vector3.up - playerCamera.transform.position).normalized;
        return Vector3.Dot(playerCamera.transform.forward, toPingo) > 0.75f;
    }

    private void StopMoving()
    {
        if (agent == null)
        {
            return;
        }

        agent.speed = 0f;
        agent.velocity = Vector3.zero;
        agent.isStopped = true;
    }
}

[HarmonyPatch(typeof(StartOfRound))]
internal static class StartOfRoundPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("StartGame")]
    private static void StartGamePostfix()
    {
        PingoEnemyPlugin.ResetForcedSpawnForNewRound();
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnShipLandedMiscEvents")]
    private static void OnShipLandedMiscEventsPostfix()
    {
        PingoEnemyPlugin.Log.LogInfo("StartOfRound.OnShipLandedMiscEvents fired; checking forced Pingo spawn.");
        PingoEnemyPlugin.TryForceSpawnAfterLanding("OnShipLandedMiscEvents");
    }
}

[HarmonyPatch(typeof(GameNetworkManager))]
internal static class GameNetworkManagerPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("Start")]
    private static void StartPostfix()
    {
        PingoEnemyPlugin.TryRegisterNetworkPrefab("GameNetworkManager.Start");
    }
}
