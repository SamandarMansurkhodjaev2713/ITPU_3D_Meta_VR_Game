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
    public static class PaperBasketInteractionPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string RootName = "PAPER_BASKET_INTERACTION_WO29";
        private const string MaterialPath = "Assets/ITPU/Art/Materials/ITPU_paper.mat";

        [Serializable]
        private sealed class ColliderRecord
        {
            public string path;
            public string type;
            public bool convex;
            public bool trigger;
            public float[] world_center;
            public float[] world_size;
        }

        [Serializable]
        private sealed class InspectionReport
        {
            public string status;
            public string unity_version;
            public string scene;
            public int bin_matches;
            public bool bin_static;
            public float[] bin_world_position;
            public ColliderRecord[] bin_colliders;
            public ColliderRecord[] nearby_support_candidates;
            public string quest;
            public string[] errors;
        }

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string unity_version;
            public string scene;
            public int paper_props;
            public int paper_rigidbodies;
            public int basket_sensors;
            public int bin_mesh_colliders;
            public bool bin_mesh_nonconvex;
            public int environment_renderers;
            public int environment_box_colliders;
            public int environment_capsule_colliders;
            public int xr_origins;
            public int locomotion_drivers;
            public int coffee_controllers;
            public int board_controllers;
            public int c01_controllers;
            public int c02_controllers;
            public int c03_controllers;
            public float[] mouth_center;
            public float[] mouth_size;
            public float[] inside_center;
            public float[] inside_size;
            public string paper_support_path;
            public string input_contract;
            public string quest;
            public string[] errors;
        }

        [MenuItem("ITPU/WO-29 Inspect Paper Basket Hierarchy")]
        public static void InspectBatch()
        {
            var errors = new List<string>();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var bin = FindStable("BIN_403", errors);
            if (bin != null && !bin.gameObject.isStatic) errors.Add("BIN_403 must remain static");
            var colliders = bin == null ? Array.Empty<Collider>() : bin.GetComponentsInChildren<Collider>(true);
            if (bin != null && colliders.Length == 0) errors.Add("BIN_403 has no authored collider");
            var nearby = bin == null ? Array.Empty<Collider>() : UnityEngine.Object
                .FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(item => item != null && !item.transform.IsChildOf(bin) && !item.isTrigger &&
                               Vector3.Distance(item.bounds.center, bin.position) <= 3f &&
                               item.bounds.max.y >= 0.55f && item.bounds.max.y <= 1.25f &&
                               item.bounds.size.y <= 0.12f && item.bounds.size.x >= 0.25f &&
                               item.bounds.size.z >= 0.25f)
                .OrderBy(item => Vector3.Distance(item.bounds.center, bin.position)).Take(16).ToArray();
            var report = new InspectionReport
            {
                status = errors.Count == 0 ? "PASS_READ_ONLY" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                bin_matches = CountStable("BIN_403"),
                bin_static = bin != null && bin.gameObject.isStatic,
                bin_world_position = bin == null ? Array.Empty<float>() : Values(bin.position),
                bin_colliders = colliders.Select(Describe).ToArray(),
                nearby_support_candidates = nearby.Select(Describe).ToArray(),
                quest = "NOT_RUN",
                errors = errors.ToArray(),
            };
            WriteReport("paper_basket_hierarchy_inspection.json", JsonUtility.ToJson(report, true));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-29 paper basket hierarchy inspection PASS");
        }

        [MenuItem("ITPU/WO-29 Apply Paper Basket Interaction")]
        public static void ApplyBatch()
        {
            var errors = new List<string>();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var bin = FindStable("BIN_403", errors);
            var authority = UnityEngine.Object.FindFirstObjectByType<ChallengeSessionAuthority>(
                FindObjectsInactive.Include);
            var priority = UnityEngine.Object.FindFirstObjectByType<InteractionPriority>(
                FindObjectsInactive.Include);
            if (authority == null) errors.Add("ChallengeSessionAuthority missing");
            if (priority == null) errors.Add("InteractionPriority missing");
            var binMesh = bin == null ? null : bin.GetComponentsInChildren<MeshCollider>(true)
                .SingleOrDefault(item => !item.convex);
            if (binMesh == null) errors.Add("BIN_403 static non-convex MeshCollider missing");
            var support = bin == null ? null : FindPaperSupport(bin);
            if (support == null) errors.Add("Nearby horizontal paper support surface missing");
            if (errors.Count > 0)
            {
                WritePatchReport(errors, binMesh, null, null, support);
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            RemovePreviousPatch();
            var root = new GameObject(RootName);
            var binBounds = binMesh.bounds;
            var mouth = CreateSensor(root.transform, "PAPER_BASKET_MOUTH",
                new Vector3(binBounds.center.x, binBounds.max.y - 0.015f, binBounds.center.z),
                new Vector3(0.19f, 0.08f, 0.19f), PaperBasketSensorKind.Mouth);
            var inside = CreateSensor(root.transform, "PAPER_BASKET_INSIDE",
                new Vector3(binBounds.center.x, binBounds.min.y + 0.15f, binBounds.center.z),
                new Vector3(0.16f, 0.22f, 0.16f), PaperBasketSensorKind.Inside);
            var material = CreatePaperMaterial();
            var homeY = support.bounds.max.y + 0.008f;
            for (var index = 0; index < 3; index++)
            {
                var offset = (index - 1) * 0.205f;
                var homePosition = new Vector3(support.bounds.center.x + offset, homeY,
                    support.bounds.center.z - 0.18f);
                CreatePaper(root.transform, index, homePosition, authority, priority, material);
            }

            Physics.SyncTransforms();
            ValidatePatch(errors, bin, binMesh, root);
            EditorSceneManager.MarkSceneDirty(scene);
            if (errors.Count == 0 && !EditorSceneManager.SaveScene(scene, ScenePath))
                errors.Add("Scene save returned false");
            AssetDatabase.SaveAssets();
            WritePatchReport(errors, binMesh, mouth, inside, support);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-29 paper basket interaction patch PASS");
        }

        private static void RemovePreviousPatch()
        {
            var previous = SceneManager.GetActiveScene().GetRootGameObjects()
                .SingleOrDefault(item => item.name == RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        }

        private static BoxCollider CreateSensor(Transform parent, string name, Vector3 center,
            Vector3 size, PaperBasketSensorKind kind)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, true);
            target.transform.position = center;
            var box = target.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;
            target.AddComponent<PaperBasketSensor>().Configure(kind);
            return box;
        }

        private static void CreatePaper(Transform parent, int index, Vector3 homePosition,
            ChallengeSessionAuthority authority, InteractionPriority priority, Material material)
        {
            var home = new GameObject($"PAPER_HOME_{index}").transform;
            home.SetParent(parent, true);
            home.SetPositionAndRotation(homePosition, Quaternion.identity);

            var paper = new GameObject($"PAPER_PROP_{index}");
            paper.transform.SetParent(parent, true);
            paper.transform.SetPositionAndRotation(homePosition, Quaternion.identity);
            paper.AddComponent<StableId>().Set($"PAPER_PROP_{index}");
            var sheetCollider = paper.AddComponent<BoxCollider>();
            sheetCollider.size = new Vector3(0.18f, 0.008f, 0.12f);
            var crumpledCollider = paper.AddComponent<SphereCollider>();
            crumpledCollider.radius = 0.04f;
            crumpledCollider.enabled = false;
            var body = paper.AddComponent<Rigidbody>();
            body.mass = 0.025f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sheet.name = "PAPER_SHEET_VISUAL";
            sheet.transform.SetParent(paper.transform, false);
            sheet.transform.localScale = new Vector3(0.18f, 0.004f, 0.12f);
            UnityEngine.Object.DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.GetComponent<Renderer>().sharedMaterial = material;

            var crumpled = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crumpled.name = "PAPER_CRUMPLED_VISUAL";
            crumpled.transform.SetParent(paper.transform, false);
            crumpled.transform.localScale = Vector3.one * 0.075f;
            UnityEngine.Object.DestroyImmediate(crumpled.GetComponent<Collider>());
            crumpled.GetComponent<Renderer>().sharedMaterial = material;
            crumpled.SetActive(false);

            var adapter = paper.AddComponent<PaperPropGrabAdapter>();
            adapter.Configure(authority, priority, body, home, sheet, crumpled,
                sheetCollider, crumpledCollider);
        }

        private static Collider FindPaperSupport(Transform bin) => UnityEngine.Object
            .FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(item => item != null && !item.transform.IsChildOf(bin) && !item.isTrigger &&
                           Vector3.Distance(item.bounds.center, bin.position) <= 3f &&
                           item.bounds.max.y >= 0.55f && item.bounds.max.y <= 1.25f &&
                           item.bounds.size.y <= 0.12f && item.bounds.size.x >= 0.6f &&
                           item.bounds.size.z >= 0.6f)
            .OrderBy(item => Vector3.Distance(item.bounds.center, bin.position)).FirstOrDefault();

        private static Material CreatePaperMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit shader unavailable");
            if (material == null)
            {
                material = new Material(shader) { name = "ITPU_paper" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.94f, 0.925f, 0.875f, 1f));
            material.SetColor("_Color", new Color(0.94f, 0.925f, 0.875f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.16f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidatePatch(ICollection<string> errors, Transform bin,
            MeshCollider binMesh, GameObject root)
        {
            var papers = UnityEngine.Object.FindObjectsByType<PaperPropGrabAdapter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (papers.Length != 3) errors.Add("Expected exactly three pooled paper props");
            if (papers.Select(item => item.GetComponent<Rigidbody>()).Where(item => item != null).Distinct().Count() != 3)
                errors.Add("Expected exactly three paper rigidbodies");
            var sensors = root.GetComponentsInChildren<PaperBasketSensor>(true);
            if (sensors.Length != 2 || sensors.Select(item => item.Kind).Distinct().Count() != 2)
                errors.Add("Expected exactly mouth and inside basket sensors");
            if (bin == null || binMesh == null || binMesh.convex || binMesh.isTrigger)
                errors.Add("BIN_403 non-convex physical container contract changed");
            for (var index = 0; index < 3; index++)
                if (CountStable($"PAPER_PROP_{index}") != 1)
                    errors.Add($"Expected one PAPER_PROP_{index} StableId");
            var environment = FindStableNoErrors("environment.full.v005");
            if (environment == null || environment.GetComponentsInChildren<Renderer>(true).Length != 626 ||
                environment.GetComponentsInChildren<BoxCollider>(true).Length != 549 ||
                environment.GetComponentsInChildren<CapsuleCollider>(true).Length != 1)
                errors.Add("Accepted environment renderer/collider contract changed");
            if (UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("One XR origin/mover contract changed");
            if (UnityEngine.Object.FindObjectsByType<CoffeeInteractionController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<BoardDrawingController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Coffee or board interaction was not preserved");
            if (UnityEngine.Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("C01/C02/C03 controllers were not preserved");
        }

        private static void WritePatchReport(ICollection<string> errors, MeshCollider binMesh,
            BoxCollider mouth, BoxCollider inside, Collider support)
        {
            var environment = FindStableNoErrors("environment.full.v005");
            var papers = UnityEngine.Object.FindObjectsByType<PaperPropGrabAdapter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var report = new PatchReport
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                paper_props = papers.Length,
                paper_rigidbodies = papers.Count(item => item.GetComponent<Rigidbody>() != null),
                basket_sensors = UnityEngine.Object.FindObjectsByType<PaperBasketSensor>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                bin_mesh_colliders = binMesh == null ? 0 : 1,
                bin_mesh_nonconvex = binMesh != null && !binMesh.convex,
                environment_renderers = environment?.GetComponentsInChildren<Renderer>(true).Length ?? 0,
                environment_box_colliders = environment?.GetComponentsInChildren<BoxCollider>(true).Length ?? 0,
                environment_capsule_colliders = environment?.GetComponentsInChildren<CapsuleCollider>(true).Length ?? 0,
                xr_origins = UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                locomotion_drivers = UnityEngine.Object.FindObjectsByType<XrLocomotionDriver>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                coffee_controllers = UnityEngine.Object.FindObjectsByType<CoffeeInteractionController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                board_controllers = UnityEngine.Object.FindObjectsByType<BoardDrawingController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c01_controllers = UnityEngine.Object.FindObjectsByType<C01ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c02_controllers = UnityEngine.Object.FindObjectsByType<C02ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c03_controllers = UnityEngine.Object.FindObjectsByType<C03ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                mouth_center = mouth == null ? Array.Empty<float>() : Values(mouth.bounds.center),
                mouth_size = mouth == null ? Array.Empty<float>() : Values(mouth.bounds.size),
                inside_center = inside == null ? Array.Empty<float>() : Values(inside.bounds.center),
                inside_size = inside == null ? Array.Empty<float>() : Values(inside.bounds.size),
                paper_support_path = support == null ? "" : PathOf(support.transform),
                input_contract = "one generic XrGrabTarget path; controller trigger crumples + grip grabs; first hand pinch crumples + second pinch grabs",
                quest = "NOT_RUN",
                errors = errors.ToArray(),
            };
            WriteReport("paper_basket_interaction_patch.json", JsonUtility.ToJson(report, true));
        }

        private static ColliderRecord Describe(Collider collider) => new()
        {
            path = PathOf(collider.transform),
            type = collider.GetType().FullName,
            convex = collider is MeshCollider mesh && mesh.convex,
            trigger = collider.isTrigger,
            world_center = Values(collider.bounds.center),
            world_size = Values(collider.bounds.size),
        };

        private static Transform FindStable(string value, ICollection<string> errors)
        {
            var matches = UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.Value == value).ToArray();
            if (matches.Length == 1) return matches[0].transform;
            errors.Add($"Expected one {value} StableId, found {matches.Length}");
            return null;
        }

        private static int CountStable(string value) => UnityEngine.Object
            .FindObjectsByType<StableId>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(item => item.Value == value);

        private static Transform FindStableNoErrors(string value) => UnityEngine.Object
            .FindObjectsByType<StableId>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .SingleOrDefault(item => item.Value == value)?.transform;

        private static string PathOf(Transform node)
        {
            var names = new Stack<string>();
            for (var current = node; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names);
        }

        private static float[] Values(Vector3 value) => new[] { value.x, value.y, value.z };

        private static void WriteReport(string filename, string contents)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-29");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), contents + Environment.NewLine);
        }
    }
}
