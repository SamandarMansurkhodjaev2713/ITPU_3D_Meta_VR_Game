using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ITPU.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class FullEnvironmentPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string TransferPath = "Assets/ITPU/Data/itpu_environment_v005_transfer.json";
        private const string EnvironmentPath = "Assets/ITPU/Art/Environment/ITPU_Environment_v005_full.fbx";
        private const string MaterialDirectory = "Assets/ITPU/Art/Materials";
        private const string TextureDirectory = "Assets/ITPU/Art/Textures";
        private const string EnvironmentName = "ENVIRONMENT_FULL_REFINED_005";
        private const string EnvironmentStableId = "environment.full.v005";
        private const string AcceptedSourceHash =
            "4b09805ff2d225bd8d189221e14c9cebc3c4e0861098eddb1c3b289b63ee1043";
        private const string FixtureMaterialPath = MaterialDirectory + "/ITPU_fixture_emissive.mat";
        private const string WallMaterialPath = MaterialDirectory + "/ITPU_wall_paint.mat";
        private const string FloorMaterialPath = MaterialDirectory + "/ITPU_floor_stone.mat";

        private static readonly string[] AllowedZones =
        {
            "WORK_403", "HALL_LEFT", "APPROACH_401", "RECEPTION_FRONT", "HALL_TOP",
            "NICHE_412", "NICHE_413", "NICHE_414", "HALL_RIGHT", "APPROACH_423", "KITCHEN",
        };

        [Serializable] private sealed class VectorData { public float x; public float y; public float z; }
        [Serializable] private sealed class ColliderData
        {
            public string name; public string shape; public string mesh_source;
            public bool convex; public VectorData center; public VectorData size;
        }
        [Serializable] private sealed class RootData
        {
            public string id; public VectorData position; public float yaw_degrees; public bool movable;
            public bool teleport_surface; public ColliderData[] colliders;
        }
        [Serializable] private sealed class MaterialData
        {
            public string id; public string base_texture; public string mask_texture;
        }
        [Serializable] private sealed class ColliderCounts
        {
            public int roots; public int colliders; public int boxes;
            public int static_mesh_proxies; public int capsule_proxies; public int teleport_surfaces;
        }
        [Serializable] private sealed class TransferData
        {
            public int schema_version; public string status; public string source_blend;
            public string source_blend_sha256; public string scope; public string[] zones;
            public RootData[] roots; public MaterialData[] materials; public ColliderCounts collider_counts;
        }
        [Serializable] private sealed class Report
        {
            public string status; public string unity_version; public string scene; public string environment_asset;
            public string source_blend_sha256; public int stable_roots; public int renderers;
            public int box_colliders; public int static_mesh_colliders; public int capsule_colliders;
            public int teleport_surfaces; public int xr_origins; public int locomotion_drivers;
            public int c01_controllers; public int c02_controllers; public int c03_controllers;
            public VectorData bounds_center_m; public VectorData bounds_size_m;
            public bool c03_inside_403; public bool immutable_source_contract;
            public string main_light_shadows; public int msaa_samples;
            public int emissive_fixture_renderers; public int wall_paint_renderers; public int floor_renderers;
            public string quest; public string[] errors;
        }
        [Serializable] private sealed class VisualReport
        {
            public string status; public string scene; public VectorData bounds_center_m;
            public VectorData bounds_size_m; public string[] images; public string quest;
        }

        [MenuItem("ITPU/Apply WO-25 Refined Full Environment")]
        public static void Apply()
        {
            var errors = new List<string>();
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(TransferPath);
            if (source == null) throw new FileNotFoundException("Full transfer data missing", TransferPath);
            var transfer = JsonUtility.FromJson<TransferData>(source.text);
            ValidateTransfer(transfer);
            ConfigureMaskImports(transfer.materials);
            AssetDatabase.ImportAsset(EnvironmentPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            if (prefab == null) throw new FileNotFoundException("Full environment FBX missing", EnvironmentPath);
            var environment = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (environment == null) throw new InvalidOperationException("Could not instantiate full environment FBX");
            environment.name = EnvironmentName + "__STAGING";
            AddStableId(environment, EnvironmentStableId);
            ApplyMaterials(environment, transfer.materials);

            var stableRoots = 0;
            var boxCount = 0;
            var meshCount = 0;
            var capsuleCount = 0;
            foreach (var rootData in transfer.roots)
            {
                var importedRoot = FindByName(environment.transform, rootData.id);
                if (importedRoot == null)
                {
                    errors.Add("Missing imported stable root: " + rootData.id);
                    continue;
                }
                stableRoots++;
                AddStableId(importedRoot.gameObject, rootData.id);
                SetStaticRecursively(importedRoot, !rootData.movable);
                Transform colliderFrame = null;
                if ((rootData.colliders?.Length ?? 0) > 0)
                    colliderFrame = CreateColliderFrame(environment.transform, importedRoot, rootData, errors);
                foreach (var colliderData in rootData.colliders ?? Array.Empty<ColliderData>())
                {
                    switch (colliderData.shape)
                    {
                        case "box":
                            CreateBoxCollider(colliderFrame, colliderData);
                            boxCount++;
                            break;
                        case "static_mesh_proxy":
                            CreateStaticMeshCollider(environment.transform, colliderData, errors);
                            meshCount++;
                            break;
                        case "capsule_proxy":
                            CreateCapsuleCollider(colliderFrame, colliderData, errors);
                            capsuleCount++;
                            break;
                        default:
                            errors.Add($"Unsupported collider shape {colliderData.shape} on {rootData.id}");
                            break;
                    }
                }
                if (rootData.teleport_surface && importedRoot.GetComponent<TeleportSurface>() == null)
                    importedRoot.gameObject.AddComponent<TeleportSurface>();
            }

            ApplyVisualRefinement(environment, errors, out var key, out var msaaSamples,
                out var emissiveFixtures, out var wallPaintRenderers, out var floorRenderers);

            Physics.SyncTransforms();
            var renderers = environment.GetComponentsInChildren<Renderer>(true);
            var bounds = CalculateBounds(renderers, errors);
            ValidateContract(environment, transfer, stableRoots, renderers.Length, boxCount, meshCount,
                capsuleCount, bounds, errors);
            var c03Inside = ValidateC03Placement(environment, errors);
            var report = CreateReport(transfer, stableRoots, renderers.Length, boxCount, meshCount,
                capsuleCount, bounds, c03Inside, key, msaaSamples, emissiveFixtures,
                wallPaintRenderers, floorRenderers, errors);

            if (errors.Count > 0)
            {
                WriteReport(report);
                Object.DestroyImmediate(environment);
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            foreach (var oldEnvironment in SceneManager.GetActiveScene().GetRootGameObjects()
                          .Where(item => item != environment &&
                                         (item.name == "ENVIRONMENT_403_ACCEPTED_004" ||
                                          item.name == "ENVIRONMENT_FULL_ACCEPTED_004" ||
                                          item.name == EnvironmentName)).ToArray())
                Object.DestroyImmediate(oldEnvironment);
            environment.name = EnvironmentName;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                errors.Add("Scene save returned false");
            AssetDatabase.SaveAssets();
            report = CreateReport(transfer, stableRoots, renderers.Length, boxCount, meshCount,
                capsuleCount, bounds, c03Inside, key, msaaSamples, emissiveFixtures,
                wallPaintRenderers, floorRenderers, errors);
            WriteReport(report);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-25 refined full environment patch PASS: " + JsonUtility.ToJson(report));
        }

        [MenuItem("ITPU/Capture WO-25 Refined Full Environment Views")]
        public static void CaptureViews()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environment = SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(item => item.name == EnvironmentName);
            var cameraObject = new GameObject("WO25_DIAGNOSTIC_CAMERA");
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.cullingMask = ~0;
            camera.clearFlags = CameraClearFlags.Skybox;
            var bounds = CalculateBounds(environment.GetComponentsInChildren<Renderer>(true),
                new List<string>());
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-25", "control_views_v2");
            Directory.CreateDirectory(directory);
            var images = new[]
            {
                "full_overview.png", "hall_to_niches.png", "niche_413.png", "kitchen.png", "work403.png",
            };

            camera.orthographic = true;
            camera.orthographicSize = 13.2f;
            camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, 30f, bounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));
            Render(camera, Path.Combine(directory, images[0]));

            camera.orthographic = false;
            camera.fieldOfView = 66f;
            SetView(camera, new Vector3(-6.55f, 1.65f, -25.73f), new Vector3(-14.80f, 1.38f, -25.73f));
            Render(camera, Path.Combine(directory, images[1]));
            SetView(camera, new Vector3(-10.225f, 1.65f, -26.30f), new Vector3(-10.225f, 1.05f, -24.20f));
            Render(camera, Path.Combine(directory, images[2]));
            SetView(camera, new Vector3(-18.55f, 1.65f, -14.25f), new Vector3(-18.10f, 1.20f, -17.35f));
            Render(camera, Path.Combine(directory, images[3]));
            SetView(camera, new Vector3(-3.20f, 1.65f, -10.70f), new Vector3(-1.90f, 1.35f, -14.80f));
            Render(camera, Path.Combine(directory, images[4]));

            var report = new VisualReport
            {
                status = "PASS_RENDERED_FOR_VISUAL_INSPECTION",
                scene = ScenePath,
                bounds_center_m = FromVector(bounds.center),
                bounds_size_m = FromVector(bounds.size),
                images = images.Select(item => "reports/local/WO-25/control_views_v2/" + item).ToArray(),
                quest = "NOT_RUN",
            };
            File.WriteAllText(Path.Combine(directory, "visual_report.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
            Object.DestroyImmediate(cameraObject);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static void ValidateTransfer(TransferData transfer)
        {
            if (transfer == null || transfer.schema_version != 2 || transfer.roots == null ||
                transfer.materials == null || transfer.collider_counts == null)
                throw new InvalidDataException("Invalid full-environment transfer schema");
            if (transfer.source_blend_sha256 != AcceptedSourceHash)
                throw new InvalidDataException("Full transfer does not reference the validated _005 successor");
            if (transfer.scope != "FULL_ALLOWED" || transfer.zones == null ||
                !transfer.zones.SequenceEqual(AllowedZones))
                throw new InvalidDataException("Full transfer zone allowlist mismatch");
            var counts = transfer.collider_counts;
            if (transfer.roots.Length != 380 || counts.roots != 380 || counts.colliders != 554 ||
                counts.boxes != 549 || counts.static_mesh_proxies != 4 || counts.capsule_proxies != 1 ||
                counts.teleport_surfaces != 28)
                throw new InvalidDataException("Full transfer inventory mismatch");
        }

        private static void ConfigureMaskImports(IEnumerable<MaterialData> materials)
        {
            foreach (var maskName in materials.Select(item => item.mask_texture)
                         .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct())
            {
                var path = TextureDirectory + "/" + maskName;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    throw new FileNotFoundException("Mask texture importer missing", path);
                if (!importer.sRGBTexture) continue;
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
        }

        private static void ApplyMaterials(GameObject environment, IEnumerable<MaterialData> definitions)
        {
            var materials = definitions.ToDictionary(item => item.id,
                item => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDirectory}/ITPU_{item.id}.mat"),
                StringComparer.OrdinalIgnoreCase);
            if (materials.Any(item => item.Value == null))
                throw new FileNotFoundException("One or more accepted Unity materials are missing");
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                var assigned = renderer.sharedMaterials;
                for (var index = 0; index < assigned.Length; index++)
                {
                    var name = assigned[index] == null ? "" : assigned[index].name.Replace(" (Instance)", "");
                    var id = name.StartsWith("ITPU_", StringComparison.OrdinalIgnoreCase) ? name.Substring(5) : name;
                    if (materials.TryGetValue(id, out var accepted)) assigned[index] = accepted;
                }
                renderer.sharedMaterials = assigned;
            }
        }

        private static void ApplyVisualRefinement(GameObject environment, ICollection<string> errors,
            out Light key, out int msaaSamples, out int emissiveFixtures,
            out int wallPaintRenderers, out int floorRenderers)
        {
            var directional = Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.type == LightType.Directional).ToArray();
            key = directional.SingleOrDefault();
            if (key == null)
                errors.Add($"Expected exactly one directional light, found {directional.Length}");
            else
            {
                key.intensity = 0.88f;
                key.shadows = LightShadows.None;
                key.shadowStrength = 0f;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.84f, 0.86f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.66f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.48f, 0.44f, 0.38f);
            RenderSettings.ambientIntensity = 1.20f;

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            {
                errors.Add("Active render pipeline is not URP");
                msaaSamples = 0;
            }
            else
            {
                pipeline.msaaSampleCount = 4;
                EditorUtility.SetDirty(pipeline);
                msaaSamples = pipeline.msaaSampleCount;
            }

            var fixtureMaterial = CreateOrUpdateMaterial(FixtureMaterialPath, "ITPU_fixture_emissive",
                new Color(1.00f, 0.91f, 0.72f), 0.18f, new Color(3.2f, 2.55f, 1.55f));
            var wallMaterial = CreateOrUpdateMaterial(WallMaterialPath, "ITPU_wall_paint",
                new Color(0.78f, 0.79f, 0.77f), 0.14f, Color.black);
            var floorMaterial = CreateOrUpdateMaterial(FloorMaterialPath, "ITPU_floor_stone",
                new Color(0.58f, 0.57f, 0.54f), 0.22f, Color.black);

            emissiveFixtures = 0;
            wallPaintRenderers = 0;
            floorRenderers = 0;
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var upper = renderer.name.ToUpperInvariant();
                var fixture = (upper.Contains("LIGHT_") && upper.EndsWith("DIFFUSER")) ||
                              (upper.Contains("PENDANT_") && upper.EndsWith("GLOBE"));
                if (fixture)
                {
                    AssignMaterial(renderer, fixtureMaterial);
                    emissiveFixtures++;
                }
            }

            foreach (var stable in environment.GetComponentsInChildren<StableId>(true))
            {
                var identifier = stable.Value;
                if (string.IsNullOrWhiteSpace(identifier)) continue;
                if (identifier.StartsWith("FLOOR_", StringComparison.Ordinal))
                    floorRenderers += AssignMaterial(stable.transform, floorMaterial);
                else if (identifier.StartsWith("WALL_", StringComparison.Ordinal) ||
                         identifier.StartsWith("NICHE_SIDE_", StringComparison.Ordinal) ||
                         identifier.StartsWith("ARCH_", StringComparison.Ordinal) ||
                         identifier.StartsWith("SPANDREL_", StringComparison.Ordinal))
                    wallPaintRenderers += AssignMaterial(stable.transform, wallMaterial);
            }

            var localLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Count(item => item.enabled && item.gameObject.activeInHierarchy &&
                                                      (item.type == LightType.Point || item.type == LightType.Spot));
            if (localLights != 0) errors.Add("Quest visual profile must retain zero realtime point/spot lights");
            if (msaaSamples != 4) errors.Add("Quest visual profile requires 4x MSAA");
            if (emissiveFixtures != 21) errors.Add($"Expected 21 emissive fixture renderers, found {emissiveFixtures}");
            if (floorRenderers != 28) errors.Add($"Expected 28 floor renderers, found {floorRenderers}");
            if (wallPaintRenderers < 130) errors.Add($"Wall paint assignment is incomplete: {wallPaintRenderers}");
        }

        private static Material CreateOrUpdateMaterial(string path, string name, Color baseColor,
            float smoothness, Color emission)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit shader is unavailable");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_Color", baseColor);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_ReceiveShadows", 0f);
            material.enableInstancing = true;
            if (emission.maxColorComponent > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AssignMaterial(Renderer renderer, Material material)
        {
            var assigned = renderer.sharedMaterials;
            for (var index = 0; index < assigned.Length; index++) assigned[index] = material;
            renderer.sharedMaterials = assigned;
        }

        private static int AssignMaterial(Transform root, Material material)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers) AssignMaterial(renderer, material);
            return renderers.Length;
        }

        private static Transform CreateColliderFrame(Transform environment, Transform importedRoot,
            RootData rootData, ICollection<string> errors)
        {
            const float tolerance = 0.001f;
            var scale = importedRoot.lossyScale;
            if (Mathf.Abs(scale.x - 1f) > tolerance || Mathf.Abs(scale.y - 1f) > tolerance ||
                Mathf.Abs(scale.z - 1f) > tolerance)
                errors.Add($"Collider proxy requires unit imported scale: {rootData.id} scale={scale}");
            var frame = new GameObject(rootData.id + "__COLLIDER_FRAME");
            frame.transform.SetParent(importedRoot, false);
            frame.transform.SetPositionAndRotation(environment.TransformPoint(ToUnityPoint(rootData.position)),
                environment.rotation * Quaternion.Euler(0f, -rootData.yaw_degrees, 0f));
            frame.transform.localScale = Vector3.one;
            return frame.transform;
        }

        private static void CreateBoxCollider(Transform frame, ColliderData data)
        {
            if (frame == null || data.center == null || data.size == null)
                throw new InvalidDataException("Box collider data is incomplete: " + data.name);
            var collider = frame.gameObject.AddComponent<BoxCollider>();
            collider.center = ToUnityPoint(data.center);
            collider.size = ToVector(data.size);
        }

        private static void CreateCapsuleCollider(Transform frame, ColliderData data,
            ICollection<string> errors)
        {
            if (frame == null || data.center == null || data.size == null)
            {
                errors.Add("Capsule collider data is incomplete: " + data.name);
                return;
            }
            var size = ToVector(data.size);
            var collider = frame.gameObject.AddComponent<CapsuleCollider>();
            collider.center = ToUnityPoint(data.center);
            collider.direction = 1;
            collider.radius = Mathf.Max(size.x, size.z) * 0.5f;
            collider.height = Mathf.Max(size.y, collider.radius * 2f);
        }

        private static void CreateStaticMeshCollider(Transform environment, ColliderData data,
            ICollection<string> errors)
        {
            // Blender FBX may flatten the render member beside its authored stable-root
            // empty. Render node IDs are globally unique inside this bounded asset.
            var filter = environment.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(item =>
                string.Equals(item.name, data.mesh_source, StringComparison.Ordinal) ||
                string.Equals(item.sharedMesh?.name, data.mesh_source, StringComparison.Ordinal) ||
                item.name.EndsWith(data.mesh_source, StringComparison.Ordinal) ||
                (item.sharedMesh?.name?.EndsWith(data.mesh_source, StringComparison.Ordinal) ?? false));
            if (filter == null || filter.sharedMesh == null)
            {
                var suffix = data.mesh_source.Contains("__")
                    ? data.mesh_source.Substring(data.mesh_source.IndexOf("__", StringComparison.Ordinal) + 2)
                    : data.mesh_source;
                var owner = data.mesh_source.Split(new[] { "__" }, StringSplitOptions.None)[0];
                filter = environment.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(item =>
                    item.name.Contains(owner) && item.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            }
            if (filter == null || filter.sharedMesh == null)
            {
                errors.Add("Static mesh source missing after transform/mesh identity lookup: " + data.mesh_source);
                return;
            }
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = data.convex;
        }

        private static void ValidateContract(GameObject environment, TransferData transfer, int stableRoots,
            int rendererCount, int boxes, int meshes, int capsules, Bounds bounds, ICollection<string> errors)
        {
            if (stableRoots != 380) errors.Add("Expected 380 imported stable roots, found " + stableRoots);
            if (rendererCount != 626) errors.Add("Expected 626 imported renderers, found " + rendererCount);
            if (boxes != 549 || meshes != 4 || capsules != 1)
                errors.Add($"Collider counts mismatch: boxes={boxes}, meshes={meshes}, capsules={capsules}");
            if (environment.GetComponentsInChildren<TeleportSurface>(true).Length != 28)
                errors.Add("Expected 28 teleport surfaces");
            if (Mathf.Abs(bounds.size.x - 21.743f) > 0.15f || Mathf.Abs(bounds.size.y - 3.18f) > 0.08f ||
                Mathf.Abs(bounds.size.z - 22.0815f) > 0.15f)
                errors.Add("Full environment bounds mismatch: " + bounds.size);
            foreach (var id in new[] { "MARKER", "ERASER", "K_CUP" })
            {
                var movable = FindByName(environment.transform, id);
                if (movable == null || movable.gameObject.isStatic)
                    errors.Add(id + " must remain non-static");
            }
            if (Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected exactly one XR origin");
            if (Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected exactly one locomotion driver");
            if (Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C01 controller was not preserved");
            if (Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C02 controller was not preserved");
            if (Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C03 controller was not preserved");
        }

        private static bool ValidateC03Placement(GameObject environment, ICollection<string> errors)
        {
            Physics.SyncTransforms();
            var panel = GameObject.Find("C03_INFORMATION_PANEL")?.GetComponent<Renderer>();
            var floorRoots = new[] { "FLOOR_0_2", "FLOOR_0_3" }
                .Select(id => FindByName(environment.transform, id)).Where(item => item != null).ToArray();
            var floors = floorRoots.SelectMany(item => item.GetComponentsInChildren<Collider>(true)).ToArray();
            if (panel == null || floors.Length != 2)
            {
                errors.Add("C03 panel or accepted 403 floor colliders are missing");
                return false;
            }
            var floorBounds = floors[0].bounds;
            floorBounds.Encapsulate(floors[1].bounds);
            var inside = panel.bounds.min.x > floorBounds.min.x && panel.bounds.max.x < floorBounds.max.x &&
                         panel.bounds.min.z > floorBounds.min.z && panel.bounds.max.z < floorBounds.max.z;
            var wallRoot = FindByName(environment.transform, "WOOD_WALL_403");
            var wall = wallRoot == null ? null : wallRoot.GetComponentInChildren<Collider>(true);
            inside &= wall != null && panel.bounds.min.z > wall.bounds.max.z;
            if (!inside) errors.Add("Corrected C03 placement is not inside the full 403 collision frame");
            return inside;
        }

        private static Report CreateReport(TransferData transfer, int roots, int renderers, int boxes,
            int meshes, int capsules, Bounds bounds, bool c03Inside, Light key, int msaaSamples,
            int emissiveFixtures, int wallPaintRenderers, int floorRenderers, List<string> errors) => new()
        {
            status = errors.Count == 0 ? "PASS" : "FAIL",
            unity_version = Application.unityVersion,
            scene = ScenePath,
            environment_asset = EnvironmentPath,
            source_blend_sha256 = transfer.source_blend_sha256,
            stable_roots = roots,
            renderers = renderers,
            box_colliders = boxes,
            static_mesh_colliders = meshes,
            capsule_colliders = capsules,
            teleport_surfaces = transfer.collider_counts.teleport_surfaces,
            xr_origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            locomotion_drivers = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            c01_controllers = Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            c02_controllers = Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            c03_controllers = Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            bounds_center_m = FromVector(bounds.center),
            bounds_size_m = FromVector(bounds.size),
            c03_inside_403 = c03Inside,
            immutable_source_contract = transfer.source_blend_sha256 == AcceptedSourceHash,
            main_light_shadows = key == null ? "MISSING" : key.shadows.ToString(),
            msaa_samples = msaaSamples,
            emissive_fixture_renderers = emissiveFixtures,
            wall_paint_renderers = wallPaintRenderers,
            floor_renderers = floorRenderers,
            quest = "NOT_RUN",
            errors = errors.ToArray(),
        };

        private static Bounds CalculateBounds(Renderer[] renderers, ICollection<string> errors)
        {
            if (renderers.Length == 0)
            {
                errors.Add("Full environment has no renderers");
                return default;
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void SetStaticRecursively(Transform root, bool value)
        {
            var flags = value
                ? StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                  StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic
                : 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags);
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void AddStableId(GameObject target, string value)
        {
            var stable = target.GetComponent<StableId>() ?? target.AddComponent<StableId>();
            stable.Set(value);
        }

        private static Vector3 ToVector(VectorData value) => new(value.x, value.y, value.z);
        private static Vector3 ToUnityPoint(VectorData value) => new(-value.x, value.y, value.z);
        private static VectorData FromVector(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };

        private static void WriteReport(Report report)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-25");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "unity_visual_refinement_patch.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static void SetView(Camera camera, Vector3 position, Vector3 target)
        {
            camera.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation((target - position).normalized, Vector3.up));
        }

        private static void Render(Camera camera, string path)
        {
            const int width = 1280;
            const int height = 720;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(output);
            texture.Release();
            Object.DestroyImmediate(texture);
        }
    }
}
