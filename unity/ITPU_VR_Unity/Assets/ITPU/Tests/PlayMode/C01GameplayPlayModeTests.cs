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
    public sealed class C01GameplayPlayModeTests
    {
        [UnityTest]
        public IEnumerator SavedSceneUsesOneAuthorityAndCompletesOnlyActualABA()
        {
            yield return LoadScene();
            var controller = Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var presenter = Object.FindObjectsByType<C01RoutePresenter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(true, true, true);
            yield return null;

            Assert.That(Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<C01RoutePresenter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(presenter.GetComponents<LineRenderer>(), Has.Length.EqualTo(1));
            Assert.That(presenter.VisiblePointCount, Is.Zero);
            Assert.That(GameObject.Find("C01_CHALLENGE_ROOT").GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(2), "C01 must retain exactly its two added controls");

            var show = Find("interaction.demo-route-button");
            var link = Find("interaction.c01-link-handler");
            var select = Find("interaction.c01-destination-select");
            Assert.That(show.TryActivate("playmode-no-handler"), Is.True);
            yield return null;
            Assert.That(controller.HandlerLinked, Is.False);
            Assert.That(controller.Completed, Is.False);
            Assert.That(presenter.VisiblePointCount, Is.Zero);
            Assert.That(controller.TraceSnapshot.Count(item => item.Event == "NoHandler"), Is.EqualTo(1));

            Assert.That(link.TryActivate("playmode-link"), Is.True);
            yield return null;
            Assert.That(controller.HandlerLinked, Is.True);
            Assert.That(presenter.VisiblePointCount, Is.Zero, "linking alone cannot present a route");

            Assert.That(show.TryActivate("playmode-kitchen-1"), Is.True);
            yield return null;
            Assert.That(controller.Stage, Is.EqualTo(C01Stage.FirstRoute));
            Assert.That(presenter.CurrentDestination, Is.EqualTo("KITCHEN"));
            var singleLine = presenter.RouteLine;

            Assert.That(select.TryActivate("playmode-select-niche"), Is.True);
            yield return null;
            Assert.That(controller.SelectedDestination, Is.EqualTo("NICHE_413"));
            Assert.That(show.TryActivate("playmode-niche"), Is.True);
            yield return null;
            Assert.That(controller.Stage, Is.EqualTo(C01Stage.SecondRoute));
            Assert.That(presenter.CurrentDestination, Is.EqualTo("NICHE_413"));

            Assert.That(select.TryActivate("playmode-select-kitchen"), Is.True);
            yield return null;
            Assert.That(show.TryActivate("playmode-kitchen-2"), Is.True);
            yield return null;
            Assert.That(controller.Completed, Is.True);
            Assert.That(controller.Stage, Is.EqualTo(C01Stage.Verified));
            Assert.That(controller.TraceSnapshot.Count(item => item.Event == "ShowRoute"), Is.EqualTo(3));
            Assert.That(presenter.RouteLine, Is.SameAs(singleLine), "routes must reuse one presenter");
            Assert.That(Object.FindObjectsByType<C01RoutePresenter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ResetIsSinglePerFrameClearsC01AndPreservesLifecyclePause()
        {
            yield return LoadScene();
            var controller = Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var reset = Object.FindObjectsByType<VerticalSliceReset>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(true, true, true);
            yield return null;

            var link = Find("interaction.c01-link-handler");
            var show = Find("interaction.demo-route-button");
            Assert.That(link.TryActivate("pre-reset-link"), Is.True);
            yield return null;
            Assert.That(show.TryActivate("pre-reset-route"), Is.True);
            yield return null;
            Assert.That(controller.Presenter.VisiblePointCount, Is.GreaterThan(0));
            var stale = controller.CurrentToken;
            var revision = reset.SessionRevision;
            reset.ResetSession(Time.unscaledTimeAsDouble);
            reset.ResetSession(Time.unscaledTimeAsDouble);
            Assert.That(reset.SessionRevision, Is.EqualTo(revision + 1),
                "two pointer callbacks in one frame must reset once");
            Assert.That(controller.CurrentToken.Equals(stale), Is.False);
            Assert.That(controller.HandlerLinked, Is.False);
            Assert.That(controller.Completed, Is.False);
            Assert.That(controller.Stage, Is.EqualTo(C01Stage.MissingHandler));
            Assert.That(controller.SelectedDestination, Is.EqualTo("KITCHEN"));
            Assert.That(controller.TraceSnapshot, Is.Empty);
            Assert.That(controller.Presenter.VisiblePointCount, Is.Zero);

            yield return null;
            lifecycle.SetPaused(true);
            yield return null;
            Assert.That(link.TryActivate("paused-link"), Is.True);
            yield return null;
            Assert.That(controller.HandlerLinked, Is.False, "lifecycle pause must block the domain");
            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            Assert.That(link.TryActivate("still-paused-link"), Is.True);
            yield return null;
            Assert.That(controller.HandlerLinked, Is.False, "reset must not clear lifecycle pause");

            lifecycle.SetPaused(false);
            yield return null;
            Assert.That(link.TryActivate("resumed-link"), Is.True);
            yield return null;
            Assert.That(controller.HandlerLinked, Is.True);
        }

        [UnityTest]
        public IEnumerator SavedSceneC01TextUsesBoundedUniformWorldScale()
        {
            yield return LoadScene();
            yield return null;

            var panel = GameObject.Find("C01_INFORMATION_PANEL").GetComponent<Renderer>();
            Assert.That(panel, Is.Not.Null);
            var status = GameObject.Find("C01_STATUS").GetComponent<TextMesh>();
            var trace = GameObject.Find("C01_TRACE").GetComponent<TextMesh>();
            var mapTitle = GameObject.Find("C01_MAP_TITLE").GetComponent<TextMesh>();
            var link = GameObject.Find("C01_LINK_HANDLER_BUTTON_LABEL").GetComponent<TextMesh>();
            var target = GameObject.Find("C01_DESTINATION_BUTTON_LABEL").GetComponent<TextMesh>();

            foreach (var label in new[] { status, trace, mapTitle, link, target })
            {
                Assert.That(label, Is.Not.Null);
                var scale = label.transform.lossyScale;
                Assert.That(Mathf.Abs(scale.x - scale.y), Is.LessThan(0.001f), label.name + " scale x/y");
                Assert.That(Mathf.Abs(scale.y - scale.z), Is.LessThan(0.001f), label.name + " scale y/z");
                Assert.That(label.GetComponent<Renderer>().bounds.size.x, Is.LessThan(panel.bounds.size.x),
                    label.name + " must fit panel width");
            }

            Assert.That(status.GetComponent<Renderer>().bounds.size.y, Is.LessThan(0.4f));
            Assert.That(trace.GetComponent<Renderer>().bounds.size.y, Is.LessThan(0.65f));
            Assert.That(link.GetComponent<Renderer>().bounds.Intersects(target.GetComponent<Renderer>().bounds),
                Is.False, "adjacent C01 button labels must not overlap");
        }

        private static DemoPressInteractable Find(string id) =>
            Object.FindObjectsByType<StableId>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(item => item.Value == id).GetComponent<DemoPressInteractable>();

        private static IEnumerator LoadScene()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
        }
    }
}
