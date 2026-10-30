using System.Collections;
using System.Linq;
using ITPU.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Unity.XR.CoreUtils;

namespace ITPU.XR.Tests.PlayMode
{
    public sealed class C02GameplayPlayModeTests
    {
        [UnityTest]
        public IEnumerator SavedSceneUsesSharedAuthorityAndCompletesVisibleWaitResume()
        {
            yield return LoadReadyScene();
            var c02 = Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var presenter = Object.FindObjectsByType<C02GuidePresenter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            Assert.That(Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(GameObject.Find("C02_CHALLENGE_ROOT").GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(3));

            var step = Find("interaction.c02-step");
            Assert.That(step.TryActivate("locked-step"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.BrokenReady));
            Assert.That(c02.Completed, Is.False);

            yield return CompleteC01();
            Assert.That(c02.PrerequisiteMet, Is.True);
            var origin = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single().transform;
            var originPosition = origin.position;
            var originRotation = origin.rotation;
            var startPosition = presenter.GuidePosition;

            Assert.That(step.TryActivate("broken-run"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.BrokenGuideLeft));
            Assert.That(presenter.GuidePosition, Is.Not.EqualTo(startPosition));
            Assert.That(c02.TraceSnapshot.Count(item => item.Event == "GuideLeftWithoutGuest"), Is.EqualTo(1));

            Assert.That(Find("interaction.c02-add-wait").TryActivate("install-wait"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.WaitRuleInstalled));
            Assert.That(presenter.GuidePosition, Is.EqualTo(startPosition));
            Assert.That(step.TryActivate("fixed-run"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.WaitingForGuest));
            Assert.That(presenter.GuidePosition, Is.EqualTo(startPosition));
            Assert.That(step.TryActivate("prove-hold"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.WaitingForGuest));
            Assert.That(presenter.GuidePosition, Is.EqualTo(startPosition));

            Assert.That(Find("interaction.c02-guest-arrived").TryActivate("guest-arrived"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.GuestConfirmed));
            Assert.That(presenter.GuestVisible, Is.True);
            Assert.That(step.TryActivate("resume"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.Verified));
            Assert.That(c02.Completed, Is.True);
            Assert.That(origin.position, Is.EqualTo(originPosition));
            Assert.That(origin.rotation, Is.EqualTo(originRotation));
        }

        [UnityTest]
        public IEnumerator PauseTrackingResetAndTeleportRespectCurrentSession()
        {
            yield return LoadReadyScene();
            yield return CompleteC01();
            var c02 = Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var authority = Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var reset = Object.FindObjectsByType<VerticalSliceReset>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var step = Find("interaction.c02-step");
            Assert.That(step.TryActivate("broken-run"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-add-wait").TryActivate("install-wait"), Is.True);
            yield return null;

            lifecycle.SetPaused(true);
            yield return null;
            Assert.That(step.TryActivate("paused-step"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.WaitRuleInstalled));
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(false, true, true);
            yield return null;
            Assert.That(step.TryActivate("untracked-step"), Is.True);
            yield return null;
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.WaitRuleInstalled));
            Assert.That(c02.ApplyConfirmedTeleport(C02ChallengeController.Checkpoint), Is.False);
            lifecycle.SetTracking(true, true, true);
            yield return null;
            Assert.That(c02.ApplyConfirmedTeleport(C02ChallengeController.Checkpoint), Is.True);
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.WaitingForGuest));

            var stale = c02.CurrentToken;
            var resetRevision = reset.SessionRevision;
            var authorityRevision = authority.ResetRevision;
            reset.ResetSession(Time.unscaledTimeAsDouble);
            reset.ResetSession(Time.unscaledTimeAsDouble);
            Assert.That(reset.SessionRevision, Is.EqualTo(resetRevision + 1));
            Assert.That(authority.ResetRevision, Is.EqualTo(authorityRevision + 1));
            Assert.That(c02.CurrentToken.Equals(stale), Is.False);
            Assert.That(c02.Stage, Is.EqualTo(C02Stage.BrokenReady));
            Assert.That(c02.WaitRuleInstalled, Is.False);
            Assert.That(c02.Completed, Is.False);
            Assert.That(c02.TraceSnapshot, Is.Empty);
            Assert.That(c02.PrerequisiteMet, Is.False);
        }

        [UnityTest]
        public IEnumerator C02PanelTextAndButtonsRemainBounded()
        {
            yield return LoadReadyScene();
            var panel = GameObject.Find("C02_INFORMATION_PANEL").GetComponent<Renderer>();
            var title = GameObject.Find("C02_TITLE").GetComponent<TextMesh>();
            var status = GameObject.Find("C02_STATUS").GetComponent<TextMesh>();
            var trace = GameObject.Find("C02_TRACE").GetComponent<TextMesh>();
            var labels = new[]
            {
                GameObject.Find("C02_STEP_BUTTON_LABEL").GetComponent<TextMesh>(),
                GameObject.Find("C02_ADD_WAIT_BUTTON_LABEL").GetComponent<TextMesh>(),
                GameObject.Find("C02_GUEST_BUTTON_LABEL").GetComponent<TextMesh>(),
            };
            foreach (var label in new[] { title, status, trace }.Concat(labels))
            {
                var scale = label.transform.lossyScale;
                Assert.That(Mathf.Abs(scale.x - scale.y), Is.LessThan(0.001f), label.name + " x/y scale");
                Assert.That(Mathf.Abs(scale.y - scale.z), Is.LessThan(0.001f), label.name + " y/z scale");
                Assert.That(label.GetComponent<Renderer>().bounds.size.x, Is.LessThan(panel.bounds.size.x),
                    label.name + " must fit C02 panel width");
            }
            Assert.That(labels[0].GetComponent<Renderer>().bounds.Intersects(labels[1].GetComponent<Renderer>().bounds),
                Is.False);
            Assert.That(labels[1].GetComponent<Renderer>().bounds.Intersects(labels[2].GetComponent<Renderer>().bounds),
                Is.False);
        }

        private static IEnumerator CompleteC01()
        {
            Assert.That(Find("interaction.c01-link-handler").TryActivate("c02-prereq-link"), Is.True);
            yield return null;
            Assert.That(Find("interaction.demo-route-button").TryActivate("c02-prereq-kitchen-a"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c01-destination-select").TryActivate("c02-prereq-niche"), Is.True);
            yield return null;
            Assert.That(Find("interaction.demo-route-button").TryActivate("c02-prereq-show-niche"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c01-destination-select").TryActivate("c02-prereq-kitchen-b"), Is.True);
            yield return null;
            Assert.That(Find("interaction.demo-route-button").TryActivate("c02-prereq-show-kitchen"), Is.True);
            yield return null;
            Assert.That(Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single().Completed, Is.True);
        }

        private static DemoPressInteractable Find(string id) =>
            Object.FindObjectsByType<StableId>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(item => item.Value == id).GetComponent<DemoPressInteractable>();

        private static IEnumerator LoadReadyScene()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(true, true, true);
            yield return null;
        }
    }
}
