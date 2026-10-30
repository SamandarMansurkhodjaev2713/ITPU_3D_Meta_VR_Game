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
    public sealed class C03GameplayPlayModeTests
    {
        [UnityTest]
        public IEnumerator FixedButtonsProveBypassThenExactAuthorizationMatrix()
        {
            yield return LoadReadyScene();
            var c03 = Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var origin = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single().transform;
            var originPosition = origin.position;
            var originRotation = origin.rotation;
            Assert.That(Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<ChallengeSessionAuthority>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(GameObject.Find("C03_CHALLENGE_ROOT").GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(5));

            Assert.That(Find("interaction.c03-bypass").TryActivate("locked"), Is.True);
            yield return null;
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.BaselineReady));
            yield return CompleteC01AndC02();
            Assert.That(c03.PrerequisiteMet, Is.True);

            Assert.That(Find("interaction.c03-bypass").TryActivate("baseline"), Is.True);
            yield return null;
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.BaselineBypassProven));
            Assert.That(GameObject.Find("C03_STATUS").GetComponent<TextMesh>().text, Does.Contain("0 → 1"));
            Assert.That(Find("interaction.c03-install-gate").TryActivate("gate"), Is.True);
            yield return null;
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.GateInstalled));
            Assert.That(Find("interaction.c03-auth01").TryActivate("auth01"), Is.True);
            yield return null;
            Assert.That(GameObject.Find("C03_STATUS").GetComponent<TextMesh>().text,
                Does.Contain("ALLOW  DATA 0 → 0"));
            Assert.That(Find("interaction.c03-auth02").TryActivate("auth02"), Is.True);
            yield return null;
            Assert.That(GameObject.Find("C03_STATUS").GetComponent<TextMesh>().text,
                Does.Contain("DENY  DATA 0 → 0"));
            Assert.That(Find("interaction.c03-auth03").TryActivate("auth03"), Is.True);
            yield return null;
            Assert.That(c03.Completed, Is.True);
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.Verified));
            Assert.That(GameObject.Find("C03_STATUS").GetComponent<TextMesh>().text, Does.Contain("C03 PASS"));
            Assert.That(origin.position, Is.EqualTo(originPosition));
            Assert.That(origin.rotation, Is.EqualTo(originRotation));
        }

        [UnityTest]
        public IEnumerator PauseTrackingAndResetUseSharedSessionAuthority()
        {
            yield return LoadReadyScene();
            yield return CompleteC01AndC02();
            var c03 = Object.FindObjectsByType<C03ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var reset = Object.FindObjectsByType<VerticalSliceReset>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var bypass = Find("interaction.c03-bypass");
            lifecycle.SetPaused(true);
            yield return null;
            Assert.That(bypass.TryActivate("paused"), Is.True);
            yield return null;
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.BaselineReady));
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(false, true, true);
            yield return null;
            Assert.That(bypass.TryActivate("untracked"), Is.True);
            yield return null;
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.BaselineReady));
            lifecycle.SetTracking(true, true, true);
            yield return null;
            Assert.That(bypass.TryActivate("tracked"), Is.True);
            yield return null;
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.BaselineBypassProven));
            var stale = c03.CurrentToken;
            var oldRevision = c03.PolicyRevision;
            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            Assert.That(c03.CurrentToken.Equals(stale), Is.False);
            Assert.That(c03.PolicyRevision, Is.GreaterThan(oldRevision));
            Assert.That(c03.Stage, Is.EqualTo(C03Stage.BaselineReady));
            Assert.That(c03.GateInstalled, Is.False);
            Assert.That(c03.Completed, Is.False);
            Assert.That(c03.PrerequisiteMet, Is.False);
            Assert.That(c03.TraceSnapshot, Is.Empty);
            Assert.That(Object.FindObjectsByType<C01ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single().Stage, Is.EqualTo(C01Stage.MissingHandler));
            Assert.That(Object.FindObjectsByType<C02ChallengeController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single().Stage, Is.EqualTo(C02Stage.BrokenReady));
        }

        [UnityTest]
        public IEnumerator C03PanelTextAndFixedButtonsRemainBounded()
        {
            yield return LoadReadyScene();
            var panel = GameObject.Find("C03_INFORMATION_PANEL").GetComponent<Renderer>();
            var labels = GameObject.Find("C03_CHALLENGE_ROOT").GetComponentsInChildren<TextMesh>(true);
            foreach (var label in labels)
            {
                var scale = label.transform.lossyScale;
                Assert.That(Mathf.Abs(scale.x - scale.y), Is.LessThan(0.001f), label.name + " x/y scale");
                Assert.That(Mathf.Abs(scale.y - scale.z), Is.LessThan(0.001f), label.name + " y/z scale");
                Assert.That(label.GetComponent<Renderer>().bounds.size.x,
                    Is.LessThan(panel.bounds.size.x), label.name + " must fit C03 panel width");
            }
            var buttons = GameObject.Find("C03_CHALLENGE_ROOT")
                .GetComponentsInChildren<DemoPressInteractable>(true)
                .Select(item => item.GetComponent<Renderer>().bounds).ToArray();
            for (var i = 0; i < buttons.Length; i++)
                for (var j = i + 1; j < buttons.Length; j++)
                    Assert.That(buttons[i].Intersects(buttons[j]), Is.False, "C03 buttons overlap");

            var floorColliders = new[] { "FLOOR_0_2__COLLIDER_FRAME", "FLOOR_0_3__COLLIDER_FRAME" }
                .Select(GameObject.Find).SelectMany(item => item.GetComponentsInChildren<Collider>(true)).ToArray();
            Assert.That(floorColliders, Has.Length.EqualTo(2));
            var floorBounds = floorColliders[0].bounds;
            floorBounds.Encapsulate(floorColliders[1].bounds);
            Assert.That(panel.bounds.min.x, Is.GreaterThan(floorBounds.min.x));
            Assert.That(panel.bounds.max.x, Is.LessThan(floorBounds.max.x));
            Assert.That(panel.bounds.min.z, Is.GreaterThan(floorBounds.min.z));
            Assert.That(panel.bounds.max.z, Is.LessThan(floorBounds.max.z));

            var backWall = GameObject.Find("WOOD_WALL_403__COLLIDER_FRAME")
                .GetComponentInChildren<Collider>(true);
            Assert.That(panel.bounds.min.z, Is.GreaterThan(backWall.bounds.max.z),
                "C03 panel must remain on the walkable side of the 403 wood wall.");
            var controlColliders = GameObject.Find("C03_CHALLENGE_ROOT")
                .GetComponentsInChildren<DemoPressInteractable>(true)
                .Select(item => item.GetComponent<Collider>()).ToArray();
            var wallColliders = new[]
                {
                    "WALL_0__COLLIDER_FRAME", "WALL_1__COLLIDER_FRAME", "WALL_2__COLLIDER_FRAME",
                    "WALL_3__COLLIDER_FRAME", "WOOD_WALL_403__COLLIDER_FRAME",
                }
                .Select(GameObject.Find)
                .SelectMany(item => item.GetComponentsInChildren<Collider>(true)).ToArray();
            Assert.That(controlColliders, Has.Length.EqualTo(5));
            foreach (var control in controlColliders)
            {
                Assert.That(control.bounds.min.x, Is.GreaterThan(floorBounds.min.x), control.name);
                Assert.That(control.bounds.max.x, Is.LessThan(floorBounds.max.x), control.name);
                Assert.That(control.bounds.min.z, Is.GreaterThan(floorBounds.min.z), control.name);
                Assert.That(control.bounds.max.z, Is.LessThan(floorBounds.max.z), control.name);
                Assert.That(wallColliders.Any(wall => control.bounds.Intersects(wall.bounds)), Is.False,
                    control.name + " must not intersect any accepted 403 wall collider.");
            }
        }

        private static IEnumerator CompleteC01AndC02()
        {
            Assert.That(Find("interaction.c01-link-handler").TryActivate("c03-link"), Is.True);
            yield return null;
            Assert.That(Find("interaction.demo-route-button").TryActivate("c03-kitchen-a"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c01-destination-select").TryActivate("c03-niche"), Is.True);
            yield return null;
            Assert.That(Find("interaction.demo-route-button").TryActivate("c03-show-niche"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c01-destination-select").TryActivate("c03-kitchen-b"), Is.True);
            yield return null;
            Assert.That(Find("interaction.demo-route-button").TryActivate("c03-show-kitchen"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-step").TryActivate("c03-broken"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-add-wait").TryActivate("c03-wait"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-step").TryActivate("c03-hold-a"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-step").TryActivate("c03-hold-b"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-guest-arrived").TryActivate("c03-guest"), Is.True);
            yield return null;
            Assert.That(Find("interaction.c02-step").TryActivate("c03-resume"), Is.True);
            yield return null;
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
