using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ITPU.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using Object = UnityEngine.Object;

namespace ITPU.XR.Editor
{
    public static class VerticalSliceBuilder
    {
        private const string TransferPath = "Assets/ITPU/Data/itpu_403_transfer.json";
        private const string EnvironmentPath = "Assets/ITPU/Art/Environment/ITPU_403_v004.fbx";
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";
        private const string MaterialDirectory = "Assets/ITPU/Art/Materials";
        private const string TextureDirectory = "Assets/ITPU/Art/Textures";
        private const string SettingsDirectory = "Assets/ITPU/Settings";
        private const string AimMaterialPath = "Assets/ITPU/Art/Materials/ITPU_xr_aim_unlit.mat";
        private const string LeftHandVisualPrefab =
            "Assets/Samples/XR Hands/1.9.0/HandVisualizer/Prefabs/Left Hand Tracking.prefab";
        private const string RightHandVisualPrefab =
            "Assets/Samples/XR Hands/1.9.0/HandVisualizer/Prefabs/Right Hand Tracking.prefab";
        // Blender FBX is right-handed while Unity scene space is left-handed. The
        // imported FBX therefore mirrors authored X. Every generated scene-space
        // object and collider must use the same conversion as the visible mesh.
        private const float AuthoredSpawnYaw = 190f;
        private static readonly Vector3 AcceptedSpawnPosition = ToUnityPoint(new Vector3(3.20f, 0f, -10.70f));
        private static readonly Vector3 AcceptedSpawnForward = ToUnityYaw(AuthoredSpawnYaw) * Vector3.forward;
        private static readonly Vector3 DemoButtonPosition = ToUnityPoint(new Vector3(3.15f, 1.25f, -12.05f));
        private static readonly Vector3 ResetButtonPosition = ToUnityPoint(new Vector3(3.55f, 1.25f, -12.05f));
        private static readonly Vector3 TurnModeButtonPosition = ToUnityPoint(new Vector3(3.84f, 1.25f, -12.05f));

        [Serializable] private sealed class VectorData { public float x; public float y; public float z; }
        [Serializable] private sealed class ColliderData { public string name; public VectorData center; public VectorData size; }
        [Serializable] private sealed class RootData
        {
            public string id; public VectorData position; public float yaw_degrees; public bool movable;
            public bool teleport_surface; public ColliderData[] colliders;
        }
        [Serializable] private sealed class MaterialData
        {
            public string id; public string color_hex; public float roughness; public float metallic;
            public string base_texture; public string mask_texture;
        }
        [Serializable] private sealed class TransferData
        {
            public int schema_version; public string status; public string source_blend;
            public string source_blend_sha256; public string zone; public RootData[] roots;
            public MaterialData[] materials; public string[] skipped_non_box_colliders;
        }
        [Serializable] private sealed class PackageEntry { public string name; public string version; }
        [Serializable] private sealed class DependencyEntry
        {
            public string path; public string guid; public string sha256; public string meta_sha256;
            public string publication;
        }
        [Serializable] private sealed class DependencyManifest
        {
            public int schema_version; public string scene; public string scope; public bool self_contained;
            public string quest; public DependencyEntry[] asset_dependencies; public DependencyEntry[] build_dependencies;
        }
        [Serializable] private sealed class BuildReport
        {
            public string status; public string unity_version; public string scene; public string environment_asset;
            public string source_blend; public string source_blend_sha256; public int stable_roots_found;
            public int expected_stable_roots; public int box_colliders; public int static_mesh_colliders;
            public int xr_origins; public int locomotion_drivers; public int character_controllers;
            public int hand_pointer_sources; public int controller_pointer_sources; public int interactables;
            public int pointer_visuals; public int pointer_line_renderers; public int pointer_hit_markers;
            public int pointer_visual_colliders;
            public int hand_mesh_controllers; public int hand_skeleton_drivers;
            public int hand_tracking_events; public int hand_visual_colliders;
            public int tracked_pose_drivers; public bool hmd_pose_bindings; public bool reset_path;
            public bool openxr_standalone;
            public bool openxr_android; public bool meta_quest_feature; public bool xr_hands_feature;
            public bool hand_tracking_data_source; public bool hand_joints_motion_range;
            public VectorData imported_bounds_size_m; public PackageEntry[] packages;
            public string[] errors; public string quest;
        }

