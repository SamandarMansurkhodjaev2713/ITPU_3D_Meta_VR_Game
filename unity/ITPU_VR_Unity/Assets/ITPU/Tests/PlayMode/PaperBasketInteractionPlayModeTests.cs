using System.Collections;
using System.Linq;
using ITPU.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ITPU.XR.Tests.PlayMode
{
    public sealed class PaperBasketInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator SavedScene_HasFixedPoolOpenBasketAndPreservesAcceptedSystems()
        {
            yield return LoadScene();
            var environment = FindStable("environment.full.v005").gameObject;
            Assert.That(environment.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(626));
            Assert.That(environment.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(549));
            Assert.That(environment.GetComponentsInChildren<CapsuleCollider>(true), Has.Length.EqualTo(1));
            var bin = FindStable("BIN_403");
            var binMeshes = bin.GetComponentsInChildren<MeshCollider>(true);
            Assert.That(binMeshes, Has.Length.EqualTo(1));
            Assert.That(binMeshes[0].convex, Is.False);
            Assert.That(binMeshes[0].isTrigger, Is.False);
            var papers = Papers();
            Assert.That(papers, Has.Length.EqualTo(3));
            Assert.That(papers.Select(item => item.GetComponent<Rigidbody>()).Distinct().Count(), Is.EqualTo(3));
            Assert.That(papers.All(item => item.State == PaperPropState.Sheet), Is.True);
            var sensors = Object.FindObjectsByType<PaperBasketSensor>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(sensors, Has.Length.EqualTo(2));
            Assert.That(sensors.Select(item => item.Kind).Distinct().Count(), Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CoffeeInteractionController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<BoardDrawingController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExplicitCrumpleGrabTrackingLossAndReset_AreDeterministic()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var paper = Papers()[0];
            var priority = Object.FindFirstObjectByType<InteractionPriority>(FindObjectsInactive.Include);
            var reset = Object.FindFirstObjectByType<VerticalSliceReset>(FindObjectsInactive.Include);
            var homePosition = paper.HomeAnchor.position;
            Assert.That(paper.TryBeginGrab("source-a", new Pose(paper.transform.position,
                Quaternion.identity)), Is.False, "sheet must be crumpled explicitly before grab");
            Assert.That(paper.TryPrimaryActivate("source-a-trigger"), Is.True);
            Assert.That(paper.State, Is.EqualTo(PaperPropState.Crumpled));
            var pose = new Pose(paper.transform.position + Vector3.up * 0.1f, Quaternion.identity);
            Assert.That(paper.TryBeginGrab("source-a", pose), Is.True);
            Assert.That(paper.TryBeginGrab("source-b", pose), Is.False);
            Assert.That(priority.HoldingObject, Is.True);
            Assert.That(paper.MarkTrackingLost("source-a"), Is.True);
            Assert.That(paper.Held, Is.True);
            Assert.That(paper.TrackingValid, Is.False);
            Assert.That(paper.TryReturnToPool(), Is.False);
            Assert.That(paper.TryRelease("source-a", true, Vector3.down * 4f, Vector3.one * 8f), Is.True);
            Assert.That(paper.LastReleaseTracked, Is.False);
            Assert.That(paper.Body.linearVelocity.sqrMagnitude, Is.Zero);
            Assert.That(priority.HoldingObject, Is.False);

            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            Assert.That(paper.State, Is.EqualTo(PaperPropState.Sheet));
            Assert.That(paper.Held, Is.False);
            Assert.That(Vector3.Distance(paper.transform.position, homePosition), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator BasketScoring_RejectsInvalidEntryAndScoresOnceAfterTrackedDwell()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var papers = Papers();
            var valid = papers[0];
            PrepareTrackedRelease(valid, "valid", Vector3.down * 1.2f);
            Assert.That(valid.NotifyBasketMouth(true), Is.True);
            Assert.That(valid.TickBasketInside(true, 0.1f), Is.False);
            Assert.That(valid.TickBasketInside(true, 0.05f), Is.True);
            Assert.That(valid.State, Is.EqualTo(PaperPropState.Scored));
            Assert.That(valid.ScoreCount, Is.EqualTo(1));
            Assert.That(valid.TickBasketInside(true, 1f), Is.False);
            Assert.That(valid.TryReturnToPool(), Is.True);
            Assert.That(valid.State, Is.EqualTo(PaperPropState.Sheet));
            Assert.That(valid.ScoreCount, Is.EqualTo(1));

            var upward = papers[1];
            PrepareTrackedRelease(upward, "upward", Vector3.up);
            Assert.That(upward.NotifyBasketMouth(false), Is.False);
            Assert.That(upward.TickBasketInside(true, 1f), Is.False);
            Assert.That(upward.ScoreCount, Is.Zero);

            var untracked = papers[2];
            Assert.That(untracked.TryPrimaryActivate("untracked-trigger"), Is.True);
            Assert.That(untracked.TryBeginGrab("untracked", new Pose(untracked.transform.position,
                Quaternion.identity)), Is.True);
            Assert.That(untracked.MarkTrackingLost("untracked"), Is.True);
            Assert.That(untracked.TryRelease("untracked", true, Vector3.down, Vector3.zero), Is.True);
            Assert.That(untracked.NotifyBasketMouth(true), Is.False);
            Assert.That(untracked.TickBasketInside(true, 1f), Is.False);
            Assert.That(untracked.ScoreCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MultipleOwnersAndControllerHandPaths_ShareOnePriorityAndOutrankTeleport()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var papers = Papers();
            var priority = Object.FindFirstObjectByType<InteractionPriority>(FindObjectsInactive.Include);
            var teleport = Object.FindFirstObjectByType<HandTeleportIntentAdapter>(FindObjectsInactive.Include);
            for (var index = 0; index < 2; index++)
            {
                Assert.That(papers[index].TryPrimaryActivate("prepare-" + index), Is.True);
                Assert.That(papers[index].TryBeginGrab("owner-" + index,
                    new Pose(papers[index].transform.position, Quaternion.identity)), Is.True);
            }
            Assert.That(priority.HoldingObject, Is.True);
            Assert.That(priority.HoldingSourceCount, Is.EqualTo(2));
            Assert.That(papers[0].TryRelease("owner-0", false, Vector3.zero, Vector3.zero), Is.True);
            Assert.That(priority.HoldingObject, Is.True);
            Assert.That(priority.HoldingSourceCount, Is.EqualTo(1));
            Assert.That(papers[1].TryRelease("owner-1", false, Vector3.zero, Vector3.zero), Is.True);
            Assert.That(priority.HoldingObject, Is.False);

            var reset = Object.FindFirstObjectByType<VerticalSliceReset>(FindObjectsInactive.Include);
            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            var controller = Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).First();
            controller.enabled = false;
            var now = Time.unscaledTimeAsDouble;
            var controllerRay = RayAt(papers[0]);
            controller.ProcessSample(true, false, false, controllerRay, now);
            controller.ProcessSample(true, true, false, controllerRay, now + 0.05);
            Assert.That(papers[0].State, Is.EqualTo(PaperPropState.Crumpled));
            controller.ProcessSample(true, false, false, RayAt(papers[0]), now + 0.1);
            controller.ProcessSample(true, false, false, RayAt(papers[0]), now + 0.15);
            controller.ProcessSample(true, false, true, RayAt(papers[0]), now + 0.2);
            Assert.That(papers[0].Owner, Is.EqualTo(controller.Source));
            Assert.That(teleport.OwnerSource, Is.Empty);
            controller.ProcessSample(true, false, false, RayAt(papers[0]), now + 0.25);

            var hand = Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).First();
            hand.enabled = false;
            var handRay = RayAt(papers[1]);
            hand.ProcessSample(true, false, false, handRay, now + 0.3);
            hand.ProcessSample(true, false, true, handRay, now + 0.35);
            Assert.That(papers[1].State, Is.EqualTo(PaperPropState.Crumpled));
            hand.ProcessSample(true, false, false, RayAt(papers[1]), now + 0.4);
            hand.ProcessSample(true, false, false, RayAt(papers[1]), now + 0.45);
            hand.ProcessSample(true, false, true, RayAt(papers[1]), now + 0.5);
            Assert.That(papers[1].Owner, Is.EqualTo(hand.Source));
            Assert.That(teleport.OwnerSource, Is.Empty);
            hand.ProcessSample(true, false, false, RayAt(papers[1]), now + 0.55);
            Assert.That(priority.HoldingObject, Is.False);
        }

        private static void PrepareTrackedRelease(PaperPropGrabAdapter paper, string source,
            Vector3 velocity)
        {
            Assert.That(paper.TryPrimaryActivate(source + "-trigger"), Is.True);
            Assert.That(paper.TryBeginGrab(source, new Pose(paper.transform.position,
                Quaternion.identity)), Is.True);
            Assert.That(paper.TryRelease(source, true, velocity, Vector3.zero), Is.True);
            Assert.That(paper.LastReleaseTracked, Is.True);
        }

        private static Ray RayAt(PaperPropGrabAdapter paper)
        {
            Physics.SyncTransforms();
            var collider = paper.GetComponents<Collider>().Single(item => item.enabled);
            return new Ray(collider.bounds.center + Vector3.up * 0.25f, Vector3.down);
        }

        private static PaperPropGrabAdapter[] Papers() => Object
            .FindObjectsByType<PaperPropGrabAdapter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).OrderBy(item => item.name).ToArray();

        private static IEnumerator LoadScene()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Physics.SyncTransforms();
        }

        private static Transform FindStable(string value) => Object
            .FindObjectsByType<StableId>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(item => item.Value == value).transform;

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
