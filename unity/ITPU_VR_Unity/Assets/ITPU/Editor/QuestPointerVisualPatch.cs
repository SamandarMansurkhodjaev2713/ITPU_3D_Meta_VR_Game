using System;
using System.Collections.Generic;
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
    public static class QuestPointerVisualPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string AimMaterialPath = "Assets/ITPU/Art/Materials/ITPU_xr_aim_unlit.mat";

        [Serializable]
        private sealed class PatchReport
        {
            public int schema_version = 1;
            public string work_order = "WO-23";
            public string status;
            public string observed_utc;
            public string unity_version;
            public string scene;
            public string scene_sha256_before;
            public string scene_sha256_after;
            public string accepted_blend_sha256;
            public string material;
            public int xr_origins;
            public int character_controllers;
            public int locomotion_drivers;
            public int hand_pointers;
            public int controller_pointers;
            public int pointer_visuals;
            public int line_renderers;
            public int hit_markers;
            public int visual_colliders;
            public float controller_max_distance_m;
            public float optical_hand_max_distance_m;
            public bool all_pointers_assigned;
            public bool one_shared_unlit_material;
            public string controller_policy = "Controller data source activates only the controller pointer; controller tracking is fallback only when source is unknown";
            public string optical_policy = "Unobstructed data source activates only the optical Meta aim-pose pointer";
            public string[] errors;
            public string publication = "LOCAL_ONLY_PRIVATE / NOT_SENT";
        }

        [MenuItem("ITPU/Apply Quest Pointer Visual Patch")]
        public static void ApplyBatch()
        {
            var errors = new List<string>();
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var sceneFile = Path.Combine(projectRoot, ScenePath);
            var reportDirectory = Path.Combine(workspace, "reports", "local", "WO-23");
            var reportPath = Path.Combine(reportDirectory, "quest_pointer_visual_patch.json");
            var acceptedBlend = Path.Combine(workspace, "art_pipeline", "environment_v6", "blender",
                "ITPU_environment_v6_lenovo32_004.blend");
            Directory.CreateDirectory(reportDirectory);

            var report = new PatchReport
            {
                status = "IN_PROGRESS",
                observed_utc = DateTime.UtcNow.ToString("O"),
                unity_version = Application.unityVersion,
                scene = ScenePath,
                scene_sha256_before = HashFile(sceneFile),
                accepted_blend_sha256 = HashFile(acceptedBlend),
                material = AimMaterialPath,
                errors = Array.Empty<string>(),
            };

            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var material = EnsureAimMaterial();
                var hands = Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                var controllers = Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                if (hands.Length != 2) errors.Add($"Expected two hand pointers; found {hands.Length}.");
                if (controllers.Length != 2) errors.Add($"Expected two controller pointers; found {controllers.Length}.");
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));

                foreach (var hand in hands)
                    hand.ConfigureVisual(InstallVisual(hand.gameObject, material));
                foreach (var controller in controllers)
                {
                    var serialized = new SerializedObject(controller);
                    serialized.FindProperty("maxDistance").floatValue = 6f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    controller.ConfigureVisual(InstallVisual(controller.gameObject, material));
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    errors.Add("Scene save returned false.");
                AssetDatabase.SaveAssets();

                var visuals = Object.FindObjectsByType<XrPointerVisual>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                report.xr_origins = Count<XROrigin>();
                report.character_controllers = Count<CharacterController>();
                report.locomotion_drivers = Count<XrLocomotionDriver>();
                report.hand_pointers = hands.Length;
                report.controller_pointers = controllers.Length;
                report.pointer_visuals = visuals.Length;
                report.line_renderers = visuals.Count(item => item.Line != null);
                report.hit_markers = visuals.Count(item => item.HitMarker != null &&
                                                          item.HitMarkerRenderer != null);
                report.visual_colliders = visuals.Sum(item =>
                    item.GetComponentsInChildren<Collider>(true).Length);
                report.controller_max_distance_m = controllers.Max(item => item.MaxDistance);
                report.optical_hand_max_distance_m = hands.Max(item => item.MaxDistance);
                report.all_pointers_assigned = hands.All(item => item.PointerVisual != null) &&
                                               controllers.All(item => item.PointerVisual != null);
                report.one_shared_unlit_material = visuals.All(item =>
                    item.Line != null && item.Line.sharedMaterial == material &&
                    item.HitMarkerRenderer != null && item.HitMarkerRenderer.sharedMaterial == material);

                if (report.xr_origins != 1) errors.Add("Expected exactly one XROrigin.");
                if (report.character_controllers != 1) errors.Add("Expected exactly one CharacterController.");
                if (report.locomotion_drivers != 1) errors.Add("Expected exactly one locomotion driver.");
                if (report.pointer_visuals != 4) errors.Add("Expected four pointer visual components.");
                if (report.line_renderers != 4) errors.Add("Expected four authored aim lines.");
                if (report.hit_markers != 4) errors.Add("Expected four authored hit markers.");
                if (report.visual_colliders != 0) errors.Add("Pointer visuals must not add colliders.");
                if (report.controller_max_distance_m > 6f || report.optical_hand_max_distance_m > 6f)
                    errors.Add("Pointer distance must not exceed the six-metre teleport semantic range.");
                if (!report.all_pointers_assigned) errors.Add("Every pointer must reference its visual.");
                if (!report.one_shared_unlit_material)
                    errors.Add("Every aim line/marker must use the one shared unlit material.");

                report.scene_sha256_after = HashFile(sceneFile);
                report.errors = errors.ToArray();
                report.status = errors.Count == 0 ? "PASS" : "FAIL";
                Write(reportPath, report);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                Debug.Log("ITPU_QUEST_POINTER_VISUAL_PATCH=" + JsonUtility.ToJson(report));
            }
            catch (Exception exception)
            {
                report.observed_utc = DateTime.UtcNow.ToString("O");
                report.scene_sha256_after = HashFile(sceneFile);
                report.errors = errors.Concat(new[] { exception.GetType().Name + ": " + exception.Message })
                    .Distinct().ToArray();
                report.status = "FAIL";
                Write(reportPath, report);
                throw;
            }
        }

        private static XrPointerVisual InstallVisual(GameObject root, Material material)
        {
            RemoveChild(root.transform, "Aim Line");
            RemoveChild(root.transform, "Aim Hit");

            var lineObject = new GameObject("Aim Line");
            lineObject.transform.SetParent(root.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.009f;
            line.endWidth = 0.004f;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.enabled = false;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Aim Hit";
            marker.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            var markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = material;
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;

            var visual = root.GetComponent<XrPointerVisual>() ?? root.AddComponent<XrPointerVisual>();
            visual.Configure(line, marker.transform, markerRenderer);
            return visual;
        }

        private static void RemoveChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        private static Material EnsureAimMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(AimMaterialPath);
            if (existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            var material = new Material(shader) { name = "ITPU_xr_aim_unlit", enableInstancing = true };
            material.SetColor("_BaseColor", new Color(0.22f, 0.82f, 1f, 1f));
            AssetDatabase.CreateAsset(material, AimMaterialPath);
            return material;
        }

        private static int Count<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

        private static string HashFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void Write(string path, PatchReport report) =>
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + Environment.NewLine);
    }
}
