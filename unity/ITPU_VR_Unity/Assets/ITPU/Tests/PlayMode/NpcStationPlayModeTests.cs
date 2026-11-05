using System.Collections;
using System.Linq;
using ITPU.Domain;
using ITPU.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ITPU.Tests.PlayMode
{
    public sealed class NpcStationPlayModeTests
    {
        [UnityTest]
        public IEnumerator Controller_EnforcesAnchorAndLifecycleNeutralState()
        {
            var root = new GameObject("NPC_TEST_ROOT");
            root.transform.SetPositionAndRotation(new Vector3(2f, 0.4f, -3f), Quaternion.Euler(0f, 35f, 0f));
            var chest = new GameObject("chest_aim").transform;
            chest.SetParent(root.transform, false);
            var head = new GameObject("head_aim").transform;
            head.SetParent(root.transform, false);
            var lifecycle = new GameObject("LIFECYCLE").AddComponent<XrLifecycleState>();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetTracking(true, true, true);
            var controller = root.AddComponent<NpcStationController>();
            controller.Configure(null, chest, head, null, null, lifecycle);
            var anchor = controller.AnchorPosition;
            var rotation = controller.AnchorRotation;

            for (var index = 0; index < 10; index++) controller.SimulateLook(90d, 0.1d);
            Assert.That(controller.BodyYaw, Is.EqualTo(25d).Within(0.001d));
            Assert.That(controller.HeadYaw, Is.EqualTo(35d).Within(0.001d));
            Assert.That(controller.RequestGesture(NpcGesture.Greet), Is.True);
            root.transform.position += Vector3.one * 5f;
            root.transform.rotation = Quaternion.identity;
            controller.EnforceAnchor();

            Assert.That(Vector3.Distance(root.transform.position, anchor), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(root.transform.rotation, rotation), Is.LessThan(0.001f));
            Assert.That(controller.RootMotionDisabled, Is.True);

            lifecycle.SetTracking(false, true, true);
            Assert.That(controller.Gesture, Is.EqualTo(NpcGesture.Idle));
            Assert.That(controller.BodyYaw, Is.Zero);
            Assert.That(controller.HeadYaw, Is.Zero);
            Assert.That(controller.RequestGesture(NpcGesture.Explain), Is.False);

            Object.Destroy(root);
            Object.Destroy(lifecycle.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SessionReset_ReturnsStationToImmutableAnchor()
        {
            var reset = new GameObject("RESET").AddComponent<VerticalSliceReset>();
            var lifecycle = new GameObject("LIFECYCLE").AddComponent<XrLifecycleState>();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetTracking(true, true, true);
            var authority = new GameObject("AUTHORITY").AddComponent<ChallengeSessionAuthority>();
            authority.Configure(reset, lifecycle);
            var root = new GameObject("NPC_TEST_ROOT");
            root.transform.position = new Vector3(-1f, 0.2f, 2f);
            var controller = root.AddComponent<NpcStationController>();
            controller.Configure(null, null, null, null, authority, lifecycle);
            var anchor = controller.AnchorPosition;
            controller.SimulateLook(-80d, 1d);
            Assert.That(controller.RequestGesture(NpcGesture.Point), Is.True);
            root.transform.position = new Vector3(50f, -20f, 12f);

            reset.ResetSession(Time.unscaledTimeAsDouble);

            Assert.That(Vector3.Distance(root.transform.position, anchor), Is.LessThan(0.0001f));
            Assert.That(controller.Gesture, Is.EqualTo(NpcGesture.Idle));
            Assert.That(controller.BodyYaw, Is.Zero);
            Assert.That(controller.HeadYaw, Is.Zero);
            Assert.That(authority.ResetRevision, Is.EqualTo(1));

            Object.Destroy(root);
            Object.Destroy(authority.gameObject);
            Object.Destroy(lifecycle.gameObject);
            Object.Destroy(reset.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SavedScene_HasExactNpc01StationContract()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            yield return null;

            var station = Object.FindObjectsByType<NpcStationController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            Assert.That(station.gameObject.name, Is.EqualTo("NPC01_SAMANDAR_STATION"));
            Assert.That(station.GetComponent<StableId>().Value,
                Is.EqualTo("npc.station.NPC01.production_021"));
            Assert.That(Vector3.Distance(station.transform.localScale, Vector3.one), Is.LessThan(0.0001f));
            Assert.That(station.RootMotionDisabled, Is.True);
            Assert.That(station.ExpressionBindingCount, Is.GreaterThanOrEqualTo(5));
            Assert.That(station.GetComponentsInChildren<Component>(true)
                .Any(item => item != null && item.GetType().Name == "NavMeshAgent"), Is.False);

            var animator = station.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False);
            foreach (var required in new[] { "NPC01_Idle", "NPC01_Greet", "NPC01_Explain", "NPC01_PointNear" })
                Assert.That(animator.HasState(0, Animator.StringToHash(required)), Is.True);

            var renderers = station.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers.SelectMany(item => item.sharedMaterials).Where(item => item != null)
                .All(item => item.shader.name == "Universal Render Pipeline/Lit"), Is.True);
            var initialized = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var baked = new Mesh();
                renderer.BakeMesh(baked);
                foreach (var vertex in baked.vertices)
                {
                    var world = renderer.transform.TransformPoint(vertex);
                    if (!initialized)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        initialized = true;
                    }
                    else bounds.Encapsulate(world);
                }
                Object.Destroy(baked);
            }
            Assert.That(initialized, Is.True);
            Assert.That(bounds.size.y, Is.InRange(1.65f, 1.90f));
            Assert.That(Mathf.Abs(bounds.min.y - station.transform.position.y), Is.LessThan(0.04f));
        }
    }
}
