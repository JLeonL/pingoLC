using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildPingoEnemyBundle
{
    private const string AssetRoot = "Assets/PingoEnemy";
    private const string BundleName = "pingoenemyassets";
    private const string PrefabPath = AssetRoot + "/PingoEnemy.prefab";
    private const string MaterialRoot = AssetRoot + "/Materials";
    private const string OutputDirectory = "AssetBundles";
    private const float TargetHeight = 2.1f;

    [MenuItem("Pingo Enemy/Build AssetBundle")]
    public static void Build()
    {
        Directory.CreateDirectory(OutputDirectory);
        CreateOrUpdatePrefab();
        BuildPipeline.BuildAssetBundles(OutputDirectory, BuildAssetBundleOptions.ChunkBasedCompression, EditorUserBuildSettings.activeBuildTarget);
        Debug.Log("Built " + BundleName + " into " + OutputDirectory);
    }

    public static void AnalyzeModel()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/Luigi Removed Doubles.fbx");
        if (model == null)
        {
            throw new FileNotFoundException("Missing Luigi FBX model.");
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "LuigiModel_Analysis";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Debug.Log("=== PINGO LUIGI MODEL ANALYSIS START ===");
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var mesh = GetRendererMesh(renderer);
            Debug.Log($"Renderer '{renderer.name}' type={renderer.GetType().Name} materials={renderer.sharedMaterials.Length} bounds={renderer.bounds}");
            if (mesh == null)
            {
                continue;
            }

            Debug.Log($"  Mesh '{mesh.name}' vertices={mesh.vertexCount} subMeshes={mesh.subMeshCount} bounds={mesh.bounds}");
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var materialName = submesh < renderer.sharedMaterials.Length && renderer.sharedMaterials[submesh] != null
                    ? renderer.sharedMaterials[submesh].name
                    : "<null>";
                var submeshInfo = AnalyzeSubmesh(mesh, submesh, renderer.transform.localToWorldMatrix);
                Debug.Log($"  Submesh {submesh}: material='{materialName}' {submeshInfo}");
            }
        }
        Debug.Log("=== PINGO LUIGI MODEL ANALYSIS END ===");
        Object.DestroyImmediate(instance);
    }

    public static void RenderPreview()
    {
        const string previewPath = "../pingo-model-preview.png";

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/Luigi Removed Doubles.fbx");
        if (model == null)
        {
            throw new FileNotFoundException("Missing Luigi FBX model.");
        }

        var root = new GameObject("PingoPreviewRoot");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "LuigiModel_Preview";
        instance.transform.SetParent(root.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        instance.transform.localScale = Vector3.one;

        AssignVisibleMaterials(instance);
        NormalizeModelTransform(instance);

        var bounds = CalculateBounds(instance);
        var cameraObject = new GameObject("PingoPreviewCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        camera.fieldOfView = 32f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 100f;
        camera.transform.position = bounds.center + new Vector3(0f, 0.1f, -5.2f);
        camera.transform.LookAt(bounds.center + Vector3.up * 0.1f);

        var lightObject = new GameObject("PingoPreviewLight");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

        var renderTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = renderTexture;
        camera.Render();

        RenderTexture.active = renderTexture;
        var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(previewPath, texture.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(renderTexture);
        Object.DestroyImmediate(lightObject);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(root);

        Debug.Log("Saved Pingo model preview to " + Path.GetFullPath(previewPath));
    }

    private static Bounds CalculateBounds(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private static Mesh GetRendererMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            return skinnedMeshRenderer.sharedMesh;
        }

        var meshFilter = renderer.GetComponent<MeshFilter>();
        return meshFilter != null ? meshFilter.sharedMesh : null;
    }

    private static string AnalyzeSubmesh(Mesh mesh, int submesh, Matrix4x4 localToWorld)
    {
        var triangles = mesh.GetTriangles(submesh);
        var vertices = mesh.vertices;
        var uvs = mesh.uv;
        if (triangles.Length == 0)
        {
            return "empty";
        }

        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var uvMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var uvMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        foreach (var vertexIndex in triangles)
        {
            var point = localToWorld.MultiplyPoint3x4(vertices[vertexIndex]);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);

            if (uvs != null && vertexIndex < uvs.Length)
            {
                var uv = uvs[vertexIndex];
                uvMin = Vector2.Min(uvMin, uv);
                uvMax = Vector2.Max(uvMax, uv);
            }
        }

        var center = (min + max) * 0.5f;
        var size = max - min;
        return $"triangles={triangles.Length / 3} center={center} size={size} uvMin={uvMin} uvMax={uvMax}";
    }

    private static void CreateOrUpdatePrefab()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/Luigi Removed Doubles.fbx");
        if (model == null)
        {
            throw new FileNotFoundException("Missing Luigi FBX model.");
        }

        var root = new GameObject("PingoEnemy");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "LuigiModel";
        instance.transform.SetParent(root.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        AssignVisibleMaterials(instance);
        NormalizeModelTransform(instance);

        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        SetBundleName(PrefabPath);
        SetBundleName(AssetRoot + "/Audio/pingo.mp3");
        SetBundleName(AssetRoot + "/Textures/pc02_body.png");
        SetBundleName(AssetRoot + "/Textures/pc02_body_nml.png");
        SetBundleName(AssetRoot + "/Textures/pc02_eye.png");
        SetBundleName(AssetRoot + "/Textures/Luigi_eye_1.png");
        SetBundleName(AssetRoot + "/Textures/Luigi_Eye2.png");
        SetBundleName(MaterialRoot + "/Pingo_Luigi_Body.mat");
        SetBundleName(MaterialRoot + "/Pingo_Luigi_Eyes.mat");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void AssignVisibleMaterials(GameObject instance)
    {
        var bodyTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetRoot + "/Textures/pc02_body.png");
        var normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetRoot + "/Textures/pc02_body_nml.png");
        var eyeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetRoot + "/Textures/pc02_eye.png");
        ConfigureEyeTexture(eyeTexture);
        EnsureMaterialFolder();
        var bodyMaterial = GetOrCreateMaterialAsset(MaterialRoot + "/Pingo_Luigi_Body.mat", () => CreateMaterial("Pingo_Luigi_Body", bodyTexture, normalTexture, Color.white));
        ApplyBodyMaterial(bodyMaterial, bodyTexture, normalTexture, Color.white);
        EditorUtility.SetDirty(bodyMaterial);
        var eyeMaterial = GetOrCreateMaterialAsset(MaterialRoot + "/Pingo_Luigi_Eyes.mat", () => CreateEyeMaterial("Pingo_Luigi_Eyes", eyeTexture));
        ApplyEyeMaterial(eyeMaterial, eyeTexture);
        EditorUtility.SetDirty(eyeMaterial);

        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                var slotName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                materials[i] = ShouldUseEyeMaterial(renderer, i, slotName) ? eyeMaterial : bodyMaterial;
            }

            renderer.sharedMaterials = materials;
        }
    }

    private static bool ShouldUseEyeMaterial(Renderer renderer, int materialIndex, string slotName)
    {
        var rendererName = renderer.name.ToLowerInvariant();
        if (rendererName == "eye" || slotName.Contains("eye"))
        {
            return true;
        }

        // The imported Luigi FBX does not preserve useful eye material names.
        // On the face mesh, submeshes 3/5 are the visible eye surfaces and
        // 4/6 are the tiny companion eye pieces. Keep those on pc02_eye only.
        if (rendererName.StartsWith("newluigi_m1shape"))
        {
            return materialIndex >= 3 && materialIndex <= 6;
        }

        return false;
    }

    private static Material CreateMaterial(string materialName, Texture2D texture, Texture2D normalTexture, Color color)
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader)
        {
            name = materialName,
            color = color
        };

        ApplyBodyMaterial(material, texture, normalTexture, color);
        return material;
    }

    private static void ApplyBodyMaterial(Material material, Texture2D texture, Texture2D normalTexture, Color color)
    {
        material.shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        material.name = "Pingo_Luigi_Body";
        material.color = color;
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);

        if (texture != null)
        {
            if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTexture("_BaseColorMap", texture);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        if (normalTexture != null)
        {
            if (material.HasProperty("_NormalMap"))
            {
                material.SetTexture("_NormalMap", normalTexture);
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normalTexture);
                material.EnableKeyword("_NORMALMAP");
            }
        }
    }

    private static Material CreateEyeMaterial(string materialName, Texture2D texture)
    {
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader)
        {
            name = materialName,
            color = Color.white
        };

        ApplyEyeMaterial(material, texture);
        return material;
    }

    private static void ApplyEyeMaterial(Material material, Texture2D texture)
    {
        var eyeColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        material.shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        material.name = "Pingo_Luigi_Eyes";
        material.color = eyeColor;
        SetTexture(material, "_BaseColorMap", texture);
        SetTexture(material, "_UnlitColorMap", texture);
        SetTexture(material, "_MainTex", texture);
        SetTexture(material, "_EmissionMap", texture);
        SetTexture(material, "_EmissiveColorMap", texture);
        SetTextureScale(material, "_BaseColorMap", Vector2.one);
        SetTextureScale(material, "_UnlitColorMap", Vector2.one);
        SetTextureScale(material, "_MainTex", Vector2.one);
        SetTextureOffset(material, "_BaseColorMap", Vector2.zero);
        SetTextureOffset(material, "_UnlitColorMap", Vector2.zero);
        SetTextureOffset(material, "_MainTex", Vector2.zero);
        SetColor(material, "_BaseColor", eyeColor);
        SetColor(material, "_Color", eyeColor);
        SetColor(material, "_UnlitColor", eyeColor);
        SetColor(material, "_EmissionColor", new Color(0.18f, 0.18f, 0.18f, 1f));
        SetColor(material, "_EmissiveColor", new Color(0.18f, 0.18f, 0.18f, 1f));
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_EmissiveIntensity", 0.2f);
        material.EnableKeyword("_EMISSION");
        material.renderQueue = -1;
    }

    private static Material GetOrCreateMaterialAsset(string path, System.Func<Material> factory)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = factory();
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialRoot))
        {
            AssetDatabase.CreateFolder(AssetRoot, "Materials");
        }
    }

    private static void ConfigureEyeTexture(Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.wrapModeU = TextureWrapMode.Repeat;
        texture.wrapModeV = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        EditorUtility.SetDirty(texture);
    }

    private static void SetTexture(Material material, string property, Texture texture)
    {
        if (texture != null && material.HasProperty(property))
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

    private static void NormalizeModelTransform(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new MissingComponentException("Luigi model has no renderers.");
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        if (bounds.size.y > 0.01f)
        {
            var scaleFactor = TargetHeight / bounds.size.y;
            instance.transform.localScale *= scaleFactor;
        }

        bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        instance.transform.position += Vector3.up * -bounds.min.y;
        Debug.Log($"Normalized Luigi model. Bounds before/after target height: target={TargetHeight}; finalBounds={bounds.size}");
    }

    private static void SetBundleName(string path)
    {
        var importer = AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            throw new FileNotFoundException("Missing asset: " + path);
        }

        importer.assetBundleName = BundleName;
    }
}
