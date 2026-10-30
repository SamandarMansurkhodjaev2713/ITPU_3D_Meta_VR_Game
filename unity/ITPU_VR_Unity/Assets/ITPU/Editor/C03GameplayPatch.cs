using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class C03GameplayPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string RootName = "C03_CHALLENGE_ROOT";
        private const string AccentPath = "Assets/ITPU/Art/Materials/ITPU_accent.mat";
        private const string DarkPath = "Assets/ITPU/Art/Materials/ITPU_dark.mat";
        // Keep C03 on the free section of the accepted 403 wood wall. The FBX import
        // mirrors authored X, so positive authored room coordinates live at negative
        // Unity X. The previous +1.80 m placement put the whole panel outside 403.
        private const float PanelCenterX = -1.65f;
        private const float PanelSurfaceZ = -16.59f;
        private const float ContentSurfaceZ = -16.54f;
        private const float ButtonSurfaceZ = -16.48f;

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string observed_utc;
            public string scene;
            public string mode = "C03_LOCAL_AUTHORIZATION_SANDBOX";
            public int xr_origins;
            public int locomotion_drivers;
            public int session_authorities;
            public int c01_controllers;
            public int c02_controllers;
            public int c03_controllers;
            public int c03_buttons;
            public int total_interactables;
            public string[] errors;
        }

        [MenuItem("ITPU/Apply WO-09 C03 Gameplay")]
        public static void Apply()
        {
            var errors = new List<string>();
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Saved XR scene is missing", ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var movers = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var authorities = Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var resets = Object.FindObjectsByType<VerticalSliceReset>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var lifecycles = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (origins.Length != 1) errors.Add("Expected one XR origin, found " + origins.Length + ".");
            if (movers.Length != 1) errors.Add("Expected one locomotion driver, found " + movers.Length + ".");
            if (authorities.Length != 1) errors.Add("Expected one shared challenge session authority.");
            if (resets.Length != 1) errors.Add("Expected one vertical slice reset.");
            if (lifecycles.Length != 1) errors.Add("Expected one XR lifecycle authority.");
            if (Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Accepted C01 controller is missing.");
            if (Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Accepted C02 controller is missing.");
            if (errors.Count > 0)
            {
                WriteReport(errors, origins.Length, movers.Length);
                throw new InvalidOperationException(string.Join(" ", errors));
            }

            var existing = SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(item => item.name == RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var accent = LoadMaterial(AccentPath);
            var dark = LoadMaterial(DarkPath);
            var root = new GameObject(RootName);
            AddStableId(root, "challenge.c03");
            CreatePanel(root.transform, dark);
            var title = CreateText(root.transform, "C03_TITLE", new Vector3(PanelCenterX, 2.23f, ContentSurfaceZ),
                "C03 CHECK BEFORE ACTION", 0.072f, 0.036f, TextAnchor.MiddleCenter);
            title.color = new Color(0.86f, 0.96f, 1f, 1f);
            var status = CreateText(root.transform, "C03_STATUS", new Vector3(PanelCenterX, 1.98f, ContentSurfaceZ),
                "LOCKED\nсначала C02 PASS", 0.078f, 0.044f, TextAnchor.MiddleCenter);
            var matrix = CreateText(root.transform, "C03_MATRIX", new Vector3(PanelCenterX - 0.58f, 1.63f, ContentSurfaceZ),
                "AUTH MATRIX\n01 GUEST VIEW  —\n02 GUEST EDIT  —\n03 TEAM EDIT   —",
                0.058f, 0.032f, TextAnchor.UpperLeft);
            var trace = CreateText(root.transform, "C03_TRACE", new Vector3(PanelCenterX + 0.57f, 1.63f, ContentSurfaceZ),
                "C03 EVENT TRACE\n(empty)", 0.054f, 0.030f, TextAnchor.UpperCenter);

            var bypass = CreateButton(root.transform, "C03_BYPASS_BUTTON", "interaction.c03-bypass",
                new Vector3(PanelCenterX - 0.72f, 0.88f, ButtonSurfaceZ), accent, "RUN\nBYPASS");
            var gate = CreateButton(root.transform, "C03_GATE_BUTTON", "interaction.c03-install-gate",
                new Vector3(PanelCenterX - 0.36f, 0.88f, ButtonSurfaceZ), accent, "ADD\nGATE");
            var auth01 = CreateButton(root.transform, "C03_AUTH01_BUTTON", "interaction.c03-auth01",
                new Vector3(PanelCenterX, 0.88f, ButtonSurfaceZ), accent, "AUTH\n01");
            var auth02 = CreateButton(root.transform, "C03_AUTH02_BUTTON", "interaction.c03-auth02",
                new Vector3(PanelCenterX + 0.36f, 0.88f, ButtonSurfaceZ), accent, "AUTH\n02");
            var auth03 = CreateButton(root.transform, "C03_AUTH03_BUTTON", "interaction.c03-auth03",
                new Vector3(PanelCenterX + 0.72f, 0.88f, ButtonSurfaceZ), accent, "AUTH\n03");

            var controller = root.AddComponent<C03ChallengeController>();
            controller.Configure(bypass, gate, auth01, auth02, auth03, authorities[0], lifecycles[0],
                status, matrix, trace);
            resets[0].AddResettableInteractables(bypass, gate, auth01, auth02, auth03);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) errors.Add("Scene save returned false.");
            AssetDatabase.SaveAssets();
            ValidateSavedScene(errors);
            WriteReport(errors, origins.Length, movers.Length);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            Debug.Log("WO-09 C03 scene patch PASS: " + ScenePath);
        }

        private static void ValidateSavedScene(List<string> errors)
        {
            if (Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C03 patch changed XR origin authority.");
            if (Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C03 patch changed locomotion authority.");
            if (Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C03 patch changed session authority.");
            if (Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected one C03 controller.");
            var root = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(item => item.name == RootName);
            if (root == null) errors.Add("C03 root is missing after patch.");
            else if (root.GetComponentsInChildren<Collider>(true).Length != 5)
                errors.Add("Only the five fixed C03 controls may add colliders.");
            ValidatePanelPlacement(root, errors);
        }

        private static void ValidatePanelPlacement(GameObject root, List<string> errors)
        {
            if (root == null) return;
            // Primitive collider bounds lag transform writes in an editor batch until
            // the physics scene is synchronized; validate the saved world placement.
            Physics.SyncTransforms();
            var panel = root.transform.Find("C03_INFORMATION_PANEL")?.GetComponent<Renderer>();
            if (panel == null)
            {
                errors.Add("C03 information panel renderer is missing.");
                return;
            }
            var floors = new[] { "FLOOR_0_2__COLLIDER_FRAME", "FLOOR_0_3__COLLIDER_FRAME" }
                .Select(GameObject.Find).Where(item => item != null)
                .SelectMany(item => item.GetComponentsInChildren<Collider>(true)).ToArray();
            if (floors.Length != 2)
            {
                errors.Add("Accepted 403 floor collider frames are missing.");
                return;
            }
            var floorBounds = floors[0].bounds;
            foreach (var floor in floors.Skip(1)) floorBounds.Encapsulate(floor.bounds);
            if (panel.bounds.min.x <= floorBounds.min.x || panel.bounds.max.x >= floorBounds.max.x ||
                panel.bounds.min.z <= floorBounds.min.z || panel.bounds.max.z >= floorBounds.max.z)
                errors.Add("C03 panel must remain strictly inside the accepted 403 floor footprint.");

            var backWall = GameObject.Find("WOOD_WALL_403__COLLIDER_FRAME")?
                .GetComponentInChildren<Collider>(true);
            if (backWall == null) errors.Add("Accepted 403 wood-wall collider frame is missing.");
            else if (panel.bounds.min.z <= backWall.bounds.max.z)
                errors.Add("C03 panel must remain on the walkable side of the 403 wood wall.");

            var controls = root.GetComponentsInChildren<DemoPressInteractable>(true)
                .Select(item => item.GetComponent<Collider>()).Where(item => item != null).ToArray();
            if (controls.Length != 5)
            {
                errors.Add("Expected five C03 control colliders for placement validation.");
                return;
            }
            var walls = new[]
                {
                    "WALL_0__COLLIDER_FRAME", "WALL_1__COLLIDER_FRAME", "WALL_2__COLLIDER_FRAME",
                    "WALL_3__COLLIDER_FRAME", "WOOD_WALL_403__COLLIDER_FRAME",
                }
                .Select(GameObject.Find).Where(item => item != null)
                .SelectMany(item => item.GetComponentsInChildren<Collider>(true)).ToArray();
            foreach (var control in controls)
            {
                if (control.bounds.min.x <= floorBounds.min.x || control.bounds.max.x >= floorBounds.max.x ||
                    control.bounds.min.z <= floorBounds.min.z || control.bounds.max.z >= floorBounds.max.z)
                    errors.Add(control.name + " must remain strictly inside the accepted 403 floor footprint: " +
                               $"control={control.bounds.min}..{control.bounds.max} " +
                               $"floor={floorBounds.min}..{floorBounds.max}.");
                if (walls.Any(wall => control.bounds.Intersects(wall.bounds)))
                    errors.Add(control.name + " intersects an accepted 403 wall collider.");
            }
        }

        private static void WriteReport(List<string> errors, int origins, int movers)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var path = Path.Combine(workspace, "reports", "local", "WO-09", "c03_scene_patch.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? workspace);
            var root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(item => item.name == RootName);
            var report = new PatchReport
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                observed_utc = DateTime.UtcNow.ToString("O"),
                scene = ScenePath,
                xr_origins = origins,
                locomotion_drivers = movers,
                session_authorities = Object.FindObjectsByType<ChallengeSessionAuthority>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c01_controllers = Object.FindObjectsByType<C01ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c02_controllers = Object.FindObjectsByType<C02ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c03_controllers = Object.FindObjectsByType<C03ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c03_buttons = root == null ? 0 : root.GetComponentsInChildren<DemoPressInteractable>(true).Length,
                total_interactables = Object.FindObjectsByType<DemoPressInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                errors = errors.ToArray(),
            };
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static void CreatePanel(Transform parent, Material material)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "C03_INFORMATION_PANEL";
            panel.transform.SetParent(parent, true);
            panel.transform.SetPositionAndRotation(new Vector3(PanelCenterX, 1.51f, PanelSurfaceZ), Quaternion.identity);
            panel.transform.localScale = new Vector3(1.80f, 1.70f, 0.035f);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static DemoPressInteractable CreateButton(Transform parent, string name, string stableId,
            Vector3 position, Material material, string text)
        {
            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = name;
            button.transform.SetParent(parent, true);
            button.transform.SetPositionAndRotation(position, Quaternion.identity);
            button.transform.localScale = new Vector3(0.29f, 0.18f, 0.12f);
            AddStableId(button, stableId);
            var renderer = button.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            var interactable = button.AddComponent<DemoPressInteractable>();
            interactable.Configure(renderer);
            CreateText(button.transform, name + "_LABEL", position + new Vector3(0f, 0f, 0.07f), text,
                0.062f, 0.032f, TextAnchor.MiddleCenter);
            return interactable;
        }

        private static TextMesh CreateText(Transform parent, string name, Vector3 position, string text,
            float characterSize, float worldScale, TextAnchor anchor)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, true);
            item.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
            var parentScale = parent.lossyScale;
            item.transform.localScale = new Vector3(
                worldScale / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
                worldScale / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
                worldScale / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f));
            var mesh = item.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.characterSize = characterSize;
            mesh.fontSize = 64;
            mesh.anchor = anchor;
            mesh.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
            mesh.color = Color.white;
            mesh.richText = false;
            return mesh;
        }

        private static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new FileNotFoundException("Required material is missing", path);
            return material;
        }

        private static void AddStableId(GameObject item, string value)
        {
            var stable = item.GetComponent<StableId>() ?? item.AddComponent<StableId>();
            stable.Set(value);
        }
    }
}
