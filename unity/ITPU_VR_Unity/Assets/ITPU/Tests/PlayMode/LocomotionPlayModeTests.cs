using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.Rendering;

namespace ITPU.XR.Tests.PlayMode
{
    public sealed class LocomotionPlayModeTests
    {
        private GameObject rig;
        private GameObject floor;
        private XrLifecycleState lifecycle;
        private InteractionPriority priority;
        private XrLocomotionDriver locomotion;
        private Camera camera;

        [SetUp]
        public void SetUp()
        {
            rig = TestRig.Create(out lifecycle, out priority, out locomotion, out camera);
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "TEST_FLOOR";
            floor.transform.position = new Vector3(0, -0.05f, 0);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (rig != null) Object.DestroyImmediate(rig);
            var testRoots = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(item => item != null && item.name.StartsWith("TEST_") &&
                               (item.transform.parent == null ||
                                !item.transform.parent.name.StartsWith("TEST_")))
                .ToArray();
            foreach (var item in testRoots)
                if (item != null) Object.DestroyImmediate(item);
        }

        [UnityTest]
        public IEnumerator ControllerMove_UsesCharacterControllerAndNeutralRearm()
        {
            const double now = 1.0;
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now), Is.False);
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.02), Is.True);
            var moved = rig.transform.position;
            Assert.That(moved.z, Is.GreaterThan(0));

            lifecycle.SetFocus(false);
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.04), Is.False);
            lifecycle.SetFocus(true);
            var blockedPosition = rig.transform.position;
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.06), Is.False);
            Assert.That(rig.transform.position, Is.EqualTo(blockedPosition));
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now + 0.08), Is.False);
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.10), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SnapTurn_IsThirtyDegreesAndDoesNotTranslateCamera()
        {
            const double now = 1.0;
            locomotion.ProcessInput(Vector2.zero, 0, 0.02, now);
            var cameraPosition = camera.transform.position;
            Assert.That(locomotion.ProcessInput(Vector2.zero, 1, 0.02, now + 0.02), Is.True);
            Assert.That(Mathf.DeltaAngle(0f, rig.transform.eulerAngles.y), Is.EqualTo(30f).Within(0.05f));
            Assert.That(Vector3.Distance(cameraPosition, camera.transform.position), Is.LessThan(0.001f));
            for (var index = 0; index < 30; index++)
                Assert.That(locomotion.ProcessInput(Vector2.zero, 1, 0.02,
                    now + 0.04 + index * 0.02), Is.False,
                    "holding the right stick must not repeat after cooldown");
            Assert.That(locomotion.AppliedTurnCount, Is.EqualTo(1));
            Assert.That(Mathf.DeltaAngle(0f, rig.transform.eulerAngles.y), Is.EqualTo(30f).Within(0.05f));
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now + 0.66), Is.False);
            Assert.That(locomotion.ProcessInput(Vector2.zero, -1, 0.02, now + 0.94), Is.True);
            Assert.That(locomotion.AppliedTurnCount, Is.EqualTo(2));
            Assert.That(Mathf.DeltaAngle(0f, rig.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.05f));
            Assert.That(Vector3.Distance(cameraPosition, camera.transform.position), Is.LessThan(0.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SmoothTurn_IsOptInHeadPivotedAndStopsAtNeutral()
        {
            const double now = 2.0;
            Assert.That(locomotion.SmoothTurnEnabled, Is.False,
                "snap 30 must remain the fresh-session default");
            locomotion.ProcessInput(Vector2.zero, 0, 0.02, now);
            locomotion.SetSmoothTurnEnabled(true);
            Assert.That(locomotion.SmoothTurnEnabled, Is.True);
            Assert.That(locomotion.ProcessInput(Vector2.zero, 1, 0.02, now + 0.02), Is.False,
                "mode change must reject a previously held stick");
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now + 0.04), Is.False);

            var cameraPosition = camera.transform.position;
            for (var index = 0; index < 50; index++)
                Assert.That(locomotion.ProcessInput(Vector2.zero, 1, 0.02,
                    now + 0.06 + index * 0.02), Is.True);
            Assert.That(Mathf.DeltaAngle(0f, rig.transform.eulerAngles.y), Is.EqualTo(45f).Within(0.1f));
            Assert.That(Vector3.Distance(cameraPosition, camera.transform.position), Is.LessThan(0.001f));
            Assert.That(locomotion.AppliedTurnCount, Is.EqualTo(50));

            var stoppedYaw = rig.transform.eulerAngles.y;
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now + 1.08), Is.False);
            Assert.That(Mathf.DeltaAngle(stoppedYaw, rig.transform.eulerAngles.y), Is.Zero.Within(0.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TurnModeControl_TogglesOnceAndSessionResetRestoresSnap()
        {
            var buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buttonObject.name = "TEST_TURN_MODE_BUTTON";
            buttonObject.transform.position = new Vector3(0f, 1f, 2f);
            var button = buttonObject.AddComponent<DemoPressInteractable>();
            button.Configure(buttonObject.GetComponent<Renderer>());
            var labelObject = new GameObject("TEST_TURN_MODE_LABEL");
            labelObject.transform.SetParent(button.transform, false);
            var label = labelObject.AddComponent<TextMesh>();
            var control = button.gameObject.AddComponent<LocomotionTurnModeControl>();
            control.Configure(button, locomotion, label);
            Assert.That(control.SmoothTurnEnabled, Is.False);
            Assert.That(label.text, Does.Contain("SNAP 30"));

            Assert.That(button.TryActivate("controller-left"), Is.True);
            Assert.That(control.SmoothTurnEnabled, Is.True);
            Assert.That(label.text, Does.Contain("SMOOTH 45/s"));
            Assert.That(button.TryActivate("hand-right"), Is.False,
                "two sources in the same frame must not double-toggle");
            yield return null;

            var reset = rig.AddComponent<VerticalSliceReset>();
            reset.Configure(locomotion, null, priority, new[] { button },
                null, null, null, control);
            reset.ResetSession(3.0);
            Assert.That(control.SmoothTurnEnabled, Is.False);
            Assert.That(label.text, Does.Contain("SNAP 30"));
        }

        [UnityTest]
        public IEnumerator DevelopmentTelemetry_IsBoundedByMoveAndSnapEdges()
        {
            const double now = 5.0;
            locomotion.ProcessInput(Vector2.zero, 0, 0.02, now);

            LogAssert.Expect(LogType.Log,
                new Regex("^ITPU_XR_LOCOMOTION MOVE_APPLIED .*moveCount=1$"));
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.02), Is.True);
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.04), Is.True);
            LogAssert.Expect(LogType.Log,
                new Regex("^ITPU_XR_LOCOMOTION MOVE_BURST_END reason=NEUTRAL .*appliedSamples=2 .*moveCount=2$"));
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now + 0.06), Is.False);

            LogAssert.Expect(LogType.Log,
                new Regex(@"^ITPU_XR_LOCOMOTION TURN_APPLIED .*requestedDegrees=30\.000 actualOriginDegrees=30\.000 .*turnCount=1$"));
            Assert.That(locomotion.ProcessInput(Vector2.zero, 1, 0.02, now + 0.30), Is.True);
            Assert.That(locomotion.ProcessInput(Vector2.zero, 1, 0.02, now + 0.60), Is.False,
                "holding the turn stick must not repeat a snap");

            LogAssert.Expect(LogType.Log,
                new Regex("^ITPU_XR_LOCOMOTION TURN_BURST_END reason=NEUTRAL .*appliedInBurst=1 suppressedSamples=1 neutral=True turnCount=1$"));
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, now + 0.62), Is.False);
            LogAssert.Expect(LogType.Log,
                new Regex(@"^ITPU_XR_LOCOMOTION TURN_APPLIED .*requestedDegrees=-30\.000 actualOriginDegrees=-30\.000 .*turnCount=2$"));
            Assert.That(locomotion.ProcessInput(Vector2.zero, -1, 0.02, now + 0.90), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CharacterController_MovesThenStopsAtWallAndNoWallControlContinues()
        {
            const double now = 1.0;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TEST_WALL";
            wall.transform.position = new Vector3(0.15f, 0.9f, 0.72f);
            wall.transform.localScale = new Vector3(2f, 1.8f, 0.1f);
            Physics.SyncTransforms();
            locomotion.ProcessInput(Vector2.zero, 0, 0.02, now);
            for (var index = 0; index < 80; index++)
                locomotion.ProcessInput(Vector2.up, 0, 0.02, now + 0.02 + index * 0.02);
            var wallStop = rig.transform.position.z;
            Assert.That(wallStop, Is.GreaterThan(0.05f), "the fixture must first prove movement");
            Assert.That(wallStop, Is.LessThan(0.48f), "the capsule must stop before crossing the wall");
            Assert.That(locomotion.LastMoveCollisionFlags & CollisionFlags.Sides,
                Is.EqualTo(CollisionFlags.Sides));
            Assert.That(locomotion.LastRequestedMove.sqrMagnitude, Is.GreaterThan(0f));
            Assert.That(Vector3.Distance(locomotion.LastRequestedMove, locomotion.LastActualMove),
                Is.GreaterThan(0.001f));
            Assert.That(locomotion.CollisionLimitedMoveCount, Is.GreaterThan(0));

            Object.DestroyImmediate(wall);
            var controller = rig.GetComponent<CharacterController>();
            controller.enabled = false;
            rig.transform.position = Vector3.zero;
            controller.enabled = true;
            locomotion.ResetPolicy();
            Physics.SyncTransforms();
            locomotion.ProcessInput(Vector2.zero, 0, 0.02, 3.0);
            for (var index = 0; index < 80; index++)
                locomotion.ProcessInput(Vector2.up, 0, 0.02, 3.02 + index * 0.02);
            Assert.That(rig.transform.position.z, Is.GreaterThan(wallStop + 0.4f),
                "the no-wall control must travel materially farther");
            Assert.That(locomotion.LastMoveCollisionFlags & CollisionFlags.Sides,
                Is.EqualTo(CollisionFlags.None));
            Assert.That(Vector3.Distance(locomotion.LastRequestedMove, locomotion.LastActualMove),
                Is.LessThan(0.001f));
            yield return null;
        }

        [Test]
        public void Capsule_FollowsTrackedHeadHeightAndRoomScaleOffset()
        {
            camera.transform.localPosition = new Vector3(0.6f, 1.2f, -0.2f);
            Assert.That(locomotion.SyncCapsuleToHead(), Is.True);
            var body = rig.GetComponent<CharacterController>();
            Assert.That(body.center.x, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(body.center.z, Is.EqualTo(-0.2f).Within(0.001f));
            Assert.That(body.height, Is.EqualTo(1.2f).Within(0.001f));
        }

        [Test]
        public void Grounding_ContinuesWithoutStickCommand()
        {
            var controller = rig.GetComponent<CharacterController>();
            controller.enabled = false;
            rig.transform.position = Vector3.up;
            controller.enabled = true;
            Physics.SyncTransforms();
            var startY = rig.transform.position.y;
            for (var index = 0; index < 30; index++) locomotion.TickGrounding(0.02);
            Assert.That(rig.transform.position.y, Is.LessThan(startY - 0.1f));
            Assert.That(rig.transform.position.y, Is.GreaterThanOrEqualTo(-0.01f));
        }
    }

    public sealed class InteractionBoundaryPlayModeTests
    {
        private GameObject rig;
        private GameObject teleportFloor;
        private XrLifecycleState lifecycle;
        private InteractionPriority priority;
        private XrLocomotionDriver locomotion;
        private HandTeleportIntentAdapter teleport;
        private Camera camera;
        private Collider floorCollider;
        private const string FloorId = "test.floor";

        [SetUp]
        public void SetUp()
        {
            rig = TestRig.Create(out lifecycle, out priority, out locomotion, out camera);
            teleport = rig.AddComponent<HandTeleportIntentAdapter>();
            teleport.Configure(lifecycle, priority, locomotion, camera);
            teleportFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            teleportFloor.name = "TEST_TELEPORT_FLOOR";
            teleportFloor.transform.position = new Vector3(0, -0.05f, 1.5f);
            teleportFloor.transform.localScale = new Vector3(4f, 0.1f, 4f);
            teleportFloor.AddComponent<TeleportSurface>();
            teleportFloor.AddComponent<StableId>().Set(FloorId);
            floorCollider = teleportFloor.GetComponent<Collider>();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (rig != null) Object.DestroyImmediate(rig);
            var testRoots = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(item => item != null && item.name.StartsWith("TEST_") &&
                               (item.transform.parent == null ||
                                !item.transform.parent.name.StartsWith("TEST_")))
                .ToArray();
            foreach (var item in testRoots)
                if (item != null) Object.DestroyImmediate(item);
        }

        [Test]
        public void Coordinator_InactiveSourceCannotCancelOrReleaseActiveOwner()
        {
            var target = new Vector3(0.15f, 0f, 1.5f);
            Assert.That(teleport.ObserveNeutral(0.0, "left-hand"), Is.True);
            Assert.That(teleport.ObserveNeutral(0.01, "right-hand"), Is.True);
            Assert.That(teleport.BeginIntent(0.2, FloorId, "left-hand", target, floorCollider,
                true, false, true, true), Is.True);
            teleport.CancelForSource("right-hand", "INACTIVE_TRACKING_LOSS");
            Assert.That(teleport.OwnerSource, Is.EqualTo("left-hand"));
            Assert.That(teleport.ReleaseAndCommit(0.3, "right-hand", true, false, true, true), Is.False);
            Assert.That(teleport.UpdateIntent(0.36, "left-hand", target, true, false, true, true), Is.True);
            Assert.That(teleport.ReleaseAndCommit(0.50, "left-hand", true, false, true, true), Is.True);
            Assert.That(locomotion.AppliedTeleportCount, Is.EqualTo(1));
            Assert.That(teleport.ReleaseAndCommit(0.51, "left-hand", true, false, true, true), Is.False);
        }

        [Test]
        public void Intent_FreezesDestinationAndLifecycleCancelsSynchronously()
        {
            var target = new Vector3(0.15f, 0f, 1.5f);
            Assert.That(teleport.ObserveNeutral(0.0, "left-hand"), Is.True);
            Assert.That(teleport.BeginIntent(0.2, FloorId, "left-hand", target, floorCollider,
                true, false, true, true), Is.True);
            Assert.That(teleport.UpdateIntent(0.36, "left-hand", target + Vector3.right * 0.4f,
                true, false, true, true), Is.False);
            Assert.That(locomotion.AppliedTeleportCount, Is.Zero);

            Assert.That(teleport.ObserveNeutral(0.5, "left-hand"), Is.True);
            Assert.That(teleport.BeginIntent(0.6, FloorId, "left-hand", target, floorCollider,
                true, false, true, true), Is.True);
            lifecycle.SetPaused(true);
            Assert.That(teleport.OwnerSource, Is.Empty);
            lifecycle.SetPaused(false);
            Assert.That(teleport.ReleaseAndCommit(1.0, "left-hand", true, false, true, true), Is.False);
            Assert.That(locomotion.AppliedTeleportCount, Is.Zero);
        }

        [Test]
        public void Commit_RevalidatesModalPrecisionAndOccupiedDestination()
        {
            var target = new Vector3(0.15f, 0f, 1.5f);
            Assert.That(teleport.ObserveNeutral(0.0, "left-hand"), Is.True);
            Assert.That(teleport.BeginIntent(0.2, FloorId, "left-hand", target, floorCollider,
                true, false, true, true), Is.True);
            Assert.That(teleport.UpdateIntent(0.36, "left-hand", target, true, false, true, true), Is.True);
            priority.SetPrecisionBusy(true);
            Assert.That(teleport.OwnerSource, Is.Empty);
            priority.SetPrecisionBusy(false);

            Assert.That(teleport.ObserveNeutral(0.6, "left-hand"), Is.True);
            Assert.That(teleport.BeginIntent(0.7, FloorId, "left-hand", target, floorCollider,
                true, false, true, true), Is.True);
            Assert.That(teleport.UpdateIntent(0.86, "left-hand", target, true, false, true, true), Is.True);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "TEST_LANDING_BLOCKER";
            blocker.transform.position = target + Vector3.up * 0.9f;
            blocker.transform.localScale = new Vector3(0.5f, 1.8f, 0.5f);
            Physics.SyncTransforms();
            Assert.That(teleport.ReleaseAndCommit(1.0, "left-hand", true, false, true, true), Is.False);
            Assert.That(locomotion.AppliedTeleportCount, Is.Zero);
        }

        [Test]
        public void TrackingSpaceConversion_AppliesShiftAndRotationExactlyOnce()
        {
            var space = new GameObject("TEST_TRACKING_SPACE");
            space.transform.SetPositionAndRotation(new Vector3(3f, 0.5f, -2f), Quaternion.Euler(0, 90, 0));
            var local = new Pose(new Vector3(0.4f, 1.2f, 0.8f), Quaternion.Euler(0, 30, 0));
            var world = XrHandsPointer.TrackingToWorldPose(space.transform, local);
            Assert.That(world.position, Is.EqualTo(space.transform.TransformPoint(local.position)));
            Assert.That(Quaternion.Angle(world.rotation, space.transform.rotation * local.rotation),
                Is.LessThan(0.001f));
        }

        [Test]
        public void PointerVisual_ShowsRayAndHitMarkerWithoutAddingCollision()
        {
            var root = new GameObject("TEST_POINTER_VISUAL");
            var lineObject = new GameObject("Aim Line");
            lineObject.transform.SetParent(root.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.enabled = false;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Aim Hit";
            marker.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            var visual = root.AddComponent<XrPointerVisual>();
            visual.Configure(line, marker.transform, marker.GetComponent<Renderer>());

            var ray = new Ray(new Vector3(1f, 1.2f, 1f), Vector3.forward);
            var hitPoint = new Vector3(1f, 1.2f, 3f);
            visual.Present(true, ray, 8f, true, hitPoint, Vector3.back,
                XrAimTargetKind.Interactable);

            Assert.That(visual.LineVisible, Is.True);
            Assert.That(visual.MarkerVisible, Is.True);
            Assert.That(visual.LastTargetKind, Is.EqualTo(XrAimTargetKind.Interactable));
            Assert.That(visual.LastEndPoint, Is.EqualTo(hitPoint));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);

            visual.Present(true, ray, 6f, false, default, default, XrAimTargetKind.None);
            Assert.That(visual.LineVisible, Is.True);
            Assert.That(visual.MarkerVisible, Is.False);
            Assert.That(visual.LastEndPoint, Is.EqualTo(ray.origin + ray.direction * 6f));
            visual.Hide();
            Assert.That(visual.LineVisible, Is.False);
            Assert.That(visual.MarkerVisible, Is.False);
            visual.Present(false, ray, 6f, true, hitPoint, Vector3.back,
                XrAimTargetKind.Interactable);
            Assert.That(visual.LineVisible, Is.False);
            Assert.That(visual.MarkerVisible, Is.False);
        }

        [Test]
        public void PointerSourceArbitration_NeverShowsHandAndControllerForSameSource()
        {
            AssertPointerSourcePolicy(true, HandTrackingDataSource.Controller, true,
                expectedOptical: false, expectedController: true);
            AssertPointerSourcePolicy(true, HandTrackingDataSource.Controller, false,
                expectedOptical: false, expectedController: false);
            AssertPointerSourcePolicy(true, HandTrackingDataSource.Unobstructed, true,
                expectedOptical: true, expectedController: false);
            AssertPointerSourcePolicy(false, default, true,
                expectedOptical: false, expectedController: true);
            AssertPointerSourcePolicy(false, default, false,
                expectedOptical: true, expectedController: false);
        }

        private static void AssertPointerSourcePolicy(bool sourceKnown,
            HandTrackingDataSource source, bool controllerTracked, bool expectedOptical,
            bool expectedController)
        {
            var optical = XrHandsPointer.ShouldUseOpticalPointer(sourceKnown, source, controllerTracked);
            var controller = XrControllerPointer.ShouldUseControllerPointer(sourceKnown, source,
                controllerTracked);
            Assert.That(optical, Is.EqualTo(expectedOptical));
            Assert.That(controller, Is.EqualTo(expectedController));
            Assert.That(optical && controller, Is.False,
                "One side must never render/actuate both optical and controller pointers.");
        }

        [UnityTest]
        public IEnumerator HandAndControllerActivateSameTargetOnceAndResetIsReachable()
        {
            var route = CreateButton("TEST_ROUTE_BUTTON", new Vector3(0, 1f, 2f));
            var resetButton = CreateButton("TEST_RESET_BUTTON", new Vector3(0.5f, 1f, 2f));
            var hand = rig.AddComponent<XrHandsPointer>();
            hand.Configure(true, teleport, priority, lifecycle, rig.transform);
            var controllerObject = new GameObject("TEST_CONTROLLER_POINTER");
            controllerObject.transform.SetParent(rig.transform, false);
            controllerObject.transform.localPosition = new Vector3(0, 1f, 0);
            var controllerPointer = controllerObject.AddComponent<XrControllerPointer>();
            controllerPointer.Configure(true, teleport, priority, lifecycle);
            hand.enabled = false;
            controllerPointer.enabled = false;
            var reset = rig.AddComponent<VerticalSliceReset>();
            reset.Configure(locomotion, teleport, priority, new[] { route, resetButton },
                new[] { hand }, new[] { controllerPointer });
            var resetRoute = resetButton.gameObject.AddComponent<SessionResetButton>();
            resetRoute.Configure(resetButton, reset);
            resetRoute.SetClockProviderForTests(() => 2.0);
            Physics.SyncTransforms();

            var routeRay = new Ray(new Vector3(0, 1f, 0), Vector3.forward);
            hand.ProcessSample(true, false, false, routeRay, 0.0);
            hand.ProcessSample(true, false, false, routeRay, 0.1);
            hand.ProcessSample(true, false, true, routeRay, 0.2);
            hand.ProcessSample(true, false, true, routeRay, 0.3);
            Assert.That(route.ActivationCount, Is.EqualTo(1));
            hand.ProcessSample(true, false, false, routeRay, 0.4);
            yield return null;

            controllerPointer.ProcessSample(true, false, routeRay, 0.6);
            controllerPointer.ProcessSample(true, true, routeRay, 0.7);
            controllerPointer.ProcessSample(true, true, routeRay, 0.8);
            Assert.That(route.ActivationCount, Is.EqualTo(2));
            controllerPointer.ProcessSample(true, false, routeRay, 0.9);
            controllerPointer.ProcessSample(true, false, routeRay, 1.0);

            var resetDirection = (resetButton.transform.position - new Vector3(0, 1f, 0)).normalized;
            var resetRay = new Ray(new Vector3(0, 1f, 0), resetDirection);
            controllerPointer.ProcessSample(true, true, resetRay, 1.1);
            Assert.That(reset.SessionRevision, Is.EqualTo(2));
            Assert.That(route.ActivationCount, Is.Zero);
            Assert.That(resetButton.ActivationCount, Is.Zero);
        }

        [Test]
        public void HeldPinchOnFirstTrackedSampleCannotBecomeTeleport()
        {
            var hand = rig.AddComponent<XrHandsPointer>();
            hand.Configure(true, teleport, priority, lifecycle, rig.transform);
            var origin = camera.transform.position;
            var target = new Vector3(0.15f, 0f, 1.5f);
            var ray = new Ray(origin, (target - origin).normalized);
            hand.ProcessSample(true, false, true, ray, 0.0);
            hand.ProcessSample(true, false, true, ray, 0.4);
            hand.ProcessSample(true, false, false, ray, 0.5);
            Assert.That(locomotion.AppliedTeleportCount, Is.Zero);
            Assert.That(teleport.OwnerSource, Is.Empty);
        }

        [Test]
        public void ControllerComfortTeleport_UsesSameMoverAndHoverDoesNotBlockWalk()
        {
            var controllerObject = new GameObject("TEST_COMFORT_CONTROLLER");
            var pointer = controllerObject.AddComponent<XrControllerPointer>();
            pointer.Configure(true, teleport, priority, lifecycle);
            pointer.enabled = false;
            var target = new Vector3(0.15f, 0f, 1.5f);
            var ray = new Ray(camera.transform.position, (target - camera.transform.position).normalized);
            pointer.ProcessSample(true, false, ray, 0.0);
            Assert.That(priority.TeleportActive, Is.False);
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, 0.05), Is.False);
            Assert.That(locomotion.ProcessInput(Vector2.up, 0, 0.02, 0.07), Is.True,
                "passive pointing must not block controller walk");

            pointer.ProcessSample(true, true, ray, 0.2);
            Assert.That(priority.TeleportActive, Is.True);
            pointer.ProcessSample(true, true, ray, 0.36);
            pointer.ProcessSample(true, false, ray, 0.50);
            Assert.That(locomotion.AppliedTeleportCount, Is.EqualTo(1));
        }

        [Test]
        public void DisabledAdaptersAndUiBusyRejectActuation()
        {
            Assert.That(teleport.CanDirectInteract(true, false), Is.True);
            priority.SetUiBusy(true);
            Assert.That(teleport.CanDirectInteract(true, false), Is.False);
            priority.SetUiBusy(false);
            teleport.enabled = false;
            Assert.That(teleport.ObserveNeutral(0.0, "left-hand"), Is.False);
            Assert.That(teleport.BeginIntent(0.1, FloorId, "left-hand", new Vector3(0.15f, 0, 1.5f),
                floorCollider, true, false, true, true), Is.False);
            locomotion.enabled = false;
            Assert.That(locomotion.ProcessInput(Vector2.zero, 0, 0.02, 0.2), Is.False);
            Assert.That(locomotion.TryTeleportTo(new Vector3(0.15f, 0, 1.5f), floorCollider), Is.False);
        }

        [Test]
        public void HeadToPointerPath_IsBlockedByInterveningWall()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "TEST_PATH_WALL";
            wall.transform.position = camera.transform.position + Vector3.forward * 0.3f;
            wall.transform.localScale = new Vector3(1f, 1f, 0.05f);
            Physics.SyncTransforms();
            Assert.That(teleport.PathClearFromHead(camera.transform.position + Vector3.forward * 0.7f),
                Is.False);
        }

        [UnityTest]
        public IEnumerator ControllerTriggerAction_UsesRealInputSystemEdgeWithoutRepeat()
        {
            var route = CreateButton("TEST_INPUT_ROUTE_BUTTON", new Vector3(0, 1f, 2f));
            var controllerObject = new GameObject("TEST_INPUT_CONTROLLER_POINTER");
            controllerObject.transform.SetParent(rig.transform, false);
            controllerObject.transform.localPosition = new Vector3(0, 1f, 0);
            var pointer = controllerObject.AddComponent<XrControllerPointer>();
            pointer.Configure(true, teleport, priority, lifecycle);
            Physics.SyncTransforms();

            InputSystem.RegisterLayout<TestXrControllerDevice>();
            var device = InputSystem.AddDevice<TestXrControllerDevice>();
            InputSystem.SetDeviceUsage(device, UnityEngine.InputSystem.CommonUsages.LeftHand);
            try
            {
                InputSystem.QueueStateEvent(device, new TestXrControllerState());
                InputSystem.Update();
                yield return null;
                InputSystem.QueueStateEvent(device, new TestXrControllerState { buttons = 1 });
                InputSystem.Update();
                yield return null;
                Assert.That(route.ActivationCount, Is.EqualTo(1));
                InputSystem.QueueStateEvent(device, new TestXrControllerState { buttons = 1 });
                InputSystem.Update();
                yield return null;
                Assert.That(route.ActivationCount, Is.EqualTo(1));
                InputSystem.QueueStateEvent(device, new TestXrControllerState());
                InputSystem.Update();
                yield return null;
            }
            finally
            {
                InputSystem.RemoveDevice(device);
            }
        }

        private static DemoPressInteractable CreateButton(string name, Vector3 position)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name;
            target.transform.position = position;
            target.transform.localScale = new Vector3(0.3f, 0.2f, 0.1f);
            var button = target.AddComponent<DemoPressInteractable>();
            button.Configure(target.GetComponent<Renderer>());
            return button;
        }
    }

    public sealed class VerticalSliceSceneTests
    {
        [UnityTest]
        public IEnumerator SavedScene_FloorCollidersMatchVisualFrameAndSupportAuthoredSpawn()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Physics.SyncTransforms();

            var floorIds = new[] { "FLOOR_0_2", "FLOOR_0_3" };
            foreach (var floorId in floorIds)
            {
                var stable = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Single(item => item.Value == floorId);
                var colliders = stable.GetComponentsInChildren<BoxCollider>(true);
                Assert.That(colliders, Has.Length.EqualTo(1));
                var collider = colliders.Single();
                Assert.That(collider, Is.Not.Null, floorId + " must own one generated box collider proxy.");
                Assert.That(stable.GetComponent<BoxCollider>(), Is.Null,
                    floorId + " must not keep a collider in the rotated visual FBX frame.");
                Assert.That(collider.gameObject, Is.Not.EqualTo(stable.gameObject));
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(collider.GetComponentInParent<TeleportSurface>(), Is.Not.Null,
                    floorId + " collider must remain attached to its teleport surface hierarchy.");
                Assert.That(collider.bounds.size.y, Is.EqualTo(0.1f).Within(0.015f),
                    floorId + " physical thickness must match the visible horizontal floor.");
                Assert.That(collider.bounds.size.x, Is.EqualTo(4.6f).Within(0.015f));

                var visualRenderers = stable.GetComponentsInChildren<Renderer>(true);
                Assert.That(visualRenderers, Is.Not.Empty, floorId + " must retain its imported visual mesh.");
                var visualBounds = visualRenderers[0].bounds;
                foreach (var visualRenderer in visualRenderers.Skip(1))
                    visualBounds.Encapsulate(visualRenderer.bounds);
                Assert.That(collider.bounds.center.x, Is.EqualTo(visualBounds.center.x).Within(0.015f),
                    floorId + " visual and collider X frames must match the imported FBX handedness.");
                Assert.That(collider.bounds.center.z, Is.EqualTo(visualBounds.center.z).Within(0.015f),
                    floorId + " visual and collider Z frames must match.");
                Assert.That(collider.bounds.size.x, Is.EqualTo(visualBounds.size.x).Within(0.015f));
                Assert.That(collider.bounds.size.z, Is.EqualTo(visualBounds.size.z).Within(0.015f));
            }

            var spawnFloor = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Single(item => item.Value == "FLOOR_0_2")
                .GetComponentInChildren<BoxCollider>(true);
            Assert.That(spawnFloor.bounds.size.z, Is.EqualTo(3.9f).Within(0.015f));
            Assert.That(spawnFloor.bounds.max.y, Is.EqualTo(0f).Within(0.015f));
            foreach (var offset in new[]
                     {
                         new Vector2(-0.2f, -0.2f), new Vector2(-0.2f, 0.2f),
                         new Vector2(0.2f, -0.2f), new Vector2(0.2f, 0.2f),
                     })
            {
                var ray = new Ray(new Vector3(-3.20f + offset.x, 0.5f, -10.70f + offset.y),
                    Vector3.down);
                Assert.That(spawnFloor.Raycast(ray, out var hit, 1f), Is.True,
                    "The real authored capsule footprint must be supported by FLOOR_0_2.");
                Assert.That(hit.normal.y, Is.GreaterThan(0.99f));
                Assert.That(hit.point.y, Is.EqualTo(0f).Within(0.015f));
            }

            foreach (var controlId in new[]
                      {
                          "interaction.demo-route-button", "interaction.session-reset",
                          "interaction.turn-comfort-mode",
                      })
            {
                var control = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Single(item => item.Value == controlId);
                var controlBounds = control.GetComponent<Renderer>().bounds;
                Assert.That(controlBounds.min.x, Is.GreaterThanOrEqualTo(spawnFloor.bounds.min.x),
                    controlId + " must not protrude past the physical floor edge.");
                Assert.That(controlBounds.max.x, Is.LessThanOrEqualTo(spawnFloor.bounds.max.x));
                Assert.That(controlBounds.min.z, Is.GreaterThanOrEqualTo(spawnFloor.bounds.min.z));
                Assert.That(controlBounds.max.z, Is.LessThanOrEqualTo(spawnFloor.bounds.max.z));
            }

            var yawedChair = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Value == "CHAIR_0_1");
            var chairColliders = yawedChair.GetComponentsInChildren<BoxCollider>(true);
            Assert.That(chairColliders, Has.Length.GreaterThan(1));
            var chairColliderBounds = chairColliders[0].bounds;
            foreach (var chairCollider in chairColliders.Skip(1))
                chairColliderBounds.Encapsulate(chairCollider.bounds);
            Assert.That(chairColliderBounds.min.x, Is.EqualTo(-2.91734f).Within(0.02f));
            Assert.That(chairColliderBounds.max.x, Is.EqualTo(-2.39499f).Within(0.02f));
            Assert.That(chairColliderBounds.min.z, Is.EqualTo(-11.3179f).Within(0.02f));
            Assert.That(chairColliderBounds.max.z, Is.EqualTo(-10.7821f).Within(0.02f));
            var chairRenderers = yawedChair.GetComponentsInChildren<Renderer>(true);
            Assert.That(chairRenderers.Any(item => item.bounds.Intersects(chairColliderBounds)), Is.True,
                "Yawed chair visual and collider aggregate must share the same converted handedness frame.");
        }

        [UnityTest]
        public IEnumerator SavedScene_HasSingleAuthorityPoseBindingsAndAccessibleInputs()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Assert.That(Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(2));
            Assert.That(Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(2));
            var pointerVisuals = Object.FindObjectsByType<XrPointerVisual>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(pointerVisuals, Has.Length.EqualTo(4));
            Assert.That(pointerVisuals.All(item => item.Line != null && item.HitMarker != null &&
                                                   item.HitMarkerRenderer != null), Is.True);
            Assert.That(pointerVisuals.Sum(item => item.GetComponentsInChildren<Collider>(true).Length),
                Is.EqualTo(0), "Aim lines and hit markers must remain presentation-only.");
            Assert.That(pointerVisuals.All(item => item.Line.startWidth >= 0.008f &&
                                                   item.Line.endWidth >= 0.003f), Is.True,
                "Quest pointer lines must remain visible without becoming oversized.");
            Assert.That(Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).All(item => item.PointerVisual != null), Is.True);
            Assert.That(Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).All(item => item.PointerVisual != null &&
                                                          item.MaxDistance <= 6f), Is.True);
            Assert.That(Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).All(item => item.MaxDistance <= 6f), Is.True);
            var handVisuals = Object.FindObjectsByType<XRHandMeshController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(handVisuals, Has.Length.EqualTo(2));
            Assert.That(Object.FindObjectsByType<XRHandSkeletonDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(2));
            Assert.That(Object.FindObjectsByType<XRHandTrackingEvents>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(2));
            Assert.That(handVisuals.All(item => item.handMeshRenderer is SkinnedMeshRenderer), Is.True);
            Assert.That(handVisuals.Sum(item => item.GetComponentsInChildren<Collider>(true).Length),
                Is.EqualTo(0), "Hand presentation must not change collision or locomotion authority.");
            Assert.That(handVisuals.Select(item => item.transform.parent.name),
                Is.All.EqualTo("Camera Offset"));
            var handTelemetry = Object.FindObjectsByType<XrHandVisualTelemetry>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            Assert.That(handTelemetry.LeftVisual, Is.Not.Null);
            Assert.That(handTelemetry.RightVisual, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<DemoPressInteractable>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None), Has.Length.EqualTo(14),
                "accepted base/C01 controls, three C02, five fixed C03 and one coffee control are required");
            Assert.That(GameObject.Find("COFFEE_BREW_BUTTON"), Is.Not.Null);
            var interactionIds = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Select(item => item.Value).ToArray();
            Assert.That(interactionIds, Does.Contain("interaction.c02-step"));
            Assert.That(interactionIds, Does.Contain("interaction.c02-add-wait"));
            Assert.That(interactionIds, Does.Contain("interaction.c02-guest-arrived"));
            Assert.That(interactionIds, Does.Contain("interaction.c03-bypass"));
            Assert.That(interactionIds, Does.Contain("interaction.c03-install-gate"));
            Assert.That(interactionIds, Does.Contain("interaction.c03-auth01"));
            Assert.That(interactionIds, Does.Contain("interaction.c03-auth02"));
            Assert.That(interactionIds, Does.Contain("interaction.c03-auth03"));
            Assert.That(Object.FindObjectsByType<LocomotionTurnModeControl>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<SessionResetButton>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrStartupPoseAligner>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));

            var poseDrivers = Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(poseDrivers, Has.Length.EqualTo(3));
            var hmdDriver = poseDrivers.Single(item => item.GetComponent<Camera>() != null);
            Assert.That(hmdDriver.positionInput.action.bindings.Select(item => item.path),
                Does.Contain("<XRHMD>/centerEyePosition"));
            Assert.That(hmdDriver.rotationInput.action.bindings.Select(item => item.path),
                Does.Contain("<XRHMD>/centerEyeRotation"));
            Assert.That(hmdDriver.trackingStateInput.action.bindings.Select(item => item.path),
                Does.Contain("<XRHMD>/trackingState"));
            foreach (var controllerDriver in poseDrivers.Where(item => item.GetComponent<Camera>() == null))
            {
                Assert.That(controllerDriver.positionInput.action.bindings.Any(item =>
                    item.path.Contains("pointerPosition")), Is.True);
                Assert.That(controllerDriver.rotationInput.action.bindings.Any(item =>
                    item.path.Contains("pointerRotation")), Is.True);
                Assert.That(controllerDriver.positionInput.action.bindings.Any(item =>
                    item.path.Contains("devicePosition")), Is.False);
            }
            foreach (var pointer in Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                Assert.That(pointer.TriggerAction.bindings.Count, Is.GreaterThanOrEqualTo(2));

            var stableIds = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Select(item => item.Value).ToArray();
            Assert.That(stableIds, Does.Contain("environment.full.v005"));
            Assert.That(stableIds, Does.Contain("FLOOR_0_2"));
            Assert.That(stableIds, Does.Contain("xr.origin.primary"));
            Assert.That(stableIds, Does.Contain("interaction.demo-route-button"));
            Assert.That(stableIds, Does.Contain("interaction.session-reset"));
            Assert.That(stableIds, Does.Contain("interaction.turn-comfort-mode"));
            Assert.That(stableIds, Does.Contain("challenge.c01"));
            Assert.That(stableIds, Does.Contain("interaction.c01-link-handler"));
            Assert.That(stableIds, Does.Contain("interaction.c01-destination-select"));

            var xrOrigin = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            Assert.That(xrOrigin.CameraYOffset, Is.EqualTo(1.65f).Within(0.001f),
                "Device/Unbounded tracking fallback must retain standing eye height.");
            var environment = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Value == "environment.full.v005");
            var renderers = environment.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, "Accepted environment must contain renderers.");
            var environmentBounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) environmentBounds.Encapsulate(renderer.bounds);
            var directionToEnvironment = environmentBounds.center - xrOrigin.Camera.transform.position;
            directionToEnvironment.y = 0f;
            Assert.That(directionToEnvironment.sqrMagnitude, Is.GreaterThan(0.01f));
            var savedLocomotion = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            Assert.That(savedLocomotion.SmoothTurnEnabled, Is.False,
                "The saved scene must start and reset in snap 30 mode.");
            var turnModeControl = Object.FindObjectsByType<LocomotionTurnModeControl>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            Assert.That(turnModeControl.SmoothTurnEnabled, Is.False);
            Assert.That(turnModeControl.GetComponentInChildren<TextMesh>(true).text, Does.Contain("SNAP 30"));

            var enabledLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Where(item => item.enabled && item.gameObject.activeInHierarchy).ToArray();
            var key = enabledLights.Single(item => item.type == LightType.Directional);
            Assert.That(enabledLights.Any(item => item.type == LightType.Point || item.type == LightType.Spot),
                Is.False, "The Quest lighting correction must not add realtime point or spot lights.");
            Assert.That(key.intensity, Is.EqualTo(0.88f).Within(0.001f));
            Assert.That(key.shadows, Is.EqualTo(LightShadows.None),
                "The unbaked full environment must not project unstable realtime shadow bands on Quest.");
            Assert.That(key.shadowStrength, Is.EqualTo(0f).Within(0.001f));
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
            Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(1.20f).Within(0.001f));
            Assert.That(RenderSettings.ambientSkyColor.r, Is.EqualTo(0.84f).Within(0.01f));
            Assert.That(RenderSettings.ambientEquatorColor.r, Is.EqualTo(0.68f).Within(0.01f));
            Assert.That(RenderSettings.ambientGroundColor.r, Is.EqualTo(0.48f).Within(0.01f));
            var interiorMargin = savedLocomotion.MaximumRoomScaleOffset;
            Assert.That(xrOrigin.Camera.transform.position.x,
                Is.InRange(environmentBounds.min.x + interiorMargin,
                    environmentBounds.max.x - interiorMargin),
                "Saved head X must be inside the accepted visible environment, not behind its side wall.");
            Assert.That(xrOrigin.Camera.transform.position.z,
                Is.InRange(environmentBounds.min.z + interiorMargin,
                    environmentBounds.max.z - interiorMargin),
                "Saved head Z must be inside the accepted visible environment.");
            var expectedStartForward = new Vector3(0.17364818f, 0f, -0.98480775f);
            Assert.That(Vector3.Angle(xrOrigin.transform.forward, expectedStartForward), Is.LessThan(0.1f),
                "Saved XR origin must face inward at the authored 170-degree Unity yaw.");
            Assert.That(Vector3.Dot(xrOrigin.transform.forward, directionToEnvironment.normalized),
                Is.GreaterThan(0f), "The accepted environment center must remain in the forward hemisphere.");

            var frustum = GeometryUtility.CalculateFrustumPlanes(xrOrigin.Camera);
            foreach (var visibleId in new[]
                     {
                          "interaction.demo-route-button", "interaction.session-reset",
                          "interaction.turn-comfort-mode",
                         "CHAIR_1_1", "DESK_1_1", "COLUMN_403",
                     })
            {
                var target = Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Single(item => item.Value == visibleId);
                var targetRenderers = target.GetComponentsInChildren<Renderer>(true);
                Assert.That(targetRenderers, Is.Not.Empty, visibleId + " must have a visible renderer.");
                Assert.That(targetRenderers.Any(item => GeometryUtility.TestPlanesAABB(frustum, item.bounds)),
                    Is.True, visibleId + " must start inside the deterministic camera frustum.");
            }
        }

        [UnityTest]
        public IEnumerator StartupPoseAligner_NormalizesGuardianOffsetAndInitialYaw()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;

            var origin = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var aligner = Object.FindObjectsByType<XrStartupPoseAligner>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(false, false, false);

            var hmd = InputSystem.AddDevice<XRHMD>();
            var alignFromInputState = typeof(XrStartupPoseAligner).GetMethod(
                "TryAlignTrackedPoseFromInputState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(alignFromInputState, Is.Not.Null);

            var samples = new[]
            {
                (position: new Vector3(1.8f, 1.65f, -1.3f), yaw: 67f),
                (position: new Vector3(-1.6f, 1.42f, 1.7f), yaw: -41f),
            };
            try
            {
                aligner.Rearm();
                var rejected = (bool)alignFromInputState.Invoke(aligner,
                    new object[] { InputTrackingState.Position, true, TrackingOriginModeFlags.Floor, 1 });
                Assert.That(rejected, Is.False,
                    "Position-only tracking must not commit even when isTracked is true.");
                Assert.That(aligner.IsAligned, Is.False);
                var unknown = (bool)alignFromInputState.Invoke(aligner,
                    new object[]
                    {
                        InputTrackingState.Position | InputTrackingState.Rotation,
                        true, default(TrackingOriginModeFlags), 2,
                    });
                Assert.That(unknown, Is.False, "Unknown actual tracking origin must not commit.");

                var frameBase = 10;
                foreach (var sample in samples)
                {
                    InputSystem.QueueDeltaStateEvent(hmd.centerEyePosition, sample.position);
                    InputSystem.QueueDeltaStateEvent(hmd.centerEyeRotation,
                        Quaternion.Euler(0f, sample.yaw, 0f));
                    InputSystem.Update();
                    yield return null;

                    aligner.Rearm();
                    var first = (bool)alignFromInputState.Invoke(aligner,
                        new object[]
                        {
                            InputTrackingState.Position | InputTrackingState.Rotation,
                            false, TrackingOriginModeFlags.Floor, frameBase,
                        });
                    var sameFrame = (bool)alignFromInputState.Invoke(aligner,
                        new object[]
                        {
                            InputTrackingState.Position | InputTrackingState.Rotation,
                            false, TrackingOriginModeFlags.Floor, frameBase,
                        });
                    var secondFrame = (bool)alignFromInputState.Invoke(aligner,
                        new object[]
                        {
                            InputTrackingState.Position | InputTrackingState.Rotation,
                            false, TrackingOriginModeFlags.Floor, frameBase + 1,
                        });
                    var accepted = (bool)alignFromInputState.Invoke(aligner,
                        new object[]
                        {
                            InputTrackingState.Position | InputTrackingState.Rotation,
                            false, TrackingOriginModeFlags.Floor, frameBase + 2,
                        });
                    Assert.That(first || sameFrame || secondFrame, Is.False,
                        "LateUpdate and beforeRender in one frame must not shorten settle.");
                    Assert.That(lifecycle.HeadTracked, Is.False,
                        "Regression must not depend on the legacy InputDevices tracking flag.");
                    Assert.That(accepted, Is.True);
                    Assert.That(aligner.IsAligned, Is.True);
                    Assert.That(origin.Camera.transform.position.x, Is.EqualTo(-3.20f).Within(0.001f));
                    Assert.That(origin.Camera.transform.position.z, Is.EqualTo(-10.70f).Within(0.001f));
                    var horizontalForward = Vector3.ProjectOnPlane(origin.Camera.transform.forward,
                        Vector3.up).normalized;
                    Assert.That(Vector3.Angle(horizontalForward,
                        new Vector3(0.17364818f, 0f, -0.98480775f)), Is.LessThan(0.1f));
                    var body = origin.GetComponent<CharacterController>();
                    Assert.That(new Vector2(body.center.x, body.center.z).magnitude, Is.LessThan(0.01f));
                    frameBase += 10;
                }
            }
            finally
            {
                InputSystem.RemoveDevice(hmd);
            }
        }

        [UnityTest]
        public IEnumerator SavedScene_StartupGatePreventsFallAndGroundingIsStableForFourSeconds()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;

            var origin = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var aligner = Object.FindObjectsByType<XrStartupPoseAligner>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var locomotion = Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            var lifecycle = Object.FindObjectsByType<XrLifecycleState>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            lifecycle.SetTracking(true, true, true);

            aligner.Rearm();
            var pendingY = origin.transform.position.y;
            for (var index = 0; index < 100; index++) locomotion.TickGrounding(0.02);
            Assert.That(origin.transform.position.y, Is.EqualTo(pendingY).Within(0.0001f),
                "Gravity must not run before startup placement commits.");

            var alignFromInputState = typeof(XrStartupPoseAligner).GetMethod(
                "TryAlignTrackedPoseFromInputState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(alignFromInputState, Is.Not.Null);
            var state = InputTrackingState.Position | InputTrackingState.Rotation;
            Assert.That((bool)alignFromInputState.Invoke(aligner,
                new object[] { state, false, TrackingOriginModeFlags.Floor, 100 }), Is.False);
            Assert.That((bool)alignFromInputState.Invoke(aligner,
                new object[] { state, false, TrackingOriginModeFlags.Floor, 101 }), Is.False);
            Assert.That((bool)alignFromInputState.Invoke(aligner,
                new object[] { state, false, TrackingOriginModeFlags.Floor, 102 }), Is.True);
            Assert.That(aligner.IsAligned, Is.True);

            Physics.SyncTransforms();
            var groundedY = origin.transform.position.y;
            for (var index = 0; index < 200; index++) locomotion.TickGrounding(0.02);
            Assert.That(origin.transform.position.y, Is.EqualTo(groundedY).Within(0.01f),
                "The authored saved-scene floor must support four seconds of gravity.");

            origin.transform.GetPositionAndRotation(out var positionBeforeFocusRecovery,
                out var rotationBeforeFocusRecovery);
            lifecycle.SetFocus(false);
            lifecycle.SetFocus(true);
            Assert.That(aligner.TryAlignTrackedPose(), Is.False,
                "Ordinary focus recovery must not recenter an accepted placement.");
            Assert.That(Vector3.Distance(origin.transform.position, positionBeforeFocusRecovery),
                Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(origin.transform.rotation, rotationBeforeFocusRecovery),
                Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator HmdTrackedPoseDriver_ConsumesSyntheticInputSystemPose()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            var driver = Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.GetComponent<Camera>() != null);
            var hmd = InputSystem.AddDevice<XRHMD>();
            var expectedPosition = new Vector3(0.25f, 1.42f, -0.1f);
            var expectedRotation = Quaternion.Euler(4f, 17f, 0f);
            try
            {
                InputSystem.QueueDeltaStateEvent(hmd.centerEyePosition, expectedPosition);
                InputSystem.QueueDeltaStateEvent(hmd.centerEyeRotation, expectedRotation);
                InputSystem.QueueDeltaStateEvent(hmd.trackingState,
                    (int)(InputTrackingState.Position | InputTrackingState.Rotation));
                InputSystem.Update();
                yield return null;
                Assert.That(Vector3.Distance(driver.transform.localPosition, expectedPosition),
                    Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(driver.transform.localRotation, expectedRotation),
                    Is.LessThan(0.01f));
            }
            finally
            {
                InputSystem.RemoveDevice(hmd);
            }
        }
    }

    internal static class TestRig
    {
        public static GameObject Create(out XrLifecycleState lifecycle, out InteractionPriority priority,
            out XrLocomotionDriver locomotion, out Camera camera)
        {
            var rig = new GameObject("TEST_XR_ORIGIN");
            var origin = rig.AddComponent<XROrigin>();
            var controller = rig.AddComponent<CharacterController>();
            controller.center = new Vector3(0, 0.9f, 0);
            controller.height = 1.8f;
            controller.radius = 0.24f;
            controller.skinWidth = 0.03f;
            lifecycle = rig.AddComponent<XrLifecycleState>();
            lifecycle.SetDevicePolling(false);
            lifecycle.SetTracking(true, true, true);
            lifecycle.SetFocus(true);
            lifecycle.SetPaused(false);
            priority = rig.AddComponent<InteractionPriority>();
            locomotion = rig.AddComponent<XrLocomotionDriver>();
            var cameraObject = new GameObject("TEST_CAMERA");
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.transform.localPosition = new Vector3(0.15f, 1.65f, 0.05f);
            camera = cameraObject.AddComponent<Camera>();
            origin.Origin = rig;
            origin.CameraFloorOffsetObject = rig;
            origin.Camera = camera;
            locomotion.Configure(origin, camera, lifecycle, priority);
            locomotion.SetAutomaticInput(false);
            return rig;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    internal struct TestXrControllerState : IInputStateTypeInfo
    {
        public FourCC format => new('I', 'T', 'P', 'U');

        [InputControl(name = "triggerPressed", layout = "Button", bit = 0)]
        [FieldOffset(0)] public uint buttons;
    }

    [InputControlLayout(stateType = typeof(TestXrControllerState))]
    internal sealed class TestXrControllerDevice : XRController
    {
        public ButtonControl triggerPressed { get; private set; }

        protected override void FinishSetup()
        {
            base.FinishSetup();
            triggerPressed = GetChildControl<ButtonControl>("triggerPressed");
        }
    }
}
