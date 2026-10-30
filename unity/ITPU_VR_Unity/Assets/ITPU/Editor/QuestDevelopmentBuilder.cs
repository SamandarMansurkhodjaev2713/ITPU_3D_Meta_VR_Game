using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace ITPU.XR.Editor
{
    public static class QuestDevelopmentBuilder
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string ApplicationId = "uz.itpu.beyondcode";
        private const string ApkName = "ITPU_VR_WO25_visual_refinement_development.apk";

        [Serializable]
        private sealed class BuildReceipt
        {
            public int schema_version = 1;
            public string work_order = "WO-25";
            public string status;
            public string observed_utc;
            public string unity_version;
            public string build_target;
            public string build_options;
            public string scene;
            public string scene_sha256;
            public string apk;
            public string apk_sha256;
            public long apk_bytes;
            public string application_id;
            public string bundle_version;
            public int bundle_version_code;
            public string minimum_sdk;
            public string requested_target_sdk;
            public string architecture;
            public string scripting_backend;
            public string input_backend;
            public string[] graphics_apis;
            public bool openxr_loader_android;
            public bool meta_quest_feature;
            public bool hand_tracking;
            public bool meta_hand_tracking_aim;
            public bool hand_tracking_data_source;
            public string hand_tracking_data_source_preference;
            public bool hand_joints_motion_range;
            public string hand_joints_motion_range_mode;
            public bool hand_interaction_profile;
            public bool oculus_touch_profile;
            public bool quest_touch_plus_profile;
            public bool force_remove_internet_permission;
            public string latency_optimization;
            public string openxr_validation_status;
            public int openxr_validation_errors;
            public int openxr_validation_warnings;
            public string[] openxr_validation_issues;
            public string build_result;
            public int total_errors;
            public int total_warnings;
            public string install = "NOT_RUN";
            public string device_runtime = "NOT_RUN";
            public string comfort = "NOT_RUN";
            public string performance = "NOT_RUN";
            public string build_identity;
            public string failure;
        }

        [MenuItem("ITPU/Build Quest Development APK")]
        public static void BuildBatch()
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var reportDirectory = Path.Combine(workspace, "reports", "local", "WO-25");
            var buildDirectory = Path.Combine(reportDirectory, "build");
            var apkPath = Path.Combine(buildDirectory, ApkName);
            var receiptPath = Path.Combine(reportDirectory, "quest_visual_refinement_development_build.json");
            Directory.CreateDirectory(buildDirectory);

            var receipt = CreateBaseReceipt(workspace, apkPath);
            try
            {
                ValidateInputs();
                ConfigureAndroidPlayer();
                EnsureNewInputSystemOnly();
                ConfigureOpenXr(receipt);

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new BuildFailedException("Unity could not switch the active build target to Android.");

                CaptureConfiguredSettings(receipt);
                CaptureOpenXrValidation(receipt);
                EditorUserBuildSettings.buildAppBundle = false;
                if (File.Exists(apkPath)) File.Delete(apkPath);

                const BuildOptions options = BuildOptions.Development |
                                             BuildOptions.AllowDebugging |
                                             BuildOptions.StrictMode |
                                             BuildOptions.DetailedBuildReport |
                                             BuildOptions.CompressWithLz4;
                receipt.build_options = options.ToString();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = options
                });

                receipt.build_result = report.summary.result.ToString();
                receipt.total_errors = (int)report.summary.totalErrors;
                receipt.total_warnings = (int)report.summary.totalWarnings;
                if (report.summary.result != BuildResult.Succeeded || !File.Exists(apkPath))
                    throw new BuildFailedException(
                        $"Android build {report.summary.result}; errors={report.summary.totalErrors}; " +
                        $"warnings={report.summary.totalWarnings}.");

                receipt.status = "PASS_BUILD_ONLY_DEVICE_NOT_RUN";
                receipt.apk_bytes = new FileInfo(apkPath).Length;
                receipt.apk_sha256 = HashFile(apkPath);
                receipt.build_identity = "wo25-visual-refinement-v" + receipt.bundle_version_code + "-" +
                                         receipt.apk_sha256.Substring(0, 12);
                WriteReceipt(receiptPath, receipt);
                Debug.Log($"ITPU Quest development APK PASS: {apkPath} ({receipt.apk_bytes} bytes, " +
                          $"sha256={receipt.apk_sha256})");
            }
            catch (Exception exception)
            {
                receipt.status = "FAIL";
                receipt.failure = exception.GetType().Name + ": " + exception.Message;
                if (File.Exists(apkPath))
                {
                    receipt.apk_bytes = new FileInfo(apkPath).Length;
                    receipt.apk_sha256 = HashFile(apkPath);
                }
                WriteReceipt(receiptPath, receipt);
                throw;
            }
        }

        private static BuildReceipt CreateBaseReceipt(string workspace, string apkPath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return new BuildReceipt
            {
                status = "IN_PROGRESS",
                observed_utc = DateTime.UtcNow.ToString("O"),
                unity_version = Application.unityVersion,
                build_target = BuildTarget.Android.ToString(),
                scene = ScenePath,
                scene_sha256 = HashFile(Path.Combine(projectRoot, ScenePath)),
                apk = Path.GetRelativePath(workspace, apkPath).Replace('\\', '/'),
                application_id = ApplicationId,
                input_backend = "NOT_CAPTURED",
                openxr_validation_status = "NOT_RUN",
                openxr_validation_issues = Array.Empty<string>()
            };
        }

        private static void ValidateInputs()
        {
            if (!File.Exists(ScenePath))
                throw new BuildFailedException("Saved XR scene is missing: " + ScenePath);
            var enabledScenes = EditorBuildSettings.scenes.Where(item => item.enabled).Select(item => item.path).ToArray();
            if (enabledScenes.Length != 1 || enabledScenes[0] != ScenePath)
                throw new BuildFailedException("Build Settings must contain exactly the saved ITPU_403_XR scene.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Android Build Support is unavailable for this Unity Editor.");
        }

        private static void ConfigureAndroidPlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "ITPU";
            PlayerSettings.productName = "Po tu storonu koda";
            PlayerSettings.bundleVersion = "0.5.1-visual-refinement";
            PlayerSettings.Android.bundleVersionCode = 38;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.minifyDebug = false;
            PlayerSettings.Android.androidTVCompatibility = false;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        }

        private static void EnsureNewInputSystemOnly()
        {
            var globalSettingsField = typeof(BuildProfile).GetField(
                "s_GlobalPlayerSettings", BindingFlags.Static | BindingFlags.NonPublic);
            var playerSettings = globalSettingsField?.GetValue(null) as PlayerSettings;
            var activeProfile = BuildProfile.GetActiveBuildProfile();
            if (activeProfile != null)
            {
                var overrideField = typeof(BuildProfile).GetField(
                    "m_PlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                playerSettings = overrideField?.GetValue(activeProfile) as PlayerSettings ?? playerSettings;
            }
            if (playerSettings == null)
                throw new BuildFailedException("Could not resolve Unity PlayerSettings for activeInputHandler.");
            var serialized = new SerializedObject(playerSettings);
            var property = serialized.FindProperty("activeInputHandler");
            if (property == null)
                throw new BuildFailedException("PlayerSettings.activeInputHandler is unavailable.");
            property.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureOpenXr(BuildReceipt receipt)
        {
            const string settingsPath = "Assets/ITPU/Settings/XRGeneralSettingsPerBuildTarget.asset";
            var store = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
            var manager = store?.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            receipt.openxr_loader_android = manager?.activeLoaders.Any(loader =>
                loader != null && loader.GetType().FullName == "UnityEngine.XR.OpenXR.OpenXRLoader") == true;
            if (!receipt.openxr_loader_android)
                throw new BuildFailedException("Android OpenXR loader is not assigned.");

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new BuildFailedException("Android OpenXR settings are unavailable.");

            var metaFeature = settings.GetFeature<MetaQuestFeature>();
            if (metaFeature == null) throw new BuildFailedException(nameof(MetaQuestFeature) + " is unavailable.");
            metaFeature.enabled = true;
            metaFeature.ForceRemoveInternetPermission = true;
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            EditorUtility.SetDirty(metaFeature);
            receipt.meta_quest_feature = metaFeature.enabled;
            receipt.force_remove_internet_permission = metaFeature.ForceRemoveInternetPermission;
            receipt.latency_optimization = settings.latencyOptimization.ToString();
            receipt.hand_tracking = Enable<HandTracking>(settings);
            receipt.meta_hand_tracking_aim = Enable<MetaHandTrackingAim>(settings);
            receipt.hand_tracking_data_source = EnableConfiguredFeature(settings,
                "UnityEngine.XR.Hands.OpenXR.HandTrackingDataSourceFeature",
                ("m_LeftHandPreference", 2), ("m_RightHandPreference", 2));
            receipt.hand_tracking_data_source_preference = "Both (Unobstructed, Controller)";
            receipt.hand_joints_motion_range = EnableConfiguredFeature(settings,
                "UnityEngine.XR.Hands.OpenXR.HandJointsMotionRangeFeature",
                ("m_LeftMotionRange", 2), ("m_RightMotionRange", 2));
            receipt.hand_joints_motion_range_mode = "ConformingToController";
            receipt.hand_interaction_profile = Enable<HandInteractionProfile>(settings);
            receipt.oculus_touch_profile = Enable<OculusTouchControllerProfile>(settings);
            receipt.quest_touch_plus_profile = Enable<MetaQuestTouchPlusControllerProfile>(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void CaptureConfiguredSettings(BuildReceipt receipt)
        {
            receipt.bundle_version = PlayerSettings.bundleVersion;
            receipt.bundle_version_code = PlayerSettings.Android.bundleVersionCode;
            receipt.minimum_sdk = PlayerSettings.Android.minSdkVersion.ToString();
            receipt.requested_target_sdk = PlayerSettings.Android.targetSdkVersion.ToString();
            receipt.architecture = PlayerSettings.Android.targetArchitectures.ToString();
            receipt.scripting_backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString();
            receipt.input_backend = ReadActiveInputHandler() == 1 ? "NewInputSystem" : "INVALID";
            receipt.graphics_apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)
                .Select(value => value.ToString()).ToArray();
            if (receipt.input_backend != "NewInputSystem")
                throw new BuildFailedException("Android player must use the New Input System backend only.");
        }

        private static int ReadActiveInputHandler()
        {
            var projectSettings = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProjectSettings/ProjectSettings.asset"));
            var marker = File.ReadLines(projectSettings).FirstOrDefault(line =>
                line.TrimStart().StartsWith("activeInputHandler:", StringComparison.Ordinal));
            if (marker == null || !int.TryParse(marker.Split(':').Last().Trim(), out var value))
                throw new BuildFailedException("Could not read activeInputHandler from ProjectSettings.asset.");
            return value;
        }

        private static void CaptureOpenXrValidation(BuildReceipt receipt)
        {
            var issues = new List<UnityEngine.XR.OpenXR.Features.OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            receipt.openxr_validation_errors = issues.Count(item => item.error);
            receipt.openxr_validation_warnings = issues.Count(item => !item.error);
            receipt.openxr_validation_issues = issues.Select(item =>
                (item.error ? "ERROR: " : "WARNING: ") + item.message).ToArray();
            receipt.openxr_validation_status = receipt.openxr_validation_errors == 0
                ? receipt.openxr_validation_warnings == 0 ? "PASS" : "PASS_WITH_WARNINGS"
                : "FAIL";
            if (receipt.openxr_validation_errors > 0)
                throw new BuildFailedException("Android OpenXR project validation has blocking errors.");
        }

        private static bool Enable<T>(OpenXRSettings settings) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null) throw new BuildFailedException(typeof(T).Name + " is unavailable.");
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            return feature.enabled;
        }

        private static bool EnableConfiguredFeature(OpenXRSettings settings, string fullTypeName,
            params (string property, int value)[] values)
        {
            var feature = settings.GetFeatures().SingleOrDefault(item =>
                item != null && item.GetType().FullName == fullTypeName);
            if (feature == null) throw new BuildFailedException(fullTypeName + " is unavailable.");
            feature.enabled = true;
            var serialized = new SerializedObject(feature);
            foreach (var value in values)
            {
                var property = serialized.FindProperty(value.property);
                if (property == null)
                    throw new BuildFailedException(fullTypeName + " missing property " + value.property + ".");
                property.intValue = value.value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            return feature.enabled;
        }

        private static void WriteReceipt(string path, BuildReceipt receipt)
        {
            receipt.observed_utc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(path, JsonUtility.ToJson(receipt, true) + Environment.NewLine);
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
