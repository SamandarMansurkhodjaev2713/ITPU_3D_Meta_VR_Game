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
    public static class C02GameplayPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string RootName = "C02_CHALLENGE_ROOT";
        private const string AccentPath = "Assets/ITPU/Art/Materials/ITPU_accent.mat";
        private const string DarkPath = "Assets/ITPU/Art/Materials/ITPU_dark.mat";
        private const string WarmWhitePath = "Assets/ITPU/Art/Materials/ITPU_warm_white.mat";

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string observed_utc;
            public string scene;
            public string mode = "C02_SCHEMATIC_BEHAVIOUR_NOT_WORLD_ROUTE";
            public int xr_origins;
            public int locomotion_drivers;
            public int session_authorities;
            public int c01_controllers;
            public int c02_controllers;
            public int c02_presenters;
            public int c02_buttons;
            public int total_interactables;
            public string[] errors;
        }

        [MenuItem("ITPU/Apply WO-09 C02 Gameplay")]
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
            var warmWhite = LoadMaterial(WarmWhitePath);
            var root = new GameObject(RootName);
            AddStableId(root, "challenge.c02");

            CreatePanel(root.transform, dark);
            var title = CreateText(root.transform, "C02_TITLE", new Vector3(-0.05f, 2.08f, -12.11f),
                "C02 WAIT FOR GUEST — SCHEMATIC", 0.075f, 0.038f, TextAnchor.MiddleCenter);
            title.color = new Color(0.86f, 0.96f, 1f, 1f);
            var status = CreateText(root.transform, "C02_STATUS", new Vector3(-0.05f, 1.84f, -12.11f),
                "LOCKED\nсначала C01 PASS", 0.09f, 0.052f, TextAnchor.MiddleCenter);
            var trace = CreateText(root.transform, "C02_TRACE", new Vector3(-0.05f, 0.91f, -12.11f),
                "C02 EVENT TRACE\n(empty)", 0.07f, 0.038f, TextAnchor.UpperCenter);

            var step = CreateButton(root.transform, "C02_STEP_BUTTON", "interaction.c02-step",
                new Vector3(-0.60f, 1.22f, -12.05f), new Vector3(0.42f, 0.18f, 0.12f), accent, "STEP");
            var addWait = CreateButton(root.transform, "C02_ADD_WAIT_BUTTON", "interaction.c02-add-wait",
                new Vector3(-0.05f, 1.22f, -12.05f), new Vector3(0.42f, 0.18f, 0.12f), accent,
                "ADD WAIT");
            var guest = CreateButton(root.transform, "C02_GUEST_BUTTON", "interaction.c02-guest-arrived",
                new Vector3(0.50f, 1.22f, -12.05f), new Vector3(0.42f, 0.18f, 0.12f), accent,
                "GUEST HERE");

            var presenterObject = new GameObject("C02_GUIDE_PRESENTER");
            presenterObject.transform.SetParent(root.transform, false);
            var start = CreateAnchor(presenterObject.transform, "C02_START_ANCHOR",
                new Vector3(-0.48f, 1.54f, -12.08f));
            var next = CreateAnchor(presenterObject.transform, "C02_NEXT_ANCHOR",
                new Vector3(0.42f, 1.54f, -12.08f));
            var guideMarker = CreateMarker(presenterObject.transform, "C02_GUIDE_MARKER", start.position,
                Vector3.one * 0.11f, accent);
            var guestMarker = CreateMarker(presenterObject.transform, "C02_GUEST_MARKER",
                start.position + new Vector3(0f, -0.16f, 0f), Vector3.one * 0.075f, warmWhite);
            var presenter = presenterObject.AddComponent<C02GuidePresenter>();
            presenter.Configure(guideMarker, guestMarker, start, next);

            var controller = root.AddComponent<C02ChallengeController>();
            controller.Configure(step, addWait, guest, authorities[0], lifecycles[0], presenter, status, trace);
            resets[0].AddResettableInteractables(step, addWait, guest);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) errors.Add("Scene save returned false.");
            AssetDatabase.SaveAssets();
            ValidateSavedScene(errors);
            WriteReport(errors, origins.Length, movers.Length);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            Debug.Log("WO-09 C02 scene patch PASS: " + ScenePath);
        }

        private static void ValidateSavedScene(List<string> errors)
        {
            if (Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C02 patch changed XR origin authority.");
            if (Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C02 patch changed locomotion authority.");
            if (Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C02 patch changed session authority.");
            if (Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected one C02 controller.");
            if (Object.FindObjectsByType<C02GuidePresenter>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected one C02 presenter.");
            var root = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(item => item.name == RootName);
            if (root == null) errors.Add("C02 root is missing after patch.");
            else if (root.GetComponentsInChildren<Collider>(true).Length != 3)
                errors.Add("Only the three C02 controls may add colliders.");
        }

        private static void WriteReport(List<string> errors, int origins, int movers)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var path = Path.Combine(workspace, "reports", "local", "WO-09", "c02_scene_patch.json");
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
                c02_presenters = Object.FindObjectsByType<C02GuidePresenter>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                c02_buttons = root == null ? 0 : root.GetComponentsInChildren<DemoPressInteractable>(true).Length,
                total_interactables = Object.FindObjectsByType<DemoPressInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                errors = errors.ToArray(),
            };
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private static void CreatePanel(Transform parent, Material material)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "C02_INFORMATION_PANEL";
            panel.transform.SetParent(parent, true);
            panel.transform.SetPositionAndRotation(new Vector3(-0.05f, 1.47f, -12.16f), Quaternion.identity);
            panel.transform.localScale = new Vector3(1.70f, 1.45f, 0.035f);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static DemoPressInteractable CreateButton(Transform parent, string name, string stableId,
            Vector3 position, Vector3 scale, Material material, string text)
        {
            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = name;
            button.transform.SetParent(parent, true);
            button.transform.SetPositionAndRotation(position, Quaternion.identity);
            button.transform.localScale = scale;
            AddStableId(button, stableId);
            var renderer = button.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            var interactable = button.AddComponent<DemoPressInteractable>();
            interactable.Configure(renderer);
            CreateText(button.transform, name + "_LABEL", position + new Vector3(0f, 0f, 0.07f), text,
                0.075f, 0.04f, TextAnchor.MiddleCenter);
            return interactable;
        }

        private static Transform CreateAnchor(Transform parent, string name, Vector3 position)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, true);
            item.transform.position = position;
            return item.transform;
        }

        private static Transform CreateMarker(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(parent, true);
            marker.transform.position = position;
            marker.transform.localScale = scale;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = material;
            return marker.transform;
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
            mesh.alignment = TextAlignment.Center;
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
