using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class QuestComfortLightingPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string TurnModeId = "interaction.turn-comfort-mode";

        [Serializable]
        private sealed class PatchReport
        {
            public string status;
            public string observed_utc;
            public string scene;
            public string scene_sha256_before;
            public string scene_sha256_after;
            public string accepted_blend_sha256;
            public int xr_origins;
            public int character_controllers;
            public int locomotion_drivers;
            public int interactables;
            public int turn_mode_controls;
            public int directional_lights;
            public int realtime_point_or_spot_lights;
            public string default_turn_mode;
            public float key_intensity;
            public float key_shadow_strength;
            public string ambient_mode;
            public float ambient_intensity;
            public Color ambient_sky;
            public Color ambient_equator;
            public Color ambient_ground;
            public string control_render;
            public string control_render_sha256;
            public string[] errors;
        }

        [MenuItem("ITPU/Apply Quest Comfort And Lighting Patch")]
        public static void ApplyBatch()
        {
            var errors = Array.Empty<string>().ToList();
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var sceneFile = Path.Combine(projectRoot, ScenePath);
            var reportDirectory = Path.Combine(workspace, "reports", "local", "WO-23");
            var reportPath = Path.Combine(reportDirectory, "quest_comfort_lighting_patch.json");
            var renderPath = Path.Combine(reportDirectory, "code30_lighting_control.png");
            var acceptedBlend = Path.Combine(workspace, "art_pipeline", "environment_v6", "blender",
                "ITPU_environment_v6_lenovo32_004.blend");
            Directory.CreateDirectory(reportDirectory);

            var report = new PatchReport
            {
                status = "IN_PROGRESS",
                observed_utc = DateTime.UtcNow.ToString("O"),
                scene = ScenePath,
                scene_sha256_before = HashFile(sceneFile),
                accepted_blend_sha256 = HashFile(acceptedBlend),
                control_render = Path.GetRelativePath(workspace, renderPath).Replace('\\', '/'),
                errors = Array.Empty<string>(),
            };

            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var locomotion = Single<XrLocomotionDriver>(errors, "locomotion driver");
                var reset = Single<VerticalSliceReset>(errors, "vertical-slice reset");
                var teleport = Single<HandTeleportIntentAdapter>(errors, "teleport adapter");
                var priority = Single<InteractionPriority>(errors, "interaction priority");
                var startup = Single<XrStartupPoseAligner>(errors, "startup pose aligner");
                var demo = FindInteractable("interaction.demo-route-button", errors);
                var resetButton = FindInteractable("interaction.session-reset", errors);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));

                var demoRenderer = demo.GetComponent<Renderer>();
                EnsureLabel(demo.transform, "ACTIVATE");
                EnsureLabel(resetButton.transform, "RESET");
                var turnButton = FindInteractable(TurnModeId, null) ?? CreateTurnModeButton(demoRenderer?.sharedMaterial);
                turnButton.transform.SetPositionAndRotation(new Vector3(-3.84f, 1.25f, -12.05f),
                    Quaternion.identity);
                turnButton.transform.localScale = new Vector3(0.28f, 0.18f, 0.12f);
                var turnLabel = EnsureLabel(turnButton.transform, "TURN\nSNAP 30");
                var turnControl = turnButton.GetComponent<LocomotionTurnModeControl>() ??
                                  turnButton.gameObject.AddComponent<LocomotionTurnModeControl>();
                turnControl.Configure(turnButton, locomotion, turnLabel);
                turnControl.ResetToDefault();

                reset.Configure(locomotion, teleport, priority,
                    new[] { demo, resetButton, turnButton },
                    Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                        FindObjectsSortMode.None),
                    Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                        FindObjectsSortMode.None),
                    startup, turnControl);

                ApplyLighting(errors, out var key);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    errors.Add("Scene save returned false.");
                AssetDatabase.SaveAssets();

                report.xr_origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length;
                report.character_controllers = Object.FindObjectsByType<CharacterController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                report.locomotion_drivers = Object.FindObjectsByType<XrLocomotionDriver>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                report.interactables = Object.FindObjectsByType<DemoPressInteractable>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                report.turn_mode_controls = Object.FindObjectsByType<LocomotionTurnModeControl>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.enabled && item.gameObject.activeInHierarchy).ToArray();
                report.directional_lights = lights.Count(item => item.type == LightType.Directional);
                report.realtime_point_or_spot_lights = lights.Count(item =>
                    item.type == LightType.Point || item.type == LightType.Spot);
                report.default_turn_mode = locomotion.SmoothTurnEnabled ? "INVALID_SMOOTH" : "SNAP_30";
                report.key_intensity = key.intensity;
                report.key_shadow_strength = key.shadowStrength;
                report.ambient_mode = RenderSettings.ambientMode.ToString();
                report.ambient_intensity = RenderSettings.ambientIntensity;
                report.ambient_sky = RenderSettings.ambientSkyColor;
                report.ambient_equator = RenderSettings.ambientEquatorColor;
                report.ambient_ground = RenderSettings.ambientGroundColor;

                if (report.xr_origins != 1) errors.Add("Expected exactly one XROrigin.");
                if (report.character_controllers != 1) errors.Add("Expected exactly one CharacterController.");
                if (report.locomotion_drivers != 1) errors.Add("Expected exactly one locomotion driver.");
                if (report.interactables != 3) errors.Add("Expected exactly three interactables.");
                if (report.turn_mode_controls != 1) errors.Add("Expected exactly one turn-mode control.");
                if (report.directional_lights != 1) errors.Add("Expected exactly one enabled directional light.");
                if (report.realtime_point_or_spot_lights != 0)
                    errors.Add("Realtime point/spot lights are outside this bounded Quest profile.");
                if (locomotion.SmoothTurnEnabled) errors.Add("Saved default turn mode must remain snap 30.");

                var camera = Single<Camera>(errors, "XR camera");
                if (errors.Count == 0) Capture(camera, renderPath);
                report.control_render_sha256 = HashFile(renderPath);
                report.scene_sha256_after = HashFile(sceneFile);
                report.errors = errors.ToArray();
                report.status = errors.Count == 0 ? "PASS" : "FAIL";
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + Environment.NewLine);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                Debug.Log("ITPU_QUEST_COMFORT_LIGHTING_PATCH=" + JsonUtility.ToJson(report));
            }
            catch (Exception exception)
            {
                report.observed_utc = DateTime.UtcNow.ToString("O");
                report.scene_sha256_after = HashFile(sceneFile);
                report.errors = errors.Concat(new[] { exception.GetType().Name + ": " + exception.Message })
                    .Distinct().ToArray();
                report.status = "FAIL";
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + Environment.NewLine);
                throw;
            }
        }

        private static T Single<T>(System.Collections.Generic.ICollection<string> errors, string label)
            where T : Component
        {
            var values = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (values.Length == 1) return values[0];
            errors.Add($"Expected exactly one {label}; found {values.Length}.");
            return values.FirstOrDefault();
        }

        private static DemoPressInteractable FindInteractable(string stableId,
            System.Collections.Generic.ICollection<string> errors)
        {
            var stable = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).FirstOrDefault(item => item.Value == stableId);
            var interactable = stable == null ? null : stable.GetComponent<DemoPressInteractable>();
            if (interactable == null && errors != null) errors.Add("Missing interactable " + stableId + ".");
            return interactable;
        }

        private static DemoPressInteractable CreateTurnModeButton(Material material)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "TURN_MODE_BUTTON";
            root.transform.SetPositionAndRotation(new Vector3(-3.84f, 1.25f, -12.05f), Quaternion.identity);
            root.transform.localScale = new Vector3(0.28f, 0.18f, 0.12f);
            root.AddComponent<StableId>().Set(TurnModeId);
            var renderer = root.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            var interactable = root.AddComponent<DemoPressInteractable>();
            interactable.Configure(renderer);
            return interactable;
        }

        private static TextMesh EnsureLabel(Transform parent, string text)
        {
            var mesh = parent.GetComponentInChildren<TextMesh>(true);
            if (mesh == null)
            {
                var label = new GameObject("Control Label");
                label.transform.SetParent(parent, false);
                mesh = label.AddComponent<TextMesh>();
            }
            mesh.gameObject.name = "Control Label";
            mesh.transform.localPosition = new Vector3(0f, 0f, 0.56f);
            mesh.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            mesh.transform.localScale = new Vector3(
                0.05f / Mathf.Max(0.001f, Mathf.Abs(parent.lossyScale.x)),
                0.05f / Mathf.Max(0.001f, Mathf.Abs(parent.lossyScale.y)),
                0.05f / Mathf.Max(0.001f, Mathf.Abs(parent.lossyScale.z)));
            mesh.text = text;
            mesh.alignment = TextAlignment.Center;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.characterSize = 0.08f;
            mesh.fontSize = 64;
            mesh.color = Color.white;
            return mesh;
        }

        private static void ApplyLighting(System.Collections.Generic.ICollection<string> errors, out Light key)
        {
            var directional = Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.type == LightType.Directional).ToArray();
            if (directional.Length != 1)
            {
                errors.Add($"Expected one directional light before patch; found {directional.Length}.");
                key = directional.FirstOrDefault();
                return;
            }
            key = directional[0];
            key.intensity = 0.88f;
            key.shadows = LightShadows.None;
            key.shadowStrength = 0f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.84f, 0.86f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.66f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.48f, 0.44f, 0.38f);
            RenderSettings.ambientIntensity = 1.20f;
        }

        private static void Capture(Camera camera, string path)
        {
            const int width = 1280;
            const int height = 720;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
