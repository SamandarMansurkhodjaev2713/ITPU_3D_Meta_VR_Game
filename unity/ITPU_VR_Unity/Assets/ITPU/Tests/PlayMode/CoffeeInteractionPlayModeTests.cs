using System.Collections;
using System.Linq;
using ITPU.Domain;
using ITPU.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ITPU.XR.Tests.PlayMode
{
    public sealed class CoffeeInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator SavedScene_HasOneCoffeeSystemWithoutChangingEnvironmentContract()
        {
            yield return LoadScene();
            var environment = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Value == "environment.full.v005").gameObject;
            Assert.That(environment.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(626));
            Assert.That(environment.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(549));
            Assert.That(environment.GetComponentsInChildren<CapsuleCollider>(true), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CoffeeInteractionController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CoffeeCupGrabAdapter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            var cup = FindStable("K_CUP");
            Assert.That(cup.GetComponents<Rigidbody>(), Has.Length.EqualTo(1));
            Assert.That(cup.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(2));
            Assert.That(GameObject.Find("COFFEE_INTERACTION_WO27"), Is.Not.Null);
            Assert.That(GameObject.Find("COFFEE_BREW_BUTTON"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator CoffeeFlow_RejectsMissingCupPausesOnRemovalAndResets()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var controller = Object.FindFirstObjectByType<CoffeeInteractionController>(
                FindObjectsInactive.Include);
            var adapter = Object.FindFirstObjectByType<CoffeeCupGrabAdapter>(
                FindObjectsInactive.Include);
            var reset = Object.FindFirstObjectByType<VerticalSliceReset>(FindObjectsInactive.Include);
            Assert.That(controller, Is.Not.Null);
            Assert.That(adapter, Is.Not.Null);
            Assert.That(reset, Is.Not.Null);

            controller.ResetInteraction();
            adapter.ResetToHome();
            var homePosition = adapter.HomeAnchor.position;
            var homeRotation = adapter.HomeAnchor.rotation;
            Assert.That(controller.TryActivate(), Is.False);
            Assert.That(controller.State, Is.EqualTo(CoffeeState.Idle));
            Assert.That(controller.Fill, Is.Zero);

            Assert.That(adapter.SnapToDock(), Is.True);
            Assert.That(controller.State, Is.EqualTo(CoffeeState.CupReady));
            Assert.That(controller.TryActivate(), Is.True);
            controller.TickForTests(0.4f);
            var partial = controller.Fill;
            Assert.That(partial, Is.GreaterThan(0f));

            var pose = new Pose(adapter.transform.position, adapter.transform.rotation);
            Assert.That(adapter.TryBeginGrab("test-hand", pose), Is.True);
            Assert.That(controller.State, Is.EqualTo(CoffeeState.Paused));
            controller.TickForTests(0.5f);
            Assert.That(controller.Fill, Is.EqualTo(partial).Within(0.001f));
            Assert.That(adapter.MarkTrackingLost("test-hand"), Is.True);
            Assert.That(adapter.Held, Is.True, "tracking loss must not release or throw the cup");
            var away = new Pose(adapter.DockAnchor.position + Vector3.up * 0.45f,
                adapter.DockAnchor.rotation);
            Assert.That(adapter.UpdateGrab("test-hand", away), Is.True);
            Physics.SyncTransforms();
            Assert.That(adapter.TryRelease("test-hand", false, Vector3.one * 8f, Vector3.one * 8f),
                Is.True);
            Assert.That(adapter.Held, Is.False);
            Assert.That(adapter.Body.linearVelocity.sqrMagnitude, Is.Zero,
                "untracked release must not inherit throw velocity");

            adapter.Body.isKinematic = true;
            adapter.transform.SetPositionAndRotation(adapter.DockAnchor.position,
                adapter.DockAnchor.rotation);
            Assert.That(adapter.SnapToDock(), Is.True);
            Assert.That(controller.TryActivate(), Is.True);
            for (var index = 0; index < 30; index++) controller.TickForTests(0.25f);
            Assert.That(controller.State, Is.EqualTo(CoffeeState.Ready));
            Assert.That(controller.Fill, Is.EqualTo(1f).Within(0.001f));

            yield return null;
            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            Assert.That(controller.State, Is.EqualTo(CoffeeState.Idle));
            Assert.That(controller.Fill, Is.Zero);
            Assert.That(adapter.Held, Is.False);
            Assert.That(adapter.Docked, Is.False);
            Assert.That(Vector3.Distance(adapter.transform.position, homePosition), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(adapter.transform.rotation, homeRotation), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator ControllerGrip_GrabsCupAndBlocksTeleportPriority()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var adapter = Object.FindFirstObjectByType<CoffeeCupGrabAdapter>(FindObjectsInactive.Include);
            var pointer = Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).First();
            pointer.enabled = false;
            var priority = Object.FindFirstObjectByType<InteractionPriority>(FindObjectsInactive.Include);
            var collider = adapter.GetComponentInChildren<Collider>();
            adapter.ResetToHome();
            Physics.SyncTransforms();
            var ray = new Ray(new Vector3(collider.bounds.max.x + 0.01f, collider.bounds.center.y,
                collider.bounds.center.z), Vector3.left);
            Assert.That(Physics.Raycast(ray, out var cupHit, 0.2f, Physics.AllLayers,
                QueryTriggerInteraction.Collide), Is.True);
            Assert.That(cupHit.collider.GetComponentInParent<CoffeeCupGrabAdapter>(), Is.SameAs(adapter));
            var now = Time.unscaledTimeAsDouble;
            pointer.ProcessSample(true, false, false, ray, now);
            pointer.ProcessSample(true, false, true, ray, now + 0.1);
            Assert.That(adapter.Owner, Is.EqualTo(pointer.Source));
            Assert.That(priority.HoldingObject, Is.True);
            pointer.ProcessSample(true, false, false, ray, now + 0.2);
            Assert.That(adapter.Held, Is.False);
            Assert.That(priority.HoldingObject, Is.False);
        }

        private static IEnumerator LoadScene()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Physics.SyncTransforms();
        }

        private static Transform FindStable(string value) =>
            Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Value == value).transform;

        private static void MakeLifecycleSafe()
        {
            var lifecycle = Object.FindFirstObjectByType<XrLifecycleState>(FindObjectsInactive.Include);
            lifecycle.SetDevicePolling(false);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(true, true, true);
        }
    }
}