        [MenuItem("ITPU/Build WO-17 Vertical Slice")]
        public static void Build()
        {
            var errors = new List<string>();
            Directory.CreateDirectory(MaterialDirectory);
            Directory.CreateDirectory(SettingsDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/ITPU/Scenes");

            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(TransferPath);
            if (source == null) throw new FileNotFoundException("Transfer data was not imported", TransferPath);
            var transfer = JsonUtility.FromJson<TransferData>(source.text);
            if (transfer == null || transfer.schema_version != 1 || transfer.roots == null)
                throw new InvalidDataException("Invalid transfer data");

            var pipeline = EnsureRenderPipeline();
            var materials = EnsureMaterials(transfer.materials, pipeline);
            var openXr = ConfigureOpenXr(errors);
            ConfigurePlayer();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ITPU_403_XR";
            var environmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            if (environmentPrefab == null) throw new FileNotFoundException("Environment FBX was not imported", EnvironmentPath);
            var environment = (GameObject)PrefabUtility.InstantiatePrefab(environmentPrefab, scene);
            environment.name = "ENVIRONMENT_403_ACCEPTED_004";
            AddStableId(environment, "environment.work403.v004");
            ApplyMaterials(environment, materials);

            var rootCount = 0;
            var colliderCount = 0;
            foreach (var rootData in transfer.roots)
            {
                var transform = FindByName(environment.transform, rootData.id);
                if (transform == null)
                {
                    errors.Add("Missing imported stable root: " + rootData.id);
                    continue;
                }
                rootCount++;
                AddStableId(transform.gameObject, rootData.id);
                Transform colliderFrame = null;
                if ((rootData.colliders?.Length ?? 0) > 0)
                    colliderFrame = CreateColliderFrame(environment.transform, transform, rootData, errors);
                foreach (var colliderData in rootData.colliders ?? Array.Empty<ColliderData>())
                {
                    CreateCollider(colliderFrame, environment.transform, rootData, colliderData, errors);
                    colliderCount++;
                }
                if (rootData.teleport_surface && transform.GetComponent<TeleportSurface>() == null)
                    transform.gameObject.AddComponent<TeleportSurface>();
            }

            var meshColliderCount = AddBinMeshCollider(environment, errors);
            var bounds = CalculateBounds(environment, errors);
            if (bounds.size.x < 4f || bounds.size.x > 6f || bounds.size.y < 2.7f ||
                bounds.size.y > 3.5f || bounds.size.z < 6.5f || bounds.size.z > 8f)
                errors.Add($"Imported bounds reject metre/Y-up contract: {bounds.size}");

            var button = CreateDemoInteractable(materials);
            var resetButton = CreateResetInteractable(materials);
            var turnModeButton = CreateTurnModeInteractable(materials);
            var rig = CreateRig(button, resetButton, turnModeButton, materials);
            CreateLighting();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) errors.Add("Scene save returned false");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            WriteDependencyManifest();

            var report = new BuildReport
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                environment_asset = EnvironmentPath,
                source_blend = transfer.source_blend,
                source_blend_sha256 = transfer.source_blend_sha256,
                stable_roots_found = rootCount,
                expected_stable_roots = transfer.roots.Length,
                box_colliders = colliderCount,
                static_mesh_colliders = meshColliderCount,
                xr_origins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                locomotion_drivers = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                character_controllers = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                hand_pointer_sources = Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                controller_pointer_sources = Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                pointer_visuals = Object.FindObjectsByType<XrPointerVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                pointer_line_renderers = Object.FindObjectsByType<XrPointerVisual>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Count(item => item.Line != null),
                pointer_hit_markers = Object.FindObjectsByType<XrPointerVisual>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Count(item => item.HitMarker != null && item.HitMarkerRenderer != null),
                pointer_visual_colliders = Object.FindObjectsByType<XrPointerVisual>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Sum(item => item.GetComponentsInChildren<Collider>(true).Length),
                hand_mesh_controllers = Object.FindObjectsByType<XRHandMeshController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                hand_skeleton_drivers = Object.FindObjectsByType<XRHandSkeletonDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                hand_tracking_events = Object.FindObjectsByType<XRHandTrackingEvents>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                hand_visual_colliders = Object.FindObjectsByType<XRHandMeshController>(FindObjectsInactive.Include,
                        FindObjectsSortMode.None).Sum(item => item.GetComponentsInChildren<Collider>(true).Length),
                interactables = Object.FindObjectsByType<DemoPressInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                tracked_pose_drivers = Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                hmd_pose_bindings = HasExpectedPoseBindings(originCamera: true),
                reset_path = Object.FindObjectsByType<SessionResetButton>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                openxr_standalone = openXr.standalone,
                openxr_android = openXr.android,
                meta_quest_feature = openXr.meta,
                xr_hands_feature = openXr.hands,
                hand_tracking_data_source = openXr.dataSource,
                hand_joints_motion_range = openXr.motionRange,
                imported_bounds_size_m = FromVector(bounds.size),
                packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Where(item => item.name.StartsWith("com.unity.xr.") || item.name == "com.unity.inputsystem" ||
                                   item.name == "com.unity.render-pipelines.universal" || item.name == "com.unity.test-framework")
                    .OrderBy(item => item.name).Select(item => new PackageEntry { name = item.name, version = item.version }).ToArray(),
                errors = errors.ToArray(),
                quest = "NOT_RUN"
            };
            if (report.xr_origins != 1) errors.Add("Expected exactly one XROrigin");
            if (report.locomotion_drivers != 1) errors.Add("Expected exactly one locomotion driver");
            if (report.character_controllers != 1) errors.Add("Expected exactly one CharacterController");
            if (report.hand_pointer_sources != 2) errors.Add("Expected two hand pointer sources");
            if (report.controller_pointer_sources != 2) errors.Add("Expected two controller pointer sources");
            if (report.pointer_visuals != 4 || report.pointer_line_renderers != 4 ||
                report.pointer_hit_markers != 4)
                errors.Add("Expected four complete pointer visuals");
            if (report.pointer_visual_colliders != 0) errors.Add("Pointer visuals must not add colliders");
            if (report.hand_mesh_controllers != 2) errors.Add("Expected two hand mesh controllers");
            if (report.hand_skeleton_drivers != 2) errors.Add("Expected two hand skeleton drivers");
            if (report.hand_tracking_events != 2) errors.Add("Expected two hand tracking event sources");
            if (report.hand_visual_colliders != 0) errors.Add("Hand visuals must not add colliders");
            if (!report.hand_tracking_data_source) errors.Add("Hand tracking data-source feature is disabled");
            if (!report.hand_joints_motion_range) errors.Add("Hand joints motion-range feature is disabled");
            if (report.tracked_pose_drivers != 3 || !report.hmd_pose_bindings)
                errors.Add("Tracked pose wiring is incomplete");
            if (!report.reset_path) errors.Add("Accessible reset path is missing");
            report.errors = errors.ToArray();
            report.status = errors.Count == 0 ? "PASS" : "FAIL";
            WriteReport("unity_scene_build.json", JsonUtility.ToJson(report, true));
            Debug.Log("ITPU_WO17_BUILD_REPORT=" + JsonUtility.ToJson(report));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        }

