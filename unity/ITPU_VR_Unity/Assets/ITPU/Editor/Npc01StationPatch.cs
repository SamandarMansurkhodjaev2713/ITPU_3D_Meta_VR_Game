using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ITPU.XR;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class Npc01StationPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string ModelPath = "Assets/ITPU/Art/Characters/NPC01/NPC01_Samandar_production_021.fbx";
        private const string ManifestPath = "Assets/ITPU/Art/Characters/NPC01/NPC01_Samandar_production_021.manifest.json";
        private const string ControllerPath = "Assets/ITPU/Art/Characters/NPC01/NPC01_Station.controller";
        private const string MaterialDirectory = "Assets/ITPU/Art/Characters/NPC01/Materials";
        private const string RootName = "NPC01_SAMANDAR_STATION";
        private const string StableIdentifier = "npc.station.NPC01.production_021";
        private const string ExpectedFbxHash = "0d1887023643933676965622c1f78ba8223d9d966e89948a96a5547e84596f4b";
        private const string ExpectedManifestHash = "4b726f602db31aac8d07d691f8fc49d3d5ae833d317e45b6447bfc672b59b885";

        private static readonly string[] RequiredClips =
        {
            "NPC01_Idle", "NPC01_Greet", "NPC01_Explain", "NPC01_PointNear",
        };

        [Serializable] private sealed class ImportManifest
        {
            public int schema_version;
            public string npc_id;
            public string revision;
            public string units;
            public bool stationary_root;
            public bool root_motion;
            public bool navmesh_agent;
            public string[] clips;
            public string[] materials;
            public string[] image_dependencies;
        }

        [Serializable] private sealed class VectorData { public float x; public float y; public float z; }

        [Serializable] private sealed class Report
        {
            public string status;
            public string unity_version;
            public string scene;
            public string model;
            public string fbx_sha256;
            public string manifest_sha256;
            public string manifest_revision;
            public VectorData anchor_position_m;
            public float anchor_yaw_deg;
            public VectorData imported_bounds_m;
            public int skinned_renderers;
            public int materials;
            public int blend_shapes;
            public int expression_bindings;
            public string[] clips;
            public bool apply_root_motion;
            public bool navmesh_agent;
            public int npc_station_roots;
            public int xr_origins;
            public int locomotion_drivers;
            public string quest;
            public string[] errors;
        }

        [MenuItem("ITPU/Apply WO-19 NPC01 Station")]
        public static void Apply()
        {
            var errors = new List<string>();
            ValidateSource(errors, out var manifest);
            ConfigureImporter(errors);
            AssetDatabase.ImportAsset(ModelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var clips = LoadRequiredClips(errors);
            var animatorController = BuildAnimatorController(clips, errors);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) errors.Add("Imported NPC01 model prefab is missing");
            if (errors.Count > 0)
            {
                WriteReport(CreateReport(default, default, 0, 0, 0, Array.Empty<string>(), errors));
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var old in SceneManager.GetActiveScene().GetRootGameObjects()
                         .Where(item => item.name == RootName).ToArray())
                Object.DestroyImmediate(old);

            Physics.SyncTransforms();
            var anchor = FindClearAnchor(errors);
            var root = new GameObject(RootName);
            root.transform.SetPositionAndRotation(anchor, Quaternion.identity);
            var stable = root.AddComponent<StableId>();
            stable.Set(StableIdentifier);

            var instance = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
            if (instance == null)
            {
                Object.DestroyImmediate(root);
                throw new InvalidOperationException("Could not instantiate NPC01 FBX");
            }
            instance.name = "NPC01_SAMANDAR_MODEL_production_021";
            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var animator = instance.GetComponentInChildren<Animator>(true) ?? instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            ApplyUrpMaterials(renderers, errors);
            var bounds = CalculateDeformedBounds(renderers, errors);
            if (Mathf.Abs(bounds.min.y - anchor.y) > 0.04f)
                errors.Add($"Imported feet are not on station floor: minY={bounds.min.y:F4}, floor={anchor.y:F4}");
            if (bounds.size.y < 1.65f || bounds.size.y > 1.90f)
                errors.Add("Imported NPC height outside expected adult candidate range: " + bounds.size.y);

            var authority = Object.FindFirstObjectByType<ChallengeSessionAuthority>(FindObjectsInactive.Include);
            var lifecycle = Object.FindFirstObjectByType<XrLifecycleState>(FindObjectsInactive.Include);
            var attention = Camera.main == null ? null : Camera.main.transform;
            var controller = root.AddComponent<NpcStationController>();
            controller.Configure(animator, Find(instance.transform, "chest_aim"),
                Find(instance.transform, "head_aim"), attention, authority, lifecycle);
            controller.ResetStation();

            if (instance.GetComponentsInChildren<Component>(true)
                .Any(component => component != null && component.GetType().Name == "NavMeshAgent"))
                errors.Add("NPC01 import unexpectedly contains a NavMeshAgent");
            if (controller.ExpressionBindingCount < 5)
                errors.Add("Imported expression mapping is incomplete: " + controller.ExpressionBindingCount);
            if (renderers.Length == 0) errors.Add("NPC01 has no SkinnedMeshRenderer");
            Physics.SyncTransforms();
            ValidateSceneInvariants(root, errors);
            var report = CreateReport(anchor, bounds.size, renderers.Length,
                renderers.SelectMany(item => item.sharedMaterials).Where(item => item != null)
                    .Select(item => item.name).Distinct().Count(),
                renderers.Sum(item => item.sharedMesh == null ? 0 : item.sharedMesh.blendShapeCount),
                clips.Keys.OrderBy(item => item).ToArray(), errors,
                controller.ExpressionBindingCount);
            if (errors.Count > 0)
            {
                WriteReport(report);
                Object.DestroyImmediate(root);
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("NPC01 scene save returned false");
            AssetDatabase.SaveAssets();
            report = CreateReport(anchor, bounds.size, renderers.Length,
                renderers.SelectMany(item => item.sharedMaterials).Where(item => item != null)
                    .Select(item => item.name).Distinct().Count(),
                renderers.Sum(item => item.sharedMesh == null ? 0 : item.sharedMesh.blendShapeCount),
                clips.Keys.OrderBy(item => item).ToArray(), errors,
                controller.ExpressionBindingCount);
            WriteReport(report);
            Debug.Log("WO-19 NPC01 station patch PASS: " + JsonUtility.ToJson(report));
        }

        [MenuItem("ITPU/Capture WO-19 NPC01 Unity View")]
        public static void CaptureView()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var station = SceneManager.GetActiveScene().GetRootGameObjects().Single(item => item.name == RootName);
            var cameraObject = new GameObject("WO19_NPC01_DIAGNOSTIC_CAMERA");
            var camera = cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("WO19_NPC01_DIAGNOSTIC_LIGHT");
            var diagnosticLight = lightObject.AddComponent<Light>();
            diagnosticLight.type = LightType.Directional;
            diagnosticLight.color = Color.white;
            diagnosticLight.intensity = 1.35f;
            diagnosticLight.shadows = LightShadows.None;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 50f;
            camera.transform.SetPositionAndRotation(station.transform.position + new Vector3(0f, 1.45f, 1.45f),
                Quaternion.LookRotation((station.transform.position + new Vector3(0f, 1.05f, 0f) -
                                         (station.transform.position + new Vector3(0f, 1.45f, 1.45f))).normalized,
                    Vector3.up));
            lightObject.transform.rotation = camera.transform.rotation;
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-19", "unity_control_views_021");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "npc01_station_front.png");
            const int width = 1280;
            const int height = 720;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            // The first explicit batch render only warms URP shader/material
            // variants on this host; never use it as visual evidence.
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("WO-19 NPC01 Unity capture: " + path);
        }

        private static void ValidateSource(ICollection<string> errors, out ImportManifest manifest)
        {
            manifest = null;
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var fbx = Path.Combine(project, ModelPath.Replace('/', Path.DirectorySeparatorChar));
            var manifestFile = Path.Combine(project, ManifestPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fbx) || Sha256(fbx) != ExpectedFbxHash) errors.Add("Exact NPC01 FBX hash mismatch");
            if (!File.Exists(manifestFile) || Sha256(manifestFile) != ExpectedManifestHash)
                errors.Add("Exact NPC01 manifest hash mismatch");
            if (!File.Exists(manifestFile)) return;
            manifest = JsonUtility.FromJson<ImportManifest>(File.ReadAllText(manifestFile));
            if (manifest == null || manifest.schema_version != 1 || manifest.npc_id != "NPC01" ||
                manifest.revision != "production_021" || manifest.units != "meters" ||
                !manifest.stationary_root || manifest.root_motion || manifest.navmesh_agent ||
                manifest.image_dependencies == null || manifest.image_dependencies.Length != 0 ||
                manifest.clips == null || !RequiredClips.OrderBy(item => item)
                    .SequenceEqual(manifest.clips.OrderBy(item => item)))
                errors.Add("NPC01 manifest contract mismatch");
        }

        private static void ConfigureImporter(ICollection<string> errors)
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                errors.Add("NPC01 ModelImporter is unavailable");
                return;
            }
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importBlendShapes = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, AnimationClip> LoadRequiredClips(ICollection<string> errors)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            var result = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var name in RequiredClips)
            {
                var clip = all.FirstOrDefault(item => item.name == name) ??
                           all.FirstOrDefault(item => item.name.EndsWith(name, StringComparison.Ordinal));
                if (clip == null) errors.Add("Required imported clip missing: " + name);
                else result[name] = clip;
            }
            return result;
        }

        private static AnimatorController BuildAnimatorController(IReadOnlyDictionary<string, AnimationClip> clips,
            ICollection<string> errors)
        {
            if (!AssetDatabase.IsValidFolder(MaterialDirectory))
            {
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "ITPU/Art/Characters/NPC01/Materials")));
                AssetDatabase.Refresh();
            }
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            foreach (var state in stateMachine.states) stateMachine.RemoveState(state.state);
            foreach (var pair in clips)
            {
                var state = stateMachine.AddState(pair.Key);
                state.motion = pair.Value;
                state.writeDefaultValues = true;
                if (pair.Key == "NPC01_Idle") stateMachine.defaultState = state;
            }
            if (stateMachine.defaultState == null) errors.Add("NPC01 Idle state was not created");
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ApplyUrpMaterials(IEnumerable<SkinnedMeshRenderer> renderers,
            ICollection<string> errors)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                errors.Add("URP/Lit shader unavailable for NPC01");
                return;
            }
            var replacements = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase)
            {
                ["NPC01_MAT_DARK_OPAQUE"] = Material("NPC01_MAT_DARK_OPAQUE", shader, new Color(0.025f, 0.019f, 0.016f), 0.18f),
                ["NPC01_MAT_DENIM_LIGHT"] = Material("NPC01_MAT_DENIM_LIGHT", shader, new Color(0.55f, 0.72f, 0.80f), 0.08f),
                ["NPC01_MAT_SKIN"] = Material("NPC01_MAT_SKIN", shader, new Color(0.72f, 0.45f, 0.32f), 0.12f),
                ["NPC01_MAT_WHITE_CLOTH_SHOE"] = Material("NPC01_MAT_WHITE_CLOTH_SHOE", shader, new Color(0.91f, 0.91f, 0.88f), 0.04f),
            };
            foreach (var renderer in renderers)
            {
                var assigned = renderer.sharedMaterials;
                for (var index = 0; index < assigned.Length; index++)
                {
                    var name = assigned[index] == null ? string.Empty : assigned[index].name.Replace(" (Instance)", "");
                    if (!replacements.TryGetValue(name, out var replacement))
                    {
                        errors.Add("Unexpected NPC01 material: " + name);
                        continue;
                    }
                    assigned[index] = replacement;
                }
                renderer.sharedMaterials = assigned;
                renderer.updateWhenOffscreen = false;
            }
        }

        private static Material Material(string name, Shader shader, Color color, float smoothness)
        {
            var path = MaterialDirectory + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 0f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Vector3 FindClearAnchor(ICollection<string> errors)
        {
            var floors = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.Value == "FLOOR_0_2" ||
                                                           item.Value == "FLOOR_0_3")
                .SelectMany(item => item.GetComponentsInChildren<Collider>(true)).ToArray();
            var panel = GameObject.Find("C01_INFORMATION_PANEL")?.transform;
            if (floors.Length == 0 || panel == null)
            {
                errors.Add("403 floor colliders or C01 information panel missing");
                return default;
            }
            var floorBounds = floors[0].bounds;
            foreach (var floor in floors.Skip(1)) floorBounds.Encapsulate(floor.bounds);
            var preferred = panel.position + new Vector3(-1.75f, 0f, 0.80f);
            var candidates = new List<Vector3>();
            for (var x = floorBounds.min.x + 0.55f; x <= floorBounds.max.x - 0.55f; x += 0.35f)
            for (var z = floorBounds.min.z + 0.55f; z <= floorBounds.max.z - 0.55f; z += 0.35f)
                candidates.Add(new Vector3(x, floorBounds.max.y, z));
            candidates = candidates.OrderBy(item => (item - preferred).sqrMagnitude).ToList();
            foreach (var raw in candidates)
            {
                var candidate = new Vector3(raw.x, floorBounds.max.y, raw.z);
                if (candidate.z < panel.position.z + 0.65f) continue;
                var overlaps = Physics.OverlapCapsule(candidate + Vector3.up * 0.25f,
                        candidate + Vector3.up * 1.65f, 0.30f, ~0, QueryTriggerInteraction.Collide)
                    .Where(collider => !floors.Contains(collider)).ToArray();
                if (overlaps.Length == 0) return candidate;
            }
            errors.Add("No collision-free NPC01 station candidate beside the 403 work area");
            return new Vector3(panel.position.x - 1.75f, floorBounds.max.y, panel.position.z - 1.05f);
        }

        private static void ValidateSceneInvariants(GameObject root, ICollection<string> errors)
        {
            var stations = Object.FindObjectsByType<NpcStationController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (stations.Length != 1 || stations[0].gameObject != root)
                errors.Add("Expected exactly one NPC01 station controller");
            if (Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("NPC patch changed the one-XR-origin invariant");
            if (Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("NPC patch changed locomotion driver count");
            if (root.transform.localScale != Vector3.one) errors.Add("NPC station root scale must be one");
        }

        private static Bounds CalculateDeformedBounds(SkinnedMeshRenderer[] renderers, ICollection<string> errors)
        {
            if (renderers.Length == 0)
            {
                errors.Add("NPC01 renderer bounds unavailable");
                return default;
            }
            var initialized = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                foreach (var vertex in baked.vertices)
                {
                    var world = renderer.transform.TransformPoint(vertex);
                    if (!initialized)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        initialized = true;
                    }
                    else bounds.Encapsulate(world);
                }
                Object.DestroyImmediate(baked);
            }
            if (!initialized) errors.Add("NPC01 baked vertex bounds unavailable");
            return bounds;
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static Report CreateReport(Vector3 anchor, Vector3 bounds, int skinnedRenderers,
            int materials, int blendShapes, string[] clips, List<string> errors, int expressionBindings = 0) => new()
        {
            status = errors.Count == 0 ? "PASS" : "FAIL",
            unity_version = Application.unityVersion,
            scene = ScenePath,
            model = ModelPath,
            fbx_sha256 = ExpectedFbxHash,
            manifest_sha256 = ExpectedManifestHash,
            manifest_revision = "production_021",
            anchor_position_m = Vector(anchor),
            anchor_yaw_deg = 0f,
            imported_bounds_m = Vector(bounds),
            skinned_renderers = skinnedRenderers,
            materials = materials,
            blend_shapes = blendShapes,
            expression_bindings = expressionBindings,
            clips = clips,
            apply_root_motion = false,
            navmesh_agent = false,
            npc_station_roots = Object.FindObjectsByType<NpcStationController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            xr_origins = Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            locomotion_drivers = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length,
            quest = "NOT_RUN",
            errors = errors.ToArray(),
        };

        private static VectorData Vector(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };

        private static void WriteReport(Report report)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-19");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "unity_npc01_station_patch_021.json"),
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
