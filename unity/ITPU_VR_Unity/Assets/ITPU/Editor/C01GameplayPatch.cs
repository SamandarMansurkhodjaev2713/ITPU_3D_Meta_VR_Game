using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ITPU.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class C01GameplayPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string RootName = "C01_CHALLENGE_ROOT";
        private const string AccentPath = "Assets/ITPU/Art/Materials/ITPU_accent.mat";
        private const string DarkPath = "Assets/ITPU/Art/Materials/ITPU_dark.mat";
        private const string WarmWhitePath = "Assets/ITPU/Art/Materials/ITPU_warm_white.mat";
        private const string AimPath = "Assets/ITPU/Art/Materials/ITPU_xr_aim_unlit.mat";

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string observed_utc;
            public string scene;
            public string mode = "APPROXIMATE_SCHEMATIC_NOT_MEASURED_ROUTE_MAP";
            public int xr_origins;
            public int locomotion_drivers;
            public int challenge_controllers;
            public int session_authorities;
            public int route_presenters;
            public int route_lines;
            public int c01_buttons;
            public int total_interactables;
            public string[] stable_ids;
            public string[] errors;
        }

        [MenuItem("ITPU/Apply WO-08 C01 Gameplay")]
        public static void Apply()
        {
            var errors = new List<string>();
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("Saved XR scene is missing", ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var movers = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (origins.Length != 1) errors.Add("Expected one XR origin, found " + origins.Length + ".");
            if (movers.Length != 1) errors.Add("Expected one locomotion driver, found " + movers.Length + ".");

            var existing = SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(item => item.name == RootName);
            if (existing != null) Object.DestroyImmediate(existing);

            var routeButton = FindInteractable("interaction.demo-route-button", errors);
            var reset = Object.FindObjectsByType<VerticalSliceReset>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).SingleOrDefault();
            if (reset == null) errors.Add("VerticalSliceReset is missing.");
            if (routeButton == null || reset == null || errors.Count > 0)
            {
                WriteReport(errors, origins.Length, movers.Length);
                throw new InvalidOperationException(string.Join(" ", errors));
            }
            routeButton.gameObject.name = "C01_SHOW_ROUTE_BUTTON";
            var oldLabel = routeButton.GetComponentInChildren<TextMesh>(true);
            if (oldLabel == null) errors.Add("The accepted demo route button has no label.");
            else oldLabel.text = "SHOW ROUTE";

            var accent = LoadMaterial(AccentPath);
            var dark = LoadMaterial(DarkPath);
            var warmWhite = LoadMaterial(WarmWhitePath);
            var aim = LoadMaterial(AimPath);

            var root = new GameObject(RootName);
            AddStableId(root, "challenge.c01");
            var linkButton = CreateButton(root.transform, "C01_LINK_HANDLER_BUTTON",
                "interaction.c01-link-handler", new Vector3(-2.75f, 1.25f, -12.05f),
                new Vector3(0.32f, 0.18f, 0.12f), accent, "LINK\nHANDLER");
            var destinationButton = CreateButton(root.transform, "C01_DESTINATION_BUTTON",
                "interaction.c01-destination-select", new Vector3(-2.35f, 1.25f, -12.05f),
                new Vector3(0.32f, 0.18f, 0.12f), accent, "TARGET\nKITCHEN");

            CreatePanel(root.transform, dark);
            var status = CreateText(root.transform, "C01_STATUS", new Vector3(-3.08f, 1.76f, -12.11f),
                "1. SHOW ROUTE\nбез handler", 0.10f, 0.06f, TextAnchor.MiddleCenter);
            var trace = CreateText(root.transform, "C01_TRACE", new Vector3(-3.08f, 0.93f, -12.11f),
                "EVENT TRACE\n(empty)", 0.075f, 0.05f, TextAnchor.UpperCenter);
            var mapTitle = CreateText(root.transform, "C01_MAP_TITLE", new Vector3(-1.72f, 2.08f, -12.11f),
                "C01 APPROX ROUTE MAP", 0.08f, 0.05f, TextAnchor.MiddleCenter);
            mapTitle.color = new Color(0.86f, 0.96f, 1f, 1f);

            var presenterObject = new GameObject("C01_ROUTE_PRESENTER");
            presenterObject.transform.SetParent(root.transform, false);
            var routeLine = presenterObject.AddComponent<LineRenderer>();
            routeLine.useWorldSpace = true;
            routeLine.widthMultiplier = 0.022f;
            routeLine.numCapVertices = 4;
            routeLine.numCornerVertices = 3;
            routeLine.sharedMaterial = aim;
            routeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            routeLine.receiveShadows = false;
            routeLine.enabled = false;
            var presenter = presenterObject.AddComponent<C01RoutePresenter>();
            var bindings = CreateWaypoints(root.transform, warmWhite).ToArray();
            presenter.Configure(routeLine, bindings);

            var controller = root.AddComponent<C01ChallengeController>();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var authority = EnsureSessionAuthority(reset, lifecycle, errors);
            controller.Configure(routeButton, linkButton, destinationButton, authority, presenter,
                status, destinationButton.GetComponentInChildren<TextMesh>(true), trace);
            reset.AddResettableInteractables(linkButton, destinationButton);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) errors.Add("Scene save returned false.");
            AssetDatabase.SaveAssets();

            ValidateSavedScene(errors);
            WriteReport(errors, origins.Length, movers.Length);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            Debug.Log("WO-08 C01 scene patch PASS: " + ScenePath);
        }

        private static IEnumerable<C01WaypointBinding> CreateWaypoints(Transform parent, Material material)
        {
            var points = new[]
            {
                ("WORK_403", new Vector3(-2.12f, 1.42f, -12.10f)),
                ("HALL_LEFT", new Vector3(-1.92f, 1.42f, -12.10f)),
                ("RECEPTION_FRONT", new Vector3(-1.72f, 1.42f, -12.10f)),
                ("HALL_TOP", new Vector3(-1.52f, 1.42f, -12.10f)),
                ("NICHE_413", new Vector3(-1.32f, 1.67f, -12.10f)),
                ("HALL_RIGHT", new Vector3(-1.32f, 1.22f, -12.10f)),
                ("KITCHEN", new Vector3(-1.12f, 1.22f, -12.10f)),
            };
            foreach (var point in points)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "C01_WAYPOINT_" + point.Item1;
                marker.transform.SetParent(parent, true);
                marker.transform.position = point.Item2;
                marker.transform.localScale = Vector3.one * 0.055f;
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.GetComponent<Renderer>().sharedMaterial = material;
                marker.SetActive(false);
                yield return new C01WaypointBinding(point.Item1, marker.transform);
            }
        }

        private static void CreatePanel(Transform parent, Material material)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "C01_INFORMATION_PANEL";
            panel.transform.SetParent(parent, true);
            panel.transform.SetPositionAndRotation(new Vector3(-2.35f, 1.47f, -12.16f), Quaternion.identity);
            panel.transform.localScale = new Vector3(2.75f, 1.45f, 0.035f);
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
                0.08f, 0.05f, TextAnchor.MiddleCenter);
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
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color.white;
            mesh.richText = false;
            mesh.tabSize = 4;
            return mesh;
        }

        private static DemoPressInteractable FindInteractable(string stableId, List<string> errors)
        {
            var stable = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).SingleOrDefault(item => item.Value == stableId);
            var interactable = stable == null ? null : stable.GetComponent<DemoPressInteractable>();
            if (interactable == null) errors.Add("Missing interactable " + stableId + ".");
            return interactable;
        }

        private static ChallengeSessionAuthority EnsureSessionAuthority(VerticalSliceReset reset,
            XrLifecycleState lifecycle, List<string> errors)
        {
            var authorities = Object.FindObjectsByType<ChallengeSessionAuthority>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (authorities.Length > 1)
            {
                errors.Add("Expected at most one challenge session authority.");
                return authorities[0];
            }
            var authority = authorities.SingleOrDefault();
            if (authority == null)
            {
                var item = new GameObject("CHALLENGE_SESSION_AUTHORITY");
                AddStableId(item, "system.challenge-session");
                authority = item.AddComponent<ChallengeSessionAuthority>();
            }
            authority.Configure(reset, lifecycle);
            return authority;
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

        private static void ValidateSavedScene(List<string> errors)
        {
            if (Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C01 patch changed XR origin authority.");
            if (Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("C01 patch changed locomotion authority.");
            if (Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected one C01 challenge controller.");
            if (Object.FindObjectsByType<C01RoutePresenter>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected one C01 route presenter.");
            if (Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length != 1) errors.Add("Expected one challenge session authority.");
            var c01 = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(item => item.name == RootName);
            if (c01 == null) errors.Add("C01 root is missing after patch.");
            else if (c01.GetComponentsInChildren<Collider>(true).Length != 2)
                errors.Add("Only the two C01 controls may add colliders.");
        }

        private static void WriteReport(List<string> errors, int origins, int movers)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var path = Path.Combine(workspace, "reports", "local", "WO-08", "c01_scene_patch.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? workspace);
            var c01 = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(item => item.name == RootName);
            var report = new PatchReport
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                observed_utc = DateTime.UtcNow.ToString("O"),
                scene = ScenePath,
                xr_origins = origins,
                locomotion_drivers = movers,
                challenge_controllers = Object.FindObjectsByType<C01ChallengeController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                session_authorities = Object.FindObjectsByType<ChallengeSessionAuthority>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                route_presenters = Object.FindObjectsByType<C01RoutePresenter>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                route_lines = c01 == null ? 0 : c01.GetComponentsInChildren<LineRenderer>(true).Length,
                c01_buttons = c01 == null ? 0 : c01.GetComponentsInChildren<DemoPressInteractable>(true).Length,
                total_interactables = Object.FindObjectsByType<DemoPressInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                stable_ids = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Select(item => item.Value).OrderBy(item => item).ToArray(),
                errors = errors.ToArray(),
            };
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + Environment.NewLine);
        }
    }
}
