using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class QuestHandsVisualPatch
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string LeftPrefabPath =
            "Assets/Samples/XR Hands/1.9.0/HandVisualizer/Prefabs/Left Hand Tracking.prefab";
        private const string RightPrefabPath =
            "Assets/Samples/XR Hands/1.9.0/HandVisualizer/Prefabs/Right Hand Tracking.prefab";
        private const string DataSourceFeatureType =
            "UnityEngine.XR.Hands.OpenXR.HandTrackingDataSourceFeature";
        private const string MotionRangeFeatureType =
            "UnityEngine.XR.Hands.OpenXR.HandJointsMotionRangeFeature";

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
            public string xr_hands_package = "1.9.0";
            public string left_prefab;
            public string right_prefab;
            public int xr_origins;
            public int character_controllers;
            public int locomotion_drivers;
            public int hand_pointers;
            public int controller_pointers;
            public int hand_visual_roots;
            public int hand_mesh_controllers;
            public int hand_skeleton_drivers;
            public int hand_tracking_events;
            public int skinned_mesh_renderers;
            public int visual_colliders;
            public int telemetry_components;
            public bool hand_tracking_data_source_enabled;
            public string data_source_preference;
            public bool hand_joints_motion_range_enabled;
            public string motion_range;
            public string[] errors;
            public string publication = "LOCAL_ONLY_PRIVATE / NOT_SENT";
        }

        [MenuItem("ITPU/Apply Quest Hands Visual Patch")]
        public static void ApplyBatch()
        {
            var errors = new List<string>();
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var sceneFile = Path.Combine(projectRoot, ScenePath);
            var reportDirectory = Path.Combine(workspace, "reports", "local", "WO-23");
            var reportPath = Path.Combine(reportDirectory, "quest_hands_visual_patch.json");
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
                left_prefab = LeftPrefabPath,
                right_prefab = RightPrefabPath,
                errors = Array.Empty<string>(),
            };

            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var origin = Single<XROrigin>(errors, "XROrigin");
                var cameraOffset = origin?.CameraFloorOffsetObject?.transform;
                if (cameraOffset == null) errors.Add("Existing XROrigin Camera Offset is missing.");
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));

                RemoveExisting(cameraOffset, "Left Hand Visual");
                RemoveExisting(cameraOffset, "Right Hand Visual");
                var left = InstantiateVisual(LeftPrefabPath, "Left Hand Visual", cameraOffset, scene,
                    errors);
                var right = InstantiateVisual(RightPrefabPath, "Right Hand Visual", cameraOffset, scene,
                    errors);

                var telemetry = origin.GetComponent<XrHandVisualTelemetry>() ??
                                origin.gameObject.AddComponent<XrHandVisualTelemetry>();
                telemetry.Configure(left, right);

                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
                if (settings == null) errors.Add("Android OpenXR settings are unavailable.");
                else
                {
                    report.hand_tracking_data_source_enabled = ConfigureFeature(settings,
                        DataSourceFeatureType,
                        new Dictionary<string, int>
                        {
                            ["m_LeftHandPreference"] = 2,
                            ["m_RightHandPreference"] = 2,
                        }, errors);
                    report.data_source_preference = "Both (Unobstructed, Controller)";
                    report.hand_joints_motion_range_enabled = ConfigureFeature(settings,
                        MotionRangeFeatureType,
                        new Dictionary<string, int>
                        {
                            ["m_LeftMotionRange"] = 2,
                            ["m_RightMotionRange"] = 2,
                        }, errors);
                    report.motion_range = "ConformingToController";
                    EditorUtility.SetDirty(settings);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    errors.Add("Scene save returned false.");
                AssetDatabase.SaveAssets();

                report.xr_origins = Count<XROrigin>();
                report.character_controllers = Count<CharacterController>();
                report.locomotion_drivers = Count<XrLocomotionDriver>();
                report.hand_pointers = Count<XrHandsPointer>();
                report.controller_pointers = Count<XrControllerPointer>();
                report.hand_visual_roots = new[] { "Left Hand Visual", "Right Hand Visual" }
                    .Count(name => cameraOffset.Find(name) != null);
                report.hand_mesh_controllers = Count<XRHandMeshController>();
                report.hand_skeleton_drivers = Count<XRHandSkeletonDriver>();
                report.hand_tracking_events = Count<XRHandTrackingEvents>();
                report.skinned_mesh_renderers = Object.FindObjectsByType<SkinnedMeshRenderer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Count(item => item.GetComponentInParent<XRHandMeshController>() != null);
                report.visual_colliders = new[] { left, right }.Where(item => item != null)
                    .Sum(item => item.GetComponentsInChildren<Collider>(true).Length);
                report.telemetry_components = Count<XrHandVisualTelemetry>();

                if (report.xr_origins != 1) errors.Add("Expected exactly one XROrigin.");
                if (report.character_controllers != 1) errors.Add("Expected exactly one CharacterController.");
                if (report.locomotion_drivers != 1) errors.Add("Expected exactly one locomotion driver.");
                if (report.hand_pointers != 2) errors.Add("Expected two hand pointer sources.");
                if (report.controller_pointers != 2) errors.Add("Expected two controller pointer sources.");
                if (report.hand_visual_roots != 2) errors.Add("Expected two hand visual roots.");
                if (report.hand_mesh_controllers != 2) errors.Add("Expected two hand mesh controllers.");
                if (report.hand_skeleton_drivers != 2) errors.Add("Expected two hand skeleton drivers.");
                if (report.hand_tracking_events != 2) errors.Add("Expected two hand tracking event sources.");
                if (report.skinned_mesh_renderers != 2) errors.Add("Expected two skinned hand renderers.");
                if (report.visual_colliders != 0) errors.Add("Hand visuals must not add colliders.");
                if (report.telemetry_components != 1) errors.Add("Expected one hand visual telemetry component.");
                if (!report.hand_tracking_data_source_enabled)
                    errors.Add("Hand Tracking Data Source feature is not enabled.");
                if (!report.hand_joints_motion_range_enabled)
                    errors.Add("Hand Joints Motion Range feature is not enabled.");

                report.scene_sha256_after = HashFile(sceneFile);
                report.errors = errors.ToArray();
                report.status = errors.Count == 0 ? "PASS" : "FAIL";
                Write(reportPath, report);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                Debug.Log("ITPU_QUEST_HANDS_VISUAL_PATCH=" + JsonUtility.ToJson(report));
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

        private static XRHandMeshController InstantiateVisual(string path, string name,
            Transform parent, UnityEngine.SceneManagement.Scene scene, ICollection<string> errors)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                errors.Add("Missing official XR Hands prefab: " + path);
                return null;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                errors.Add("Could not instantiate hand prefab: " + path);
                return null;
            }
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            var controller = instance.GetComponent<XRHandMeshController>();
            if (controller == null || controller.handMeshRenderer == null)
                errors.Add(name + " is missing XRHandMeshController/renderer.");
            return controller;
        }

        private static void RemoveExisting(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        private static bool ConfigureFeature(OpenXRSettings settings, string typeName,
            IReadOnlyDictionary<string, int> integerProperties, ICollection<string> errors)
        {
            var feature = settings.GetFeatures().SingleOrDefault(item =>
                item != null && item.GetType().FullName == typeName);
            if (feature == null)
            {
                errors.Add("OpenXR feature unavailable: " + typeName);
                return false;
            }

            feature.enabled = true;
            var serialized = new SerializedObject(feature);
            foreach (var pair in integerProperties)
            {
                var property = serialized.FindProperty(pair.Key);
                if (property == null)
                {
                    errors.Add(typeName + " missing property " + pair.Key);
                    continue;
                }
                property.intValue = pair.Value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            return feature.enabled;
        }

        private static T Single<T>(ICollection<string> errors, string label) where T : Component
        {
            var values = Object.FindObjectsByType<T>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (values.Length == 1) return values[0];
            errors.Add($"Expected exactly one {label}; found {values.Length}.");
            return values.FirstOrDefault();
        }

        private static int Count<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

        private static void Write(string path, PatchReport report) =>
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + Environment.NewLine);

        private static string HashFile(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
