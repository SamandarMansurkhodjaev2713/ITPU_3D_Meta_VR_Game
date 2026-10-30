using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ITPU.XR;

namespace ITPU.Editor
{
    public static class QuestVisibilityDiagnostic
    {
        private const string ScenePath = "Assets/ITPU/Scenes/ITPU_403_XR.unity";

        [Serializable]
        private sealed class Vec
        {
            public float x;
            public float y;
            public float z;

            public static Vec From(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };
        }

        [Serializable]
        private sealed class Report
        {
            public string status;
            public string unity_version;
            public string scene;
            public bool environment_active;
            public Vec environment_position;
            public Vec environment_rotation_euler;
            public Vec environment_scale;
            public Vec bounds_center;
            public Vec bounds_size;
            public Vec bounds_min;
            public Vec bounds_max;
            public int renderer_count;
            public int active_enabled_renderers;
            public int missing_material_slots;
            public string[] shader_names;
            public Vec rig_position;
            public Vec rig_rotation_euler;
            public Vec nominal_head_position;
            public Vec initial_forward;
            public int initial_frustum_renderers;
            public string initial_center_ray_hit;
            public Vec reverse_forward;
            public int reverse_frustum_renderers;
            public string reverse_center_ray_hit;
            public float camera_near;
            public float camera_far;
            public int camera_culling_mask;
            public string initial_render;
            public string reverse_render;
            public string[] errors;
        }

        public static void Run()
        {
            var errors = new List<string>();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environment = FindRoot("ENVIRONMENT_FULL_REFINED_005") ??
                              FindRoot("ENVIRONMENT_FULL_ACCEPTED_004") ??
                              FindRoot("ENVIRONMENT_403_ACCEPTED_004");
            var rig = FindRoot("XR Origin [ITPU]");
            var camera = UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (environment == null) errors.Add("Environment root missing");
            if (rig == null) errors.Add("XR Origin root missing");
            if (camera == null) errors.Add("Camera missing");

            var renderers = environment == null
                ? Array.Empty<Renderer>()
                : environment.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length == 0 ? new Bounds() : renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var missingSlots = renderers.Sum(renderer => renderer.sharedMaterials.Count(material => material == null));
            var shaders = renderers.SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null && material.shader != null)
                .Select(material => material.shader.name).Distinct().OrderBy(name => name).ToArray();

            var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var outputDirectory = Path.Combine(workspace, "reports", "local", "WO-23");
            Directory.CreateDirectory(outputDirectory);
            var initialRelative = "reports/local/WO-23/quest_visibility_editor_forward_035.png";
            var reverseRelative = "reports/local/WO-23/quest_visibility_editor_reverse_036.png";

            var report = new Report
            {
                status = errors.Count == 0 ? "PASS" : "FAIL",
                unity_version = Application.unityVersion,
                scene = ScenePath,
                environment_active = environment != null && environment.activeInHierarchy,
                environment_position = Vec.From(environment == null ? Vector3.zero : environment.transform.position),
                environment_rotation_euler = Vec.From(environment == null ? Vector3.zero : environment.transform.eulerAngles),
                environment_scale = Vec.From(environment == null ? Vector3.zero : environment.transform.lossyScale),
                bounds_center = Vec.From(bounds.center),
                bounds_size = Vec.From(bounds.size),
                bounds_min = Vec.From(bounds.min),
                bounds_max = Vec.From(bounds.max),
                renderer_count = renderers.Length,
                active_enabled_renderers = renderers.Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                missing_material_slots = missingSlots,
                shader_names = shaders,
                rig_position = Vec.From(rig == null ? Vector3.zero : rig.transform.position),
                rig_rotation_euler = Vec.From(rig == null ? Vector3.zero : rig.transform.eulerAngles),
                nominal_head_position = Vec.From(camera == null ? Vector3.zero : camera.transform.position),
                camera_near = camera == null ? 0f : camera.nearClipPlane,
                camera_far = camera == null ? 0f : camera.farClipPlane,
                camera_culling_mask = camera == null ? 0 : camera.cullingMask,
                initial_render = initialRelative,
                reverse_render = reverseRelative,
            };

            if (camera != null)
            {
                camera.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                camera.transform.localRotation = Quaternion.identity;
                report.nominal_head_position = Vec.From(camera.transform.position);
                report.initial_forward = Vec.From(camera.transform.forward);
                report.initial_frustum_renderers = FrustumCount(camera, renderers);
                report.initial_center_ray_hit = CenterRayHit(camera);
                Render(camera, Path.Combine(workspace, initialRelative));

                camera.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                report.reverse_forward = Vec.From(camera.transform.forward);
                report.reverse_frustum_renderers = FrustumCount(camera, renderers);
                report.reverse_center_ray_hit = CenterRayHit(camera);
                Render(camera, Path.Combine(workspace, reverseRelative));

                var locomotion = UnityEngine.Object.FindFirstObjectByType<XrLocomotionDriver>(
                    FindObjectsInactive.Include);
                var interiorMargin = locomotion == null ? 0.75f : locomotion.MaximumRoomScaleOffset;
                var head = camera.transform.position;
                if (head.x < bounds.min.x + interiorMargin || head.x > bounds.max.x - interiorMargin ||
                    head.z < bounds.min.z + interiorMargin || head.z > bounds.max.z - interiorMargin)
                    errors.Add($"Nominal head is outside accepted renderer footprint: head={head} " +
                               $"boundsMin={bounds.min} boundsMax={bounds.max}");
            }

            report.errors = errors.ToArray();
            var reportPath = Path.Combine(outputDirectory, "quest_visibility_diagnostic_037.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + Environment.NewLine);
            EditorSceneManager.CloseScene(scene, true);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        }

        private static GameObject FindRoot(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .FirstOrDefault(item => item.name == name);

        private static int FrustumCount(Camera camera, IEnumerable<Renderer> renderers)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return renderers.Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy &&
                                               GeometryUtility.TestPlanesAABB(planes, renderer.bounds));
        }

        private static string CenterRayHit(Camera camera)
        {
            return Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, camera.farClipPlane,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.collider.name + " @ " + hit.distance.ToString("F3") + "m"
                : "NONE";
        }

        private static void Render(Camera camera, string path)
        {
            const int width = 1280;
            const int height = 720;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(output);
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
