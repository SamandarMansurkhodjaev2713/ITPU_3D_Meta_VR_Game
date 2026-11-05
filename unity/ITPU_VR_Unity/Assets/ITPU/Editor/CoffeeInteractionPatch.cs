using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ITPU.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ITPU.XR.Editor
{
    public static class CoffeeInteractionPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string InteractionName = "COFFEE_INTERACTION_WO27";
        private const string LiquidMaterialPath = "Assets/ITPU/Art/Materials/ITPU_coffee_liquid.mat";

        [Serializable]
        private sealed class VectorData
        {
            public float x;
            public float y;
            public float z;
        }

        [Serializable]
        private sealed class NodeData
        {
            public string path;
            public string parent;
            public VectorData world_position;
            public VectorData local_position;
            public VectorData world_scale;
            public bool is_static;
            public int renderers;
            public int colliders;
            public string[] components;
        }

        [Serializable]
        private sealed class ColliderData
        {
            public string path;
            public string type;
            public bool trigger;
            public bool enabled;
            public VectorData bounds_center;
            public VectorData bounds_size;
            public VectorData local_center;
            public VectorData local_size;
        }

        [Serializable]
        private sealed class InspectionReport
        {
            public string status;
            public string unity_version;
            public string scene;
            public NodeData[] coffee_nodes;
            public NodeData[] cup_nodes;
            public ColliderData[] coffee_colliders;
            public ColliderData[] cup_colliders;
            public string[] errors;
        }

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string unity_version;
            public string scene;
            public int coffee_controllers;
            public int cup_grab_adapters;
            public int cup_rigidbodies;
            public int brew_buttons;
            public int stream_renderers;
            public int xr_origins;
            public int locomotion_drivers;
            public int c01_controllers;
            public int c02_controllers;
            public int c03_controllers;
            public bool cup_home_preserved;
            public bool environment_v005_preserved;
            public string quest;
            public string[] errors;
        }

        [MenuItem("ITPU/WO-27 Inspect Coffee Hierarchy")]
        public static void InspectBatch()
        {
            var errors = new List<string>();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var coffee = FindStable("K_COFFEE", errors);
            var cup = FindStable("K_CUP", errors);
            if (coffee != null && !coffee.gameObject.isStatic)
                errors.Add("K_COFFEE must remain static");
            if (cup != null && cup.gameObject.isStatic)
                errors.Add("K_CUP must be non-static");
            var report = new InspectionReport
            {
                status = errors.Count == 0 ? "PASS_READ_ONLY" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                coffee_nodes = coffee == null ? Array.Empty<NodeData>() : DescribeNodes(coffee),
                cup_nodes = cup == null ? Array.Empty<NodeData>() : DescribeNodes(cup),
                coffee_colliders = coffee == null ? Array.Empty<ColliderData>() : DescribeColliders(coffee),
                cup_colliders = cup == null ? Array.Empty<ColliderData>() : DescribeColliders(cup),
                errors = errors.ToArray(),
            };
            WriteReport("coffee_hierarchy_inspection.json", JsonUtility.ToJson(report, true));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-27 coffee hierarchy inspection PASS");
        }

        [MenuItem("ITPU/WO-27 Apply Coffee Interaction")]
        public static void ApplyBatch()
        {
            var errors = new List<string>();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var coffee = FindStable("K_COFFEE", errors);
            var cup = FindStable("K_CUP", errors);
            var authority = UnityEngine.Object.FindFirstObjectByType<ChallengeSessionAuthority>(
                FindObjectsInactive.Include);
            var priority = UnityEngine.Object.FindFirstObjectByType<InteractionPriority>(
                FindObjectsInactive.Include);
            if (authority == null) errors.Add("ChallengeSessionAuthority missing");
            if (priority == null) errors.Add("InteractionPriority missing");
            if (coffee == null || cup == null || authority == null || priority == null)
            {
                WritePatchReport(errors, false, false);
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            RemovePreviousPatch(cup);
            Physics.SyncTransforms();
            var cupColliders = cup.GetComponentsInChildren<Collider>(true);
            if (cupColliders.Length != 2) errors.Add("K_CUP must retain exactly two authored colliders");
            var cupBounds = CalculateBounds(cupColliders, errors, "K_CUP");
            var coffeeBoxes = coffee.GetComponentsInChildren<BoxCollider>(true);
            var buttonSource = FindClosestBox(coffeeBoxes, new Vector3(0.035f, 0.025f, 0.008f));
            var traySource = FindClosestBox(coffeeBoxes, new Vector3(0.276f, 0.024f, 0.261f));
            var spoutSource = FindClosestBox(coffeeBoxes, new Vector3(0.09f, 0.052f, 0.081f));
            var nozzleSources = coffeeBoxes.OrderBy(item => SizeError(item.size,
                    new Vector3(0.009f, 0.04f, 0.009f))).Take(2).ToArray();
            if (buttonSource == null || traySource == null || spoutSource == null || nozzleSources.Length != 2)
                errors.Add("Authored coffee button/tray/spout/nozzle colliders are incomplete");
            if (errors.Count > 0)
            {
                WritePatchReport(errors, false, false);
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            var root = new GameObject(InteractionName);
            var home = new GameObject("COFFEE_CUP_HOME").transform;
            home.SetParent(root.transform, false);
            home.SetPositionAndRotation(cup.position, cup.rotation);

            var trayTop = traySource.bounds.max.y;
            var cupBottomOffset = cup.position.y - cupBounds.min.y;
            var spoutWorld = spoutSource.transform.TransformPoint(spoutSource.center);
            var dock = new GameObject("COFFEE_CUP_DOCK").transform;
            dock.SetParent(root.transform, false);
            dock.SetPositionAndRotation(new Vector3(spoutWorld.x, trayTop + cupBottomOffset + 0.002f,
                spoutWorld.z), cup.rotation);

            var material = CreateLiquidMaterial();
            var buttonCenter = buttonSource.transform.TransformPoint(buttonSource.center);
            var buttonForward = buttonSource.transform.TransformDirection(Vector3.forward).normalized;
            var buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buttonObject.name = "COFFEE_BREW_BUTTON";
            buttonObject.transform.SetParent(root.transform, true);
            buttonObject.transform.SetPositionAndRotation(buttonCenter + buttonForward * 0.008f,
                buttonSource.transform.rotation);
            buttonObject.transform.localScale = new Vector3(0.05f, 0.04f, 0.018f);
            var buttonRenderer = buttonObject.GetComponent<Renderer>();
            var buttonMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/ITPU/Art/Materials/ITPU_accent.mat");
            if (buttonMaterial != null) buttonRenderer.sharedMaterial = buttonMaterial;
            var press = buttonObject.AddComponent<DemoPressInteractable>();
            press.Configure(buttonRenderer);

            var liquid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            liquid.name = "COFFEE_CUP_LIQUID";
            liquid.transform.SetParent(root.transform, true);
            UnityEngine.Object.DestroyImmediate(liquid.GetComponent<Collider>());
            liquid.GetComponent<Renderer>().sharedMaterial = material;
            liquid.transform.localScale = new Vector3(0.031f, 0.0015f, 0.031f);
            var emptyWorld = new Vector3(cupBounds.center.x, cupBounds.min.y + 0.012f, cupBounds.center.z);
            var fullWorld = new Vector3(cupBounds.center.x, cupBounds.max.y - 0.014f, cupBounds.center.z);
            var emptyLocal = cup.InverseTransformPoint(emptyWorld);
            var fullLocal = cup.InverseTransformPoint(fullWorld);
            liquid.transform.SetPositionAndRotation(emptyWorld, cup.rotation);
            liquid.SetActive(false);

            var streams = new GameObject("COFFEE_STREAMS");
            streams.transform.SetParent(root.transform, false);
            foreach (var nozzle in nozzleSources)
            {
                var nozzleCenter = nozzle.transform.TransformPoint(nozzle.center);
                var nozzleBottom = nozzleCenter - nozzle.transform.up * (nozzle.size.y * 0.5f);
                var cupRim = new Vector3(nozzleBottom.x, dock.position.y + cupBounds.size.y - 0.014f,
                    nozzleBottom.z);
                CreateStream(streams.transform, nozzleBottom, cupRim, material);
            }
            streams.SetActive(false);

            var controller = root.AddComponent<CoffeeInteractionController>();
            controller.Configure(authority, press, streams, liquid.transform, buttonRenderer,
                emptyLocal, fullLocal);
            var body = cup.gameObject.AddComponent<Rigidbody>();
            body.mass = 0.18f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var adapter = cup.gameObject.AddComponent<CoffeeCupGrabAdapter>();
            adapter.Configure(authority, priority, controller, body, home, dock);
            controller.SetCup(adapter);
            controller.ResetInteraction();
            adapter.ResetToHome();

            Physics.SyncTransforms();
            ValidatePatch(root, cup, home, errors);
            EditorSceneManager.MarkSceneDirty(scene);
            if (errors.Count == 0 && !EditorSceneManager.SaveScene(scene, ScenePath))
                errors.Add("Scene save returned false");
            AssetDatabase.SaveAssets();
            WritePatchReport(errors, Vector3.Distance(cup.position, home.position) < 0.001f,
                UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Any(item => item.Value == "environment.full.v005"));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-27 coffee interaction patch PASS");
        }

        private static void RemovePreviousPatch(Transform cup)
        {
            var previous = SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(item => item.name == InteractionName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var adapter = cup.GetComponent<CoffeeCupGrabAdapter>();
            if (adapter != null) UnityEngine.Object.DestroyImmediate(adapter);
            var body = cup.GetComponent<Rigidbody>();
            if (body != null) UnityEngine.Object.DestroyImmediate(body);
        }

        private static Bounds CalculateBounds(Collider[] colliders, ICollection<string> errors,
            string label)
        {
            if (colliders == null || colliders.Length == 0)
            {
                errors.Add(label + " has no colliders");
                return default;
            }
            var bounds = colliders[0].bounds;
            foreach (var collider in colliders.Skip(1)) bounds.Encapsulate(collider.bounds);
            return bounds;
        }

        private static BoxCollider FindClosestBox(IEnumerable<BoxCollider> colliders, Vector3 expected) =>
            colliders.OrderBy(item => SizeError(item.size, expected)).FirstOrDefault();

        private static float SizeError(Vector3 actual, Vector3 expected) =>
            Mathf.Abs(actual.x - expected.x) + Mathf.Abs(actual.y - expected.y) +
            Mathf.Abs(actual.z - expected.z);

        private static Material CreateLiquidMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(LiquidMaterialPath);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit shader unavailable");
            if (material == null)
            {
                material = new Material(shader) { name = "ITPU_coffee_liquid" };
                AssetDatabase.CreateAsset(material, LiquidMaterialPath);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.17f, 0.055f, 0.018f, 1f));
            material.SetColor("_Color", new Color(0.17f, 0.055f, 0.018f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.78f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateStream(Transform parent, Vector3 start, Vector3 end, Material material)
        {
            var midpoint = (start + end) * 0.5f;
            var direction = end - start;
            var stream = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stream.name = "COFFEE_STREAM";
            stream.transform.SetParent(parent, true);
            stream.transform.SetPositionAndRotation(midpoint,
                direction.sqrMagnitude < 0.000001f
                    ? Quaternion.identity
                    : Quaternion.FromToRotation(Vector3.up, direction.normalized));
            stream.transform.localScale = new Vector3(0.0035f, direction.magnitude * 0.5f, 0.0035f);
            UnityEngine.Object.DestroyImmediate(stream.GetComponent<Collider>());
            stream.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void ValidatePatch(GameObject root, Transform cup, Transform home,
            ICollection<string> errors)
        {
            if (UnityEngine.Object.FindObjectsByType<CoffeeInteractionController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one CoffeeInteractionController");
            if (UnityEngine.Object.FindObjectsByType<CoffeeCupGrabAdapter>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one CoffeeCupGrabAdapter");
            if (cup.GetComponents<Rigidbody>().Length != 1)
                errors.Add("K_CUP must have exactly one Rigidbody");
            if (root.GetComponentsInChildren<DemoPressInteractable>(true).Length != 1)
                errors.Add("Coffee patch must have exactly one brew button");
            if (root.GetComponentsInChildren<Renderer>(true).Length != 4)
                errors.Add("Coffee patch must contain one button, one liquid and two stream renderers");
            if (Vector3.Distance(cup.position, home.position) > 0.001f)
                errors.Add("Cup home pose moved during patch");
            if (UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one XR origin");
            if (UnityEngine.Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one locomotion driver");
            if (UnityEngine.Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("C01/C02/C03 controllers were not preserved");
        }

        private static void WritePatchReport(ICollection<string> errors, bool cupHomePreserved,
            bool environmentPreserved)
        {
            var report = new PatchReport
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                coffee_controllers = UnityEngine.Object.FindObjectsByType<CoffeeInteractionController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                cup_grab_adapters = UnityEngine.Object.FindObjectsByType<CoffeeCupGrabAdapter>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                cup_rigidbodies = FindStableNoErrors("K_CUP")?.GetComponents<Rigidbody>().Length ?? 0,
                brew_buttons = UnityEngine.Object.FindObjectsByType<DemoPressInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Count(item => item.gameObject.name == "COFFEE_BREW_BUTTON"),
                stream_renderers = GameObject.Find(InteractionName)?.transform.Find("COFFEE_STREAMS")
                    ?.GetComponentsInChildren<Renderer>(true).Length ?? 0,
                xr_origins = UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                locomotion_drivers = UnityEngine.Object.FindObjectsByType<XrLocomotionDriver>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c01_controllers = UnityEngine.Object.FindObjectsByType<C01ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c02_controllers = UnityEngine.Object.FindObjectsByType<C02ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c03_controllers = UnityEngine.Object.FindObjectsByType<C03ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                cup_home_preserved = cupHomePreserved,
                environment_v005_preserved = environmentPreserved,
                quest = "NOT_RUN",
                errors = errors.ToArray(),
            };
            WriteReport("coffee_interaction_patch.json", JsonUtility.ToJson(report, true));
        }

        private static Transform FindStableNoErrors(string value) =>
            UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).SingleOrDefault(item => item.Value == value)?.transform;

        private static Transform FindStable(string value, ICollection<string> errors)
        {
            var matches = UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(item => item.Value == value).ToArray();
            if (matches.Length != 1)
            {
                errors.Add($"Expected one {value} StableId, found {matches.Length}");
                return null;
            }
            return matches[0].transform;
        }

        private static NodeData[] DescribeNodes(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).Select(node => new NodeData
            {
                path = PathOf(node),
                parent = node.parent == null ? "" : PathOf(node.parent),
                world_position = FromVector(node.position),
                local_position = FromVector(node.localPosition),
                world_scale = FromVector(node.lossyScale),
                is_static = node.gameObject.isStatic,
                renderers = node.GetComponents<Renderer>().Length,
                colliders = node.GetComponents<Collider>().Length,
                components = node.GetComponents<Component>().Where(item => item != null)
                    .Select(item => item.GetType().FullName).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            }).OrderBy(item => item.path, StringComparer.Ordinal).ToArray();

        private static ColliderData[] DescribeColliders(Transform root) =>
            root.GetComponentsInChildren<Collider>(true).Select(collider =>
            {
                var localCenter = Vector3.zero;
                var localSize = Vector3.zero;
                if (collider is BoxCollider box)
                {
                    localCenter = box.center;
                    localSize = box.size;
                }
                else if (collider is CapsuleCollider capsule)
                {
                    localCenter = capsule.center;
                    localSize = new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f);
                }
                return new ColliderData
                {
                    path = PathOf(collider.transform),
                    type = collider.GetType().FullName,
                    trigger = collider.isTrigger,
                    enabled = collider.enabled,
                    bounds_center = FromVector(collider.bounds.center),
                    bounds_size = FromVector(collider.bounds.size),
                    local_center = FromVector(localCenter),
                    local_size = FromVector(localSize),
                };
            }).OrderBy(item => item.path, StringComparer.Ordinal)
              .ThenBy(item => item.type, StringComparer.Ordinal).ToArray();

        private static string PathOf(Transform node)
        {
            var names = new Stack<string>();
            for (var current = node; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names);
        }

        private static VectorData FromVector(Vector3 value) =>
            new() { x = value.x, y = value.y, z = value.z };

        private static void WriteReport(string filename, string contents)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-27");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), contents + Environment.NewLine);
        }
    }
}
