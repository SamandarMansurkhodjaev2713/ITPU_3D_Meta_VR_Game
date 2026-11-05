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
    public static class BoardInteractionPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string RootName = "BOARD_INTERACTION_WO28";
        private const string SurfaceName = "BOARD_DRAW_SURFACE";
        private const string UndoName = "BOARD_UNDO_BUTTON";
        private const string ColorName = "BOARD_COLOR_BUTTON";
        private const string MaterialPath = "Assets/ITPU/Art/Materials/ITPU_board_drawable.mat";
        private static string reportWorkOrder = "WO-28";

        [Serializable]
        private sealed class ColliderRecord
        {
            public string path;
            public float[] local_center;
            public float[] local_size;
            public float[] world_center;
            public float[] world_size;
        }

        [Serializable]
        private sealed class InspectionReport
        {
            public string status;
            public string unity_version;
            public string scene;
            public int flipchart_matches;
            public int marker_matches;
            public int eraser_matches;
            public ColliderRecord[] flipchart_boxes;
            public string selected_board_collider;
            public string quest;
            public string[] errors;
        }

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string unity_version;
            public string scene;
            public int board_controllers;
            public int board_commands;
            public int environment_renderers;
            public int environment_box_colliders;
            public int environment_capsule_colliders;
            public int xr_origins;
            public int locomotion_drivers;
            public int coffee_controllers;
            public int c01_controllers;
            public int c02_controllers;
            public int c03_controllers;
            public string board_source_path;
            public float[] board_source_local_size;
            public float[] board_surface_world_position;
            public string input_contract;
            public string quest;
            public string[] errors;
        }

        [MenuItem("ITPU/WO-28 Inspect Board Hierarchy")]
        public static void InspectBatch()
        {
            var errors = new List<string>();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var flipchart = FindStable("FLIPCHART", errors);
            var marker = FindStable("MARKER", errors);
            var eraser = FindStable("ERASER", errors);
            if (flipchart != null && !flipchart.gameObject.isStatic)
                errors.Add("FLIPCHART must remain static");
            if (marker != null && marker.gameObject.isStatic)
                errors.Add("MARKER must remain movable");
            if (eraser != null && eraser.gameObject.isStatic)
                errors.Add("ERASER must remain movable");
            var boxes = flipchart == null
                ? Array.Empty<BoxCollider>()
                : flipchart.GetComponentsInChildren<BoxCollider>(true);
            var selected = FindBoardBox(boxes);
            if (flipchart != null && selected == null)
                errors.Add("The authored 0.75 x 1.11 x 0.03 board collider was not found");
            var report = new InspectionReport
            {
                status = errors.Count == 0 ? "PASS_READ_ONLY" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                flipchart_matches = CountStable("FLIPCHART"),
                marker_matches = CountStable("MARKER"),
                eraser_matches = CountStable("ERASER"),
                flipchart_boxes = boxes.Select(Describe).OrderBy(item => item.path,
                    StringComparer.Ordinal).ToArray(),
                selected_board_collider = selected == null ? "" : PathOf(selected.transform),
                quest = "NOT_RUN",
                errors = errors.ToArray(),
            };
            WriteReport("board_hierarchy_inspection.json", JsonUtility.ToJson(report, true));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-28 board hierarchy inspection PASS");
        }

        [MenuItem("ITPU/WO-28 Apply Board Interaction")]
        public static void ApplyBatch()
        {
            var errors = new List<string>();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var flipchart = FindStable("FLIPCHART", errors);
            var marker = FindStable("MARKER", errors);
            var eraser = FindStable("ERASER", errors);
            var authority = UnityEngine.Object.FindFirstObjectByType<ChallengeSessionAuthority>(
                FindObjectsInactive.Include);
            var priority = UnityEngine.Object.FindFirstObjectByType<InteractionPriority>(
                FindObjectsInactive.Include);
            if (authority == null) errors.Add("ChallengeSessionAuthority missing");
            if (priority == null) errors.Add("InteractionPriority missing");
            var boardSource = flipchart == null
                ? null
                : FindBoardBox(flipchart.GetComponentsInChildren<BoxCollider>(true));
            if (boardSource == null) errors.Add("Authored board collider missing");
            if (errors.Count > 0)
            {
                WritePatchReport(errors, boardSource, null);
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            RemovePreviousPatch(marker, eraser);
            var root = new GameObject(RootName);
            var center = boardSource.transform.TransformPoint(boardSource.center);
            var normal = boardSource.transform.TransformDirection(Vector3.forward).normalized;
            if (Vector3.Dot(marker.position - center, normal) < 0f) normal = -normal;
            var up = boardSource.transform.TransformDirection(Vector3.up).normalized;
            var right = boardSource.transform.TransformDirection(Vector3.right).normalized;
            if (Mathf.Abs(Vector3.Dot(up, Vector3.up)) < 0.5f)
            {
                up = Vector3.up;
                right = Vector3.Cross(up, normal).normalized;
            }

            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = SurfaceName;
            surface.transform.SetParent(root.transform, true);
            surface.transform.SetPositionAndRotation(center + normal * 0.018f,
                Quaternion.LookRotation(normal, up));
            surface.transform.localScale = new Vector3(0.72f, 1.08f, 1f);
            UnityEngine.Object.DestroyImmediate(surface.GetComponent<MeshCollider>());
            var surfaceCollider = surface.AddComponent<BoxCollider>();
            surfaceCollider.size = new Vector3(1f, 1f, 0.025f);
            surface.GetComponent<Renderer>().sharedMaterial = CreateBoardMaterial();
            var board = surface.AddComponent<BoardDrawingController>();
            board.Configure(authority, priority, surface.GetComponent<Renderer>(), surfaceCollider);

            marker.gameObject.AddComponent<BoardCommandInteractable>()
                .Configure(board, BoardCommand.Marker);
            eraser.gameObject.AddComponent<BoardCommandInteractable>()
                .Configure(board, BoardCommand.Eraser);

            var undo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            undo.name = UndoName;
            undo.transform.SetParent(root.transform, true);
            undo.transform.SetPositionAndRotation(center + right * 0.48f - up * 0.46f + normal * 0.045f,
                Quaternion.LookRotation(normal, up));
            undo.transform.localScale = new Vector3(0.13f, 0.075f, 0.025f);
            var accent = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/ITPU/Art/Materials/ITPU_accent.mat");
            if (accent != null) undo.GetComponent<Renderer>().sharedMaterial = accent;
            undo.AddComponent<BoardCommandInteractable>().Configure(board, BoardCommand.Undo);
            CreateLabel(root.transform, "UNDO_LABEL", "UNDO", undo.transform.position + normal * 0.03f,
                Quaternion.LookRotation(-normal, up));

            var color = GameObject.CreatePrimitive(PrimitiveType.Cube);
            color.name = ColorName;
            color.transform.SetParent(root.transform, true);
            color.transform.SetPositionAndRotation(center + right * 0.30f - up * 0.46f + normal * 0.045f,
                Quaternion.LookRotation(normal, up));
            color.transform.localScale = new Vector3(0.16f, 0.075f, 0.025f);
            if (accent != null) color.GetComponent<Renderer>().sharedMaterial = accent;
            color.AddComponent<BoardCommandInteractable>().Configure(board, BoardCommand.ColorNext);
            CreateLabel(root.transform, "COLOR_LABEL", "COLOR", color.transform.position + normal * 0.03f,
                Quaternion.LookRotation(-normal, up));

            Physics.SyncTransforms();
            ValidatePatch(errors, root, boardSource);
            EditorSceneManager.MarkSceneDirty(scene);
            if (errors.Count == 0 && !EditorSceneManager.SaveScene(scene, ScenePath))
                errors.Add("Scene save returned false");
            AssetDatabase.SaveAssets();
            WritePatchReport(errors, boardSource, surface.transform);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            Debug.Log("WO-28 board interaction patch PASS");
        }

        [MenuItem("ITPU/WO-30 Apply Board Color Polish")]
        public static void ApplyColorBatch()
        {
            reportWorkOrder = "WO-30";
            ApplyBatch();
        }

        private static void RemovePreviousPatch(Transform marker, Transform eraser)
        {
            var previous = SceneManager.GetActiveScene().GetRootGameObjects()
                .SingleOrDefault(item => item.name == RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            foreach (var root in new[] { marker, eraser })
            foreach (var command in root.GetComponents<BoardCommandInteractable>())
                UnityEngine.Object.DestroyImmediate(command);
        }

        private static BoxCollider FindBoardBox(IEnumerable<BoxCollider> colliders) =>
            colliders.OrderBy(item => SizeError(item.size, new Vector3(0.75f, 1.11f, 0.03f)))
                .FirstOrDefault(item => SizeError(item.size, new Vector3(0.75f, 1.11f, 0.03f)) < 0.08f);

        private static float SizeError(Vector3 actual, Vector3 expected) =>
            Mathf.Abs(actual.x - expected.x) + Mathf.Abs(actual.y - expected.y) +
            Mathf.Abs(actual.z - expected.z);

        private static Material CreateBoardMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP/Unlit shader unavailable");
            if (material == null)
            {
                material = new Material(shader) { name = "ITPU_board_drawable" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.925f, 0.933f, 0.91f, 1f));
            material.SetColor("_Color", new Color(0.925f, 0.933f, 0.91f, 1f));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ValidatePatch(ICollection<string> errors, GameObject root,
            BoxCollider boardSource)
        {
            if (UnityEngine.Object.FindObjectsByType<BoardDrawingController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one BoardDrawingController");
            var commands = UnityEngine.Object.FindObjectsByType<BoardCommandInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (commands.Length != 4 || commands.Select(item => item.Command).Distinct().Count() != 4)
                errors.Add("Expected exactly marker, eraser, undo and color board commands");
            if (root.GetComponentsInChildren<Renderer>(true).Length != 5)
                errors.Add("Board patch must add surface, two buttons and two labels");
            if (root.GetComponentsInChildren<Collider>(true).Length != 3)
                errors.Add("Board patch must add only the surface, undo and color colliders");
            if (boardSource == null || SizeError(boardSource.size,
                    new Vector3(0.75f, 1.11f, 0.03f)) >= 0.08f)
                errors.Add("Board source collider contract changed");
            if (UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one XR origin");
            if (UnityEngine.Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Expected exactly one locomotion driver");
            if (UnityEngine.Object.FindObjectsByType<CoffeeInteractionController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("Coffee interaction was not preserved");
            if (UnityEngine.Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1 ||
                UnityEngine.Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1)
                errors.Add("C01/C02/C03 controllers were not preserved");
            var environment = FindStableNoErrors("environment.full.v005");
            if (environment == null || environment.GetComponentsInChildren<Renderer>(true).Length != 626 ||
                environment.GetComponentsInChildren<BoxCollider>(true).Length != 549 ||
                environment.GetComponentsInChildren<CapsuleCollider>(true).Length != 1)
                errors.Add("Accepted environment renderer/collider contract changed");
        }

        private static void WritePatchReport(ICollection<string> errors, BoxCollider boardSource,
            Transform surface)
        {
            var environment = FindStableNoErrors("environment.full.v005");
            var report = new PatchReport
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                board_controllers = UnityEngine.Object.FindObjectsByType<BoardDrawingController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                board_commands = UnityEngine.Object.FindObjectsByType<BoardCommandInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                environment_renderers = environment?.GetComponentsInChildren<Renderer>(true).Length ?? 0,
                environment_box_colliders = environment?.GetComponentsInChildren<BoxCollider>(true).Length ?? 0,
                environment_capsule_colliders = environment?.GetComponentsInChildren<CapsuleCollider>(true).Length ?? 0,
                xr_origins = UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                locomotion_drivers = UnityEngine.Object.FindObjectsByType<XrLocomotionDriver>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                coffee_controllers = UnityEngine.Object.FindObjectsByType<CoffeeInteractionController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c01_controllers = UnityEngine.Object.FindObjectsByType<C01ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c02_controllers = UnityEngine.Object.FindObjectsByType<C02ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c03_controllers = UnityEngine.Object.FindObjectsByType<C03ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                board_source_path = boardSource == null ? "" : PathOf(boardSource.transform),
                board_source_local_size = boardSource == null ? Array.Empty<float>() : Values(boardSource.size),
                board_surface_world_position = surface == null ? Array.Empty<float>() : Values(surface.position),
                input_contract = "controller-trigger + optical-hand-pinch; one stroke owner; board outranks teleport",
                quest = "NOT_RUN",
                errors = errors.ToArray(),
            };
            WriteReport("board_interaction_patch.json", JsonUtility.ToJson(report, true));
        }

        private static ColliderRecord Describe(BoxCollider collider) => new()
        {
            path = PathOf(collider.transform),
            local_center = Values(collider.center),
            local_size = Values(collider.size),
            world_center = Values(collider.bounds.center),
            world_size = Values(collider.bounds.size),
        };

        private static void CreateLabel(Transform parent, string name, string value, Vector3 position,
            Quaternion rotation)
        {
            var label = new GameObject(name);
            label.transform.SetParent(parent, true);
            label.transform.SetPositionAndRotation(position, rotation);
            var text = label.AddComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.018f;
            text.color = new Color(0.025f, 0.035f, 0.045f, 1f);
        }

        private static Transform FindStable(string value, ICollection<string> errors)
        {
            var matches = UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.Value == value).ToArray();
            if (matches.Length == 1) return matches[0].transform;
            errors.Add($"Expected one {value} StableId, found {matches.Length}");
            return null;
        }

        private static Transform FindStableNoErrors(string value) =>
            UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).SingleOrDefault(item => item.Value == value)?.transform;

        private static int CountStable(string value) =>
            UnityEngine.Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Count(item => item.Value == value);

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
            var directory = Path.Combine(workspace, "reports", "local", reportWorkOrder);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), contents + Environment.NewLine);
        }
    }
}