        public static void BuildBatch() => Build();

        private static UniversalRenderPipelineAsset EnsureRenderPipeline()
        {
            var path = SettingsDirectory + "/ITPU_URP.asset";
            var rendererPath = SettingsDirectory + "/ITPU_UniversalRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "ITPU_UniversalRenderer";
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "ITPU_URP";
                AssetDatabase.CreateAsset(pipeline, path);
            }
            else if (pipeline.rendererDataList.Length == 0 || pipeline.rendererDataList[0] == null)
            {
                var serialized = new SerializedObject(pipeline);
                var renderers = serialized.FindProperty("m_RendererDataList");
                renderers.arraySize = 1;
                renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        private static Dictionary<string, Material> EnsureMaterials(IEnumerable<MaterialData> definitions,
            UniversalRenderPipelineAsset pipeline)
        {
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || pipeline == null) throw new InvalidOperationException("URP Lit shader unavailable");
            foreach (var definition in definitions)
            {
                var path = $"{MaterialDirectory}/ITPU_{definition.id}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = "ITPU_" + definition.id };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                if (ColorUtility.TryParseHtmlString(definition.color_hex, out var color))
                    material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", definition.metallic);
                material.SetFloat("_Smoothness", 1f - definition.roughness);
                if (!string.IsNullOrEmpty(definition.base_texture))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDirectory}/{definition.base_texture}");
                    material.SetTexture("_BaseMap", texture);
                }
                if (!string.IsNullOrEmpty(definition.mask_texture))
                {
                    var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDirectory}/{definition.mask_texture}");
                    material.SetTexture("_MetallicGlossMap", mask);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                EditorUtility.SetDirty(material);
                result[material.name] = material;
                result[definition.id] = material;
            }
            return result;
        }

        private static void ApplyMaterials(GameObject environment, IReadOnlyDictionary<string, Material> materials)
        {
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                var assigned = renderer.sharedMaterials;
                for (var i = 0; i < assigned.Length; i++)
                {
                    var name = assigned[i] == null ? "" : assigned[i].name.Replace(" (Instance)", "");
                    if (materials.TryGetValue(name, out var external)) assigned[i] = external;
                    else if (name.StartsWith("ITPU_", StringComparison.OrdinalIgnoreCase) &&
                             materials.TryGetValue(name.Substring(5), out external)) assigned[i] = external;
                }
                renderer.sharedMaterials = assigned;
            }
        }

        private static GameObject CreateRig(DemoPressInteractable button, DemoPressInteractable resetButton,
            DemoPressInteractable turnModeButton, IReadOnlyDictionary<string, Material> materials)
        {
            var rig = new GameObject("XR Origin [ITPU]");
            // The accepted 403 interior extends from this entry point toward world -Z.
            // Face the visitor into the room instead of toward the exterior wall/sky.
            rig.transform.SetPositionAndRotation(AcceptedSpawnPosition,
                Quaternion.LookRotation(AcceptedSpawnForward, Vector3.up));
            AddStableId(rig, "xr.origin.primary");
            var origin = rig.AddComponent<XROrigin>();
            var controller = rig.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.height = 1.8f;
            controller.radius = 0.24f;
            controller.stepOffset = 0.22f;
            controller.skinWidth = 0.03f;
            var lifecycle = rig.AddComponent<XrLifecycleState>();
            var priority = rig.AddComponent<InteractionPriority>();
            var locomotion = rig.AddComponent<XrLocomotionDriver>();
            var teleport = rig.AddComponent<HandTeleportIntentAdapter>();
            var reset = rig.AddComponent<VerticalSliceReset>();

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(rig.transform, false);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 80f;
            cameraObject.AddComponent<AudioListener>();
            var headPoseDriver = AddTrackedPoseDriver(cameraObject, "HMD", "<XRHMD>/centerEyePosition",
                "<XRHMD>/centerEyeRotation", "<XRHMD>/trackingState");

            origin.Origin = rig;
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = camera;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 1.65f;
            locomotion.Configure(origin, camera, lifecycle, priority);
            var turnMode = turnModeButton.gameObject.AddComponent<LocomotionTurnModeControl>();
            turnMode.Configure(turnModeButton, locomotion, turnModeButton.GetComponentInChildren<TextMesh>());
            var startupPose = rig.AddComponent<XrStartupPoseAligner>();
            startupPose.Configure(origin, camera, lifecycle, headPoseDriver,
                AcceptedSpawnPosition, AcceptedSpawnForward);
            locomotion.SetStartupPoseAligner(startupPose);
            var reticle = CreateTeleportReticle(materials);
            teleport.Configure(lifecycle, priority, locomotion, camera, reticle);
            var leftObject = new GameObject("Left Hand Pointer");
            leftObject.transform.SetParent(rig.transform, false);
            var left = leftObject.AddComponent<XrHandsPointer>();
            left.Configure(true, teleport, priority, lifecycle, offset.transform);
            left.ConfigureVisual(CreatePointerVisual(leftObject));
            var rightObject = new GameObject("Right Hand Pointer");
            rightObject.transform.SetParent(rig.transform, false);
            var right = rightObject.AddComponent<XrHandsPointer>();
            right.Configure(false, teleport, priority, lifecycle, offset.transform);
            right.ConfigureVisual(CreatePointerVisual(rightObject));
            var leftController = CreateControllerPointer(offset.transform, true, teleport, priority, lifecycle);
            var rightController = CreateControllerPointer(offset.transform, false, teleport, priority, lifecycle);
            var handVisuals = CreateHandVisuals(offset.transform);
            var handTelemetry = rig.AddComponent<XrHandVisualTelemetry>();
            handTelemetry.Configure(handVisuals.left, handVisuals.right);
            reset.Configure(locomotion, teleport, priority, new[] { button, resetButton, turnModeButton },
                new[] { left, right }, new[] { leftController, rightController }, startupPose, turnMode);
            var resetRoute = resetButton.gameObject.AddComponent<SessionResetButton>();
            resetRoute.Configure(resetButton, reset);
            return rig;
        }

        private static DemoPressInteractable CreateDemoInteractable(IReadOnlyDictionary<string, Material> materials)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "DEMO_ROUTE_BUTTON";
            root.transform.SetPositionAndRotation(DemoButtonPosition, Quaternion.identity);
            root.transform.localScale = new Vector3(0.32f, 0.18f, 0.12f);
            AddStableId(root, "interaction.demo-route-button");
            var renderer = root.GetComponent<Renderer>();
            if (materials.TryGetValue("accent", out var accent)) renderer.sharedMaterial = accent;
            var interactable = root.AddComponent<DemoPressInteractable>();
            interactable.Configure(renderer);
            CreateWorldLabel(root.transform, "ACTIVATE");
            return interactable;
        }

        private static TextMesh CreateWorldLabel(Transform parent, string text)
        {
            var label = new GameObject(text + " Label");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, 0f, 0.56f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            label.transform.localScale = new Vector3(
                0.05f / Mathf.Max(0.001f, Mathf.Abs(parent.lossyScale.x)),
                0.05f / Mathf.Max(0.001f, Mathf.Abs(parent.lossyScale.y)),
                0.05f / Mathf.Max(0.001f, Mathf.Abs(parent.lossyScale.z)));
            var mesh = label.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.alignment = TextAlignment.Center;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.characterSize = 0.08f;
            mesh.fontSize = 64;
            mesh.color = Color.white;
            return mesh;
        }

        private static DemoPressInteractable CreateResetInteractable(
            IReadOnlyDictionary<string, Material> materials)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "SESSION_RESET_BUTTON";
            root.transform.SetPositionAndRotation(ResetButtonPosition, Quaternion.identity);
            root.transform.localScale = new Vector3(0.28f, 0.18f, 0.12f);
            AddStableId(root, "interaction.session-reset");
            var renderer = root.GetComponent<Renderer>();
            if (materials.TryGetValue("accent", out var accent)) renderer.sharedMaterial = accent;
            var interactable = root.AddComponent<DemoPressInteractable>();
            interactable.Configure(renderer);
            CreateWorldLabel(root.transform, "RESET");
            return interactable;
        }

        private static DemoPressInteractable CreateTurnModeInteractable(
            IReadOnlyDictionary<string, Material> materials)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "TURN_MODE_BUTTON";
            root.transform.SetPositionAndRotation(TurnModeButtonPosition, Quaternion.identity);
            root.transform.localScale = new Vector3(0.28f, 0.18f, 0.12f);
            AddStableId(root, "interaction.turn-comfort-mode");
            var renderer = root.GetComponent<Renderer>();
            if (materials.TryGetValue("accent", out var accent)) renderer.sharedMaterial = accent;
            var interactable = root.AddComponent<DemoPressInteractable>();
            interactable.Configure(renderer);
            CreateWorldLabel(root.transform, "TURN\nSNAP 30");
            return interactable;
        }

        private static XrControllerPointer CreateControllerPointer(Transform parent, bool leftHand,
            HandTeleportIntentAdapter teleport, InteractionPriority priority, XrLifecycleState lifecycle)
        {
            var label = leftHand ? "Left" : "Right";
            var controller = new GameObject(label + " Controller Pointer");
            controller.transform.SetParent(parent, false);
            AddTrackedPoseDriver(controller, label + " Controller",
                leftHand ? "<XRController>{LeftHand}/pointerPosition" : "<XRController>{RightHand}/pointerPosition",
                leftHand ? "<XRController>{LeftHand}/pointerRotation" : "<XRController>{RightHand}/pointerRotation",
                leftHand ? "<XRController>{LeftHand}/trackingState" : "<XRController>{RightHand}/trackingState");
            var pointer = controller.AddComponent<XrControllerPointer>();
            pointer.Configure(leftHand, teleport, priority, lifecycle);
            pointer.ConfigureVisual(CreatePointerVisual(controller));
            return pointer;
        }

        private static XrPointerVisual CreatePointerVisual(GameObject pointerRoot)
        {
            var material = EnsureAimMaterial();
            var lineObject = new GameObject("Aim Line");
            lineObject.transform.SetParent(pointerRoot.transform, false);
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
            marker.transform.SetParent(pointerRoot.transform, false);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            var markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = material;
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;

            var visual = pointerRoot.AddComponent<XrPointerVisual>();
            visual.Configure(line, marker.transform, markerRenderer);
            return visual;
        }

        private static Material EnsureAimMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(AimMaterialPath);
            if (existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable");
            var material = new Material(shader) { name = "ITPU_xr_aim_unlit", enableInstancing = true };
            material.SetColor("_BaseColor", new Color(0.22f, 0.82f, 1f, 1f));
            AssetDatabase.CreateAsset(material, AimMaterialPath);
            return material;
        }

        private static (XRHandMeshController left, XRHandMeshController right) CreateHandVisuals(
            Transform parent)
        {
            return (InstantiateHandVisual(LeftHandVisualPrefab, "Left Hand Visual", parent),
                InstantiateHandVisual(RightHandVisualPrefab, "Right Hand Visual", parent));
        }

        private static XRHandMeshController InstantiateHandVisual(string path, string name,
            Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("XR Hands visual prefab missing", path);
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate " + path);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            var controller = instance.GetComponent<XRHandMeshController>();
            if (controller == null || controller.handMeshRenderer == null)
                throw new InvalidOperationException(name + " is missing XRHandMeshController/renderer");
            return controller;
        }

        private static Transform CreateTeleportReticle(IReadOnlyDictionary<string, Material> materials)
        {
            var reticle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            reticle.name = "TELEPORT_TARGET_RETICLE";
            reticle.transform.localScale = new Vector3(0.12f, 0.006f, 0.12f);
            Object.DestroyImmediate(reticle.GetComponent<Collider>());
            if (materials.TryGetValue("accent", out var accent))
                reticle.GetComponent<Renderer>().sharedMaterial = accent;
            reticle.SetActive(false);
            return reticle.transform;
        }

        private static TrackedPoseDriver AddTrackedPoseDriver(GameObject target, string prefix,
            string positionBinding, string rotationBinding, string trackingBinding)
        {
            var driver = target.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.ignoreTrackingState = false;
            driver.positionInput = new InputActionProperty(new InputAction(prefix + " Position",
                InputActionType.Value, positionBinding, expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction(prefix + " Rotation",
                InputActionType.Value, rotationBinding, expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(new InputAction(prefix + " Tracking State",
                InputActionType.Value, trackingBinding, expectedControlType: "Integer"));
            return driver;
        }

        private static bool HasExpectedPoseBindings(bool originCamera)
        {
            var drivers = Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (drivers.Length != 3) return false;
            var cameraDriver = drivers.FirstOrDefault(item => item.GetComponent<Camera>() != null);
            return cameraDriver != null && cameraDriver.positionInput.action != null &&
                   cameraDriver.rotationInput.action != null && cameraDriver.trackingStateInput.action != null &&
                   cameraDriver.positionInput.action.bindings.Any(item => item.path == "<XRHMD>/centerEyePosition") &&
                   cameraDriver.rotationInput.action.bindings.Any(item => item.path == "<XRHMD>/centerEyeRotation") &&
                   cameraDriver.trackingStateInput.action.bindings.Any(item => item.path == "<XRHMD>/trackingState");
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Key Light");
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.88f;
            light.shadows = LightShadows.None;
            light.shadowStrength = 0f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.84f, 0.86f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.66f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.48f, 0.44f, 0.38f);
            RenderSettings.ambientIntensity = 1.20f;
        }

        private static (bool standalone, bool android, bool meta, bool hands, bool dataSource, bool motionRange)
            ConfigureOpenXr(List<string> errors)
        {
            const string settingsPath = SettingsDirectory + "/XRGeneralSettingsPerBuildTarget.asset";
            var store = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
            if (store == null)
            {
                store = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(store, settingsPath);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, store, true);
            }
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                if (!store.HasSettingsForBuildTarget(group)) store.CreateDefaultSettingsForBuildTarget(group);
                if (!store.HasManagerSettingsForBuildTarget(group)) store.CreateDefaultManagerSettingsForBuildTarget(group);
                store.SettingsForBuildTarget(group).InitManagerOnStart = true;
                if (!XRPackageMetadataStore.AssignLoader(store.ManagerSettingsForBuildTarget(group),
                        "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                    errors.Add("Could not assign OpenXR loader for " + group);
                FeatureHelpers.RefreshFeatures(group);
            }
            AssetDatabase.SaveAssets();
            var standalone = HasOpenXrLoader(store, BuildTargetGroup.Standalone);
            var android = HasOpenXrLoader(store, BuildTargetGroup.Android);
            var androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var metaFeature = androidSettings?.GetFeature<MetaQuestFeature>();
            var handFeature = androidSettings?.GetFeature<HandTracking>();
            var handAim = androidSettings?.GetFeature<MetaHandTrackingAim>();
            var handProfile = androidSettings?.GetFeature<HandInteractionProfile>();
            var touchProfile = androidSettings?.GetFeature<OculusTouchControllerProfile>();
            if (metaFeature != null) metaFeature.enabled = true;
            if (handFeature != null) handFeature.enabled = true;
            if (handAim != null) handAim.enabled = true;
            if (handProfile != null) handProfile.enabled = true;
            if (touchProfile != null) touchProfile.enabled = true;
            var dataSource = ConfigureFeatureByName(androidSettings,
                "UnityEngine.XR.Hands.OpenXR.HandTrackingDataSourceFeature",
                errors, ("m_LeftHandPreference", 2), ("m_RightHandPreference", 2));
            var motionRange = ConfigureFeatureByName(androidSettings,
                "UnityEngine.XR.Hands.OpenXR.HandJointsMotionRangeFeature",
                errors, ("m_LeftMotionRange", 2), ("m_RightMotionRange", 2));
            if (androidSettings != null) EditorUtility.SetDirty(androidSettings);
            AssetDatabase.SaveAssets();
            if (metaFeature == null) errors.Add("Meta Quest OpenXR feature unavailable");
            if (handFeature == null) errors.Add("XR Hands OpenXR feature unavailable");
            return (standalone, android, metaFeature != null && metaFeature.enabled,
                handFeature != null && handFeature.enabled, dataSource, motionRange);
        }

        private static bool ConfigureFeatureByName(OpenXRSettings settings, string fullTypeName,
            ICollection<string> errors, params (string property, int value)[] values)
        {
            var feature = settings?.GetFeatures().SingleOrDefault(item =>
                item != null && item.GetType().FullName == fullTypeName);
            if (feature == null)
            {
                errors.Add("OpenXR feature unavailable: " + fullTypeName);
                return false;
            }
            feature.enabled = true;
            var serialized = new SerializedObject(feature);
            foreach (var value in values)
            {
                var property = serialized.FindProperty(value.property);
                if (property == null)
                {
                    errors.Add(fullTypeName + " missing property " + value.property);
                    continue;
                }
                property.intValue = value.value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            return feature.enabled;
        }

        private static bool HasOpenXrLoader(XRGeneralSettingsPerBuildTarget store, BuildTargetGroup group) =>
            store.ManagerSettingsForBuildTarget(group)?.activeLoaders.Any(loader =>
                loader != null && loader.GetType().FullName == "UnityEngine.XR.OpenXR.OpenXRLoader") == true;

        private static void ConfigurePlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "ITPU";
            PlayerSettings.productName = "Po tu storonu koda";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "uz.itpu.beyondcode");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        }

        private static int AddBinMeshCollider(GameObject environment, ICollection<string> errors)
        {
            var bin = FindByName(environment.transform, "BIN_403");
            if (bin == null) { errors.Add("BIN_403 root missing for static mesh proxy"); return 0; }
            var mesh = bin.GetComponentInChildren<MeshFilter>(true);
            if (mesh == null || mesh.sharedMesh == null) { errors.Add("BIN_403 mesh missing"); return 0; }
            var collider = mesh.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh.sharedMesh;
            collider.convex = false;
            return 1;
        }

        private static Transform CreateColliderFrame(Transform environment, Transform importedRoot,
            RootData rootData, ICollection<string> errors)
        {
            const float scaleTolerance = 0.001f;
            var importedScale = importedRoot.lossyScale;
            if (Mathf.Abs(importedScale.x - 1f) > scaleTolerance ||
                Mathf.Abs(importedScale.y - 1f) > scaleTolerance ||
                Mathf.Abs(importedScale.z - 1f) > scaleTolerance)
                errors.Add($"Collider proxy requires unit imported scale: {rootData.id} scale={importedScale}");

            var proxy = new GameObject(rootData.id + "__COLLIDER_FRAME");
            proxy.transform.SetParent(importedRoot, false);
            proxy.transform.SetPositionAndRotation(
                environment.TransformPoint(ToUnityPoint(rootData.position)),
                environment.rotation * ToUnityYaw(rootData.yaw_degrees));
            proxy.transform.localScale = Vector3.one;
            return proxy.transform;
        }

        private static BoxCollider CreateCollider(Transform colliderFrame, Transform environment,
            RootData rootData, ColliderData colliderData, ICollection<string> errors)
        {
            if (colliderFrame == null) throw new ArgumentNullException(nameof(colliderFrame));
            var collider = colliderFrame.gameObject.AddComponent<BoxCollider>();
            collider.center = ToUnityPoint(colliderData.center);
            collider.size = ToVector(colliderData.size);
            ValidateColliderCorners(environment, rootData, colliderData, collider, errors);
            return collider;
        }

        private static void ValidateColliderCorners(Transform environment, RootData rootData,
            ColliderData colliderData, BoxCollider collider, ICollection<string> errors)
        {
            const float cornerTolerance = 0.002f;
            var center = ToUnityPoint(colliderData.center);
            var half = ToVector(colliderData.size) * 0.5f;
            var canonical = environment.localToWorldMatrix * Matrix4x4.TRS(
                ToUnityPoint(rootData.position), ToUnityYaw(rootData.yaw_degrees), Vector3.one);
            var expected = new List<Vector3>(8);
            var actual = new List<Vector3>(8);
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = center + Vector3.Scale(half, new Vector3(x, y, z));
                expected.Add(canonical.MultiplyPoint3x4(corner));
                actual.Add(collider.transform.TransformPoint(corner));
            }

            foreach (var expectedCorner in expected)
            {
                var nearest = actual.Min(value => Vector3.Distance(value, expectedCorner));
                if (nearest <= cornerTolerance) continue;
                errors.Add($"Collider proxy corner mismatch: {rootData.id}/{colliderData.name} " +
                           $"expected={expectedCorner} nearest={nearest:F4}m");
                return;
            }
        }

        private static Bounds CalculateBounds(GameObject root, ICollection<string> errors)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { errors.Add("Imported environment has no renderers"); return default; }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void AddStableId(GameObject target, string value)
        {
            var stable = target.GetComponent<StableId>() ?? target.AddComponent<StableId>();
            stable.Set(value);
        }

        private static Vector3 ToVector(VectorData value) => new(value.x, value.y, value.z);
        private static Vector3 ToUnityPoint(VectorData value) => new(-value.x, value.y, value.z);
        private static Vector3 ToUnityPoint(Vector3 value) => new(-value.x, value.y, value.z);
        private static Quaternion ToUnityYaw(float authoredYaw) => Quaternion.Euler(0f, -authoredYaw, 0f);
        private static VectorData FromVector(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };

        private static void WriteReport(string filename, string contents)
        {
            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var directory = Path.Combine(workspace, "reports", "local", "WO-17");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), contents + Environment.NewLine);
        }

        private static void WriteDependencyManifest()
        {
            var assetEntries = AssetDatabase.GetDependencies(ScenePath, true)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(CreateDependencyEntry)
                .ToArray();
            var buildEntries = new[]
                {
                    "Packages/manifest.json",
                    "Packages/packages-lock.json",
                    "ProjectSettings/ProjectVersion.txt",
                    "ProjectSettings/ProjectSettings.asset"
                }
                .Select(CreateDependencyEntry)
                .ToArray();
            var manifest = new DependencyManifest
            {
                schema_version = 1,
                scene = ScenePath,
                scope = "LIVE_PROJECT_DEPENDENCY_MANIFEST_NOT_STANDALONE_BUNDLE",
                self_contained = false,
                quest = "NOT_RUN",
                asset_dependencies = assetEntries,
                build_dependencies = buildEntries
            };
            WriteReport("unity_dependency_manifest.json", JsonUtility.ToJson(manifest, true));
        }

        private static DependencyEntry CreateDependencyEntry(string assetPath)
        {
            var physicalPath = ResolvePhysicalPath(assetPath);
            return new DependencyEntry
            {
                path = assetPath.Replace('\\', '/'),
                guid = assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                       assetPath.StartsWith("Packages/", StringComparison.Ordinal)
                    ? AssetDatabase.AssetPathToGUID(assetPath)
                    : "N/A",
                sha256 = HashIfFile(physicalPath),
                meta_sha256 = HashIfFile(physicalPath + ".meta"),
                publication = assetPath.StartsWith("Assets/ITPU/Art/", StringComparison.Ordinal)
                    ? "LOCAL_ONLY_PRIVATE"
                    : "NOT_REVIEWED_FOR_PUBLICATION"
            };
        }

        private static string ResolvePhysicalPath(string assetPath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                assetPath.StartsWith("ProjectSettings/", StringComparison.Ordinal) ||
                assetPath == "Packages/manifest.json" || assetPath == "Packages/packages-lock.json")
                return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal)) return "";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package == null) return "";
            var prefix = "Packages/" + package.name + "/";
            var relative = assetPath.StartsWith(prefix, StringComparison.Ordinal)
                ? assetPath.Substring(prefix.Length)
                : "";
            return Path.Combine(package.resolvedPath, relative);
        }

        private static string HashIfFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "N/A";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
