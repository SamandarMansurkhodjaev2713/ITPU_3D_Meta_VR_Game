using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ITPU.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    // A reversible art candidate. Never rebuilds the environment or main scene.
    public static class VisualUpgradePatch
    {
        public const string SourceScene = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        public const string CandidateScene = "Assets/ITPU/Scenes/ITPU_403_Visual_001.unity";
        private const string Art = "Assets/ITPU/Art/VisualUpgrade001";
        private static string Workspace => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        private static string Reports => Path.Combine(Workspace, "reports/local/WO-31");
        private static string CaptureLabel = "after_bake";

        [Serializable] private sealed class Receipt
        {
            public string status, scene, unity, bake, quest;
            public int environmentRenderers, colliders, stableIds, xrOrigins, movers;
            public int bakedAreaLights, probePositions, lightmaps, uv1Meshes;
            public string[] notes;
        }

        [MenuItem("ITPU/Visual Upgrade/Create Candidate")]
        public static void CreateCandidate()
        {
            Directory.CreateDirectory(Reports);
            EnsureFolders();
            if (File.Exists(CandidateScene)) throw new InvalidOperationException("Candidate already exists; preserve saved WIP.");
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            Capture(scene, "before");
            var original = SceneContract();
            if (!EditorSceneManager.SaveScene(scene, CandidateScene, false))
                throw new InvalidOperationException("Candidate copy save failed");
            var environment = GameObject.Find("ENVIRONMENT_FULL_REFINED_005");
            if (environment == null) throw new InvalidOperationException("Accepted environment root missing");
            var renderers = environment.GetComponentsInChildren<MeshRenderer>(true);
            var materials = new Dictionary<Material, Material>();
            foreach (var renderer in renderers)
            {
                var assigned = renderer.sharedMaterials;
                for (var index = 0; index < assigned.Length; index++)
                {
                    var source = assigned[index];
                    if (source == null || source.name.Contains("board_drawable") || source.name.Contains("coffee_liquid")) continue;
                    if (!materials.TryGetValue(source, out var material))
                    {
                        material = Object.Instantiate(source);
                        material.name = source.name + "_visual001";
                        material.SetFloat("_ReceiveShadows", 1f);
                        material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                        material.enableInstancing = true;
                        material.globalIlluminationFlags = source.name.Contains("fixture")
                            ? MaterialGlobalIlluminationFlags.BakedEmissive
                            : MaterialGlobalIlluminationFlags.None;
                        AssetDatabase.CreateAsset(material, Art + "/Materials/" + material.name + ".mat");
                        StyleMaterial(source.name, material);
                        materials[source] = material;
                    }
                    assigned[index] = material;
                }
                renderer.sharedMaterials = assigned;
                renderer.receiveShadows = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }

            // Generate independent UV1 meshes, leaving FBX import settings/GUIDs untouched.
            var definitions = JsonUtility.FromJson<Transfer>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/ITPU/Data/itpu_environment_v005_transfer.json").text);
            var movable = new HashSet<string>(definitions.roots.Where(item => item.movable).Select(item => item.id));
            var meshes = new Dictionary<Mesh, Mesh>();
            var fixtures = new List<Vector3>();
            foreach (var renderer in renderers)
            {
                var id = renderer.GetComponentInParent<StableId>();
                var dynamic = id != null && movable.Contains(id.Value);
                if (renderer.GetComponentInParent<Rigidbody>() != null) dynamic = true;
                if (!dynamic)
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                        GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.ContributeGI);
                    renderer.receiveGI = ReceiveGI.Lightmaps;
                    renderer.scaleInLightmap = renderer.name.Contains("CEILING") ? 0.35f : 1f;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        var source = filter.sharedMesh;
                        if (!meshes.TryGetValue(source, out var mesh))
                        {
                            mesh = Object.Instantiate(source);
                            mesh.name = source.name + "_lightmap001";
                            if (!Unwrapping.GenerateSecondaryUVSet(mesh))
                                throw new InvalidOperationException("UV1 unwrap failed: " + source.name);
                            AssetDatabase.CreateAsset(mesh, Art + "/Meshes/" + meshes.Count.ToString("D4") + ".asset");
                            meshes[source] = mesh;
                        }
                        filter.sharedMesh = mesh;
                    }
                }
                else
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                        GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.ContributeGI);
                    renderer.receiveGI = ReceiveGI.LightProbes;
                }
                if (renderer.sharedMaterials.Any(item => item != null && item.name.Contains("fixture_emissive")))
                    fixtures.Add(renderer.bounds.center);
            }

            var lightRoot = new GameObject("ITPU_VISUAL001_BAKED_LIGHTS");
            for (var index = 0; index < fixtures.Count; index++)
            {
                var go = new GameObject("BAKED_FIXTURE_" + index.ToString("D2"));
                go.transform.SetParent(lightRoot.transform, false);
                go.transform.SetPositionAndRotation(fixtures[index] + Vector3.down * 0.10f, Quaternion.Euler(90f, 0f, 0f));
                var light = go.AddComponent<Light>();
                light.type = LightType.Rectangle;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.areaSize = new Vector2(1.25f, 0.35f);
                light.color = new Color(1f, 0.93f, 0.83f);
                light.intensity = 4f;
                light.range = 8f;
                light.shadows = LightShadows.Soft;
            }
            var probes = new GameObject("ITPU_VISUAL001_LIGHT_PROBES").AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            foreach (var floor in environment.GetComponentsInChildren<StableId>(true).Where(item => item.Value.StartsWith("FLOOR_")))
            {
                var collider = floor.GetComponentInChildren<Collider>();
                if (collider == null) continue;
                var bounds = collider.bounds;
                foreach (var x in new[] { bounds.min.x + 0.3f, bounds.center.x, bounds.max.x - 0.3f })
                foreach (var z in new[] { bounds.min.z + 0.3f, bounds.center.z, bounds.max.z - 0.3f })
                foreach (var y in new[] { 0.35f, 1.25f, 2.3f }) positions.Add(new Vector3(x, bounds.max.y + y, z));
            }
            probes.probePositions = positions.ToArray();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.40f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.25f, 0.26f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.13f, 0.12f);
            RenderSettings.ambientIntensity = 0.7f;
            var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Single(item => item.type == LightType.Directional);
            sun.intensity = 0.65f;
            sun.shadows = LightShadows.None;

            var settings = new LightingSettings
            {
                name = "ITPU_Visual001_Lighting",
                bakedGI = true, realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                lightmapResolution = 10f, lightmapMaxSize = 1024,
                indirectSampleCount = 64, directSampleCount = 32,
                environmentSampleCount = 64, maxBounces = 3,
            };
            AssetDatabase.CreateAsset(settings, Art + "/ITPU_Visual001_Lighting.lighting");
            Lightmapping.lightingSettings = settings;
            if (original != SceneContract()) throw new InvalidOperationException("Scene gameplay/physics contract changed");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            WriteReceipt("PASS_CANDIDATE_SAVED_BAKE_NOT_RUN", "NOT_RUN", meshes.Count, fixtures.Count, positions.Count);
            Capture(scene, "candidate_unbaked");
        }

        [Serializable] private sealed class Transfer { public Root[] roots; }
        [Serializable] private sealed class Root { public string id; public bool movable; }

        [MenuItem("ITPU/Visual Upgrade/Bake Candidate")]
        public static void BakeCandidate()
        {
            var scene = EditorSceneManager.OpenScene(CandidateScene, OpenSceneMode.Single);
            if (!Lightmapping.Bake()) throw new InvalidOperationException("Lighting bake failed");
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
                if (probe.name.StartsWith("ITPU_VISUAL001_"))
                {
                    var path = Art + "/" + probe.name + ".exr";
                    if (!Lightmapping.BakeReflectionProbe(probe, path))
                        throw new InvalidOperationException("Reflection probe bake failed: " + probe.name);
                }
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            var env = GameObject.Find("ENVIRONMENT_FULL_REFINED_005");
            WriteReceipt("PASS_BAKED_FOR_VISUAL_INSPECTION", "PASS",
                env.GetComponentsInChildren<MeshFilter>(true).Select(item => item.sharedMesh).Distinct().Count(),
                Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Count(item => item.lightmapBakeType == LightmapBakeType.Baked),
                Object.FindFirstObjectByType<LightProbeGroup>().probePositions.Length);
            Capture(scene, CaptureLabel);
        }

        [MenuItem("ITPU/Visual Upgrade/Refine Lighting")]
        public static void RefineLighting()
        {
            var scene = EditorSceneManager.OpenScene(CandidateScene, OpenSceneMode.Single);
            var settings = Lightmapping.lightingSettings;
            settings.lightmapResolution = 24f; settings.lightmapMaxSize = 2048;
            settings.indirectSampleCount = 128; settings.directSampleCount = 64;
            settings.environmentSampleCount = 128; settings.maxBounces = 4;
            EditorUtility.SetDirty(settings);
            RenderSettings.ambientSkyColor = new Color(0.53f, 0.57f, 0.63f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.35f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.19f);
            RenderSettings.ambientIntensity = 0.85f;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.type == LightType.Directional) light.intensity = 0.85f;
                else if (light.name.StartsWith("BAKED_FIXTURE_")) light.intensity = 8f;
            }
            var positions = new[] { new Vector3(0f, 1.5f, -12.5f), new Vector3(-18.2f, 1.5f, -16.2f) };
            for (var index = 0; index < positions.Length; index++)
            {
                var name = "ITPU_VISUAL001_REFLECTION_" + index;
                var go = GameObject.Find(name);
                if (go == null) go = new GameObject(name);
                go.transform.position = positions[index];
                var probe = go.GetComponent<ReflectionProbe>();
                if (probe == null) probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked; probe.resolution = 128;
                probe.size = new Vector3(8f, 3f, 8f); probe.boxProjection = true;
                probe.intensity = 0.8f; probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CaptureLabel = "after_bake002";
            BakeCandidate();
            ValidateCandidate();
            CaptureBaseline();
        }

        public static void RefineFloorLightmaps()
        {
            var scene = EditorSceneManager.OpenScene(CandidateScene, OpenSceneMode.Single);
            var settings = Lightmapping.lightingSettings;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            EditorUtility.SetDirty(settings);
            foreach (var renderer in GameObject.Find("ENVIRONMENT_FULL_REFINED_005").GetComponentsInChildren<MeshRenderer>(true))
                renderer.stitchLightmapSeams = true;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CaptureLabel = "after_bake004_nondirectional";
            BakeCandidate();
            ValidateCandidate();
        }

        [MenuItem("ITPU/Visual Upgrade/Capture Candidate")]
        public static void CaptureCandidate() => Capture(EditorSceneManager.OpenScene(CandidateScene, OpenSceneMode.Single), "after_bake003");

        [MenuItem("ITPU/Visual Upgrade/Capture Baseline")]
        public static void CaptureBaseline() => Capture(EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single), "before_clean");

        public static void DiagnoseFloor()
        {
            var scene = EditorSceneManager.OpenScene(CandidateScene, OpenSceneMode.Single);
            var floor = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/ITPU_floor_stone_visual001.mat");
            var previous = new Material(floor);
            floor.DisableKeyword("_NORMALMAP"); floor.SetTexture("_BumpMap", null);
            Capture(scene, "floor_without_normal");
            floor.shader = Shader.Find("Universal Render Pipeline/Unlit");
            Capture(scene, "floor_unlit_diagnostic");
            floor.shader = previous.shader;
            floor.CopyPropertiesFromMaterial(previous); Object.DestroyImmediate(previous);
            EditorUtility.SetDirty(floor); AssetDatabase.SaveAssets();
            var roots = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include).Where(item => item.Value.StartsWith("FLOOR_"));
            foreach (var root in roots.Take(2))
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                Debug.Log("WO31 FLOOR " + root.Value + " verts=" + mesh.vertexCount + " uv1=" + mesh.uv2.Length +
                    " normals=" + string.Join(";",mesh.normals.Distinct().Select(item=>item.ToString("F3"))));
            }
        }

        [MenuItem("ITPU/Visual Upgrade/Validate Candidate")]
        public static void ValidateCandidate()
        {
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            var expected = SceneContract();
            EditorSceneManager.OpenScene(CandidateScene, OpenSceneMode.Single);
            if (expected != SceneContract()) throw new InvalidOperationException("Candidate changed IDs/transforms/physics/gameplay topology");
            var maps = LightmapSettings.lightmaps;
            if (maps.Length == 0 || maps.Any(item => item.lightmapColor == null))
                throw new InvalidOperationException("Candidate has no complete saved lighting maps");
            if (Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Any(item =>
                    item.enabled && item.gameObject.activeInHierarchy && item.type != LightType.Directional &&
                    item.lightmapBakeType != LightmapBakeType.Baked))
                throw new InvalidOperationException("Candidate introduced realtime additional lights");
            foreach (var id in new[] { "K_CUP", "MARKER", "ERASER" })
            {
                var root = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include).Single(item => item.Value == id);
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    if ((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.ContributeGI) != 0)
                        throw new InvalidOperationException("Movable prop entered static bake: " + id);
            }
            File.WriteAllText(Path.Combine(Reports,"candidate_validation.txt"),
                "PASS IDs, transforms, physics topology, XR/gameplay counts, saved lightmaps, baked-only additional lights and movable props\n");
            Debug.Log("WO31 CANDIDATE_VALIDATION PASS");
        }

        private static void StyleMaterial(string name, Material material)
        {
            if (name == "ITPU_oak") Maps(material, "wood_table_001", "diff", new Color(0.68f, 0.49f, 0.29f), 0.22f, 0.12f);
            else if (name == "ITPU_floor_stone") Maps(material, "concrete_floor_02", "diff", new Color(0.63f, 0.62f, 0.59f), 0.07f, 0.10f);
            else if (name == "ITPU_fabric")
            {
                material.SetTexture("_BaseMap", null);
                material.SetColor("_BaseColor", new Color(0.12f, 0.135f, 0.15f));
                material.SetFloat("_Smoothness", 0.05f);
                material.SetTexture("_BumpMap", Normal("fabric_pattern_05_nor_gl_1k.jpg"));
                material.SetFloat("_BumpScale", 0.15f);
                material.EnableKeyword("_NORMALMAP");
            }
            else if (name == "ITPU_wall_paint")
            {
                material.SetColor("_BaseColor", new Color(0.79f, 0.78f, 0.74f));
                material.SetFloat("_Smoothness", 0.12f);
            }
            else if (name == "ITPU_steel") { material.SetFloat("_Metallic", 1f); material.SetFloat("_Smoothness", 0.55f); }
            else if (name == "ITPU_ceramic") material.SetFloat("_Smoothness", 0.65f);
            else if (name == "ITPU_frame") { material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", 0.28f); }
            EditorUtility.SetDirty(material);
        }

        private static void Maps(Material material, string asset, string suffix, Color tint, float variation, float normalScale)
        {
            var basePath = Art + "/Textures/" + asset + "_" + suffix + "_1k.jpg";
            var importer = (TextureImporter)AssetImporter.GetAtPath(basePath);
            importer.isReadable = true; importer.sRGBTexture = true; importer.mipmapEnabled = true;
            importer.anisoLevel = 4; importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
            var colors = source.GetPixels();
            var average = colors.Average(item => item.grayscale);
            for (var index = 0; index < colors.Length; index++)
            {
                var delta = Mathf.Clamp((colors[index].grayscale - average) * 2f, -1f, 1f) * variation;
                colors[index] = new Color(tint.r + delta, tint.g + delta, tint.b + delta, 1f);
            }
            var diffuse = new Texture2D(source.width, source.height, TextureFormat.RGB24, true);
            diffuse.SetPixels(colors); diffuse.Apply();
            var path = Art + "/Textures/" + asset + "_stylized_base.png";
            File.WriteAllBytes(path, diffuse.EncodeToPNG()); Object.DestroyImmediate(diffuse);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(path);
            textureImporter.anisoLevel = 4; textureImporter.mipmapEnabled = true; textureImporter.SaveAndReimport();
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", Normal(asset + "_nor_gl_1k.jpg"));
            material.SetFloat("_BumpScale", normalScale); material.EnableKeyword("_NORMALMAP");
            var roughPath = Art + "/Textures/" + asset + "_rough_1k.jpg";
            var roughImporter = (TextureImporter)AssetImporter.GetAtPath(roughPath);
            roughImporter.sRGBTexture = false; roughImporter.isReadable = true; roughImporter.SaveAndReimport();
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
            var pixels = roughness.GetPixels32();
            for (var index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(0, 255, 0, (byte)(255 - pixels[index].r));
            var mask = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, true, true);
            mask.SetPixels32(pixels); mask.Apply();
            var maskPath = Art + "/Textures/" + asset + "_mask.png";
            File.WriteAllBytes(maskPath, mask.EncodeToPNG()); Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
            var maskImporter = (TextureImporter)AssetImporter.GetAtPath(maskPath);
            maskImporter.sRGBTexture = false; maskImporter.SaveAndReimport();
            var packed = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            material.SetTexture("_MetallicGlossMap", packed); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_OcclusionMap", packed); material.SetFloat("_Smoothness", 1f);
        }

        private static Texture2D Normal(string filename)
        {
            var path = Art + "/Textures/" + filename;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false; importer.mipmapEnabled = true;
            importer.flipGreenChannel = false; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static string SceneContract() => string.Join("|", new[]
        {
            Object.FindObjectsByType<StableId>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<Collider>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include).Length,
            Object.FindObjectsByType<NpcStationController>(FindObjectsInactive.Include).Length,
        }) + "|" + string.Join(";", Object.FindObjectsByType<StableId>(FindObjectsInactive.Include)
            .OrderBy(item => item.Value).Select(item => item.Value + ":" + item.transform.position.ToString("F5") +
                ":" + item.transform.rotation.ToString("F5") + ":" + item.transform.lossyScale.ToString("F5"))) +
            "|" + string.Join(";", Object.FindObjectsByType<Collider>(FindObjectsInactive.Include)
                .Select(item => item.name + ":" + item.GetType().Name + ":" + item.transform.position.ToString("F5") +
                    ":" + item.bounds.ToString("F5") + ":" + item.isTrigger + ":" + item.enabled).OrderBy(item => item));

        private static void WriteReceipt(string status, string bake, int meshes, int lights, int probes)
        {
            var receipt = new Receipt
            {
                status = status, scene = CandidateScene, unity = Application.unityVersion,
                bake = bake, quest = "NOT_RUN", uv1Meshes = meshes, bakedAreaLights = lights,
                probePositions = probes, lightmaps = LightmapSettings.lightmaps.Length,
                environmentRenderers = GameObject.Find("ENVIRONMENT_FULL_REFINED_005").GetComponentsInChildren<Renderer>(true).Length,
                colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include).Length,
                stableIds = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include).Length,
                xrOrigins = Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include).Length,
                movers = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include).Length,
                notes = new[] { "Visual candidate only; main scene preserved.", "Art views temporarily hide gameplay UI; matching runtime-context images retain it.", "Powered by Poly Haven; source maps CC0; generated base colors are contrast-controlled derivatives." },
            };
            File.WriteAllText(Path.Combine(Reports, "visual_candidate_receipt.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("WO31 " + status + " lightmaps=" + receipt.lightmaps);
        }

        private static void EnsureFolders()
        {
            foreach (var directory in new[] { "/Materials", "/Meshes" }) Directory.CreateDirectory(Art + directory);
            AssetDatabase.Refresh();
        }

        private static void Capture(Scene scene, string label)
        {
            ShaderUtil.allowAsyncCompilation = false;
            Shader.WarmupAllShaders();
            var directory = Path.Combine(Reports, label);
            Directory.CreateDirectory(directory);
            var go = new GameObject("WO31_CAPTURE_CAMERA");
            var camera = go.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 80f;
            camera.fieldOfView = 66f; camera.clearFlags = CameraClearFlags.Skybox;
            var environment = GameObject.Find("ENVIRONMENT_FULL_REFINED_005");
            var hidden = new List<Renderer>();
            foreach (var root in scene.GetRootGameObjects().Where(item => item != environment && item != go &&
                         !item.name.StartsWith("ITPU_VISUAL")))
                hidden.AddRange(root.GetComponentsInChildren<Renderer>(true).Where(item => item.enabled));
            var coffee = environment.GetComponentsInChildren<StableId>(true).Single(item => item.Value == "K_COFFEE");
            var coffeeBounds = coffee.GetComponentsInChildren<Renderer>(true).First().bounds;
            foreach (var renderer in coffee.GetComponentsInChildren<Renderer>(true)) coffeeBounds.Encapsulate(renderer.bounds);
            for (var index = 0; index < 2; index++)
            {
                var name = index == 0 ? "work403" : "coffee";
                var position = index == 0 ? new Vector3(-3.20f, 1.65f, -10.70f) : coffeeBounds.center + new Vector3(0.85f, 0.55f, 1.05f);
                var target = index == 0 ? new Vector3(-1.90f, 1.35f, -14.80f) : coffeeBounds.center;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                Render(camera, Path.Combine(directory, name + "_runtime.png"));
                foreach (var renderer in hidden) renderer.enabled = false;
                Render(camera, Path.Combine(directory, name + "_art.png"));
                foreach (var renderer in hidden) renderer.enabled = true;
            }
            Object.DestroyImmediate(go);
        }

        private static void Render(Camera camera, string path)
        {
            var target = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render(); camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
}
