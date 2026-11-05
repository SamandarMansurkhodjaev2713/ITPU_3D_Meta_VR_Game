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
    public sealed class BoardInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator SavedScene_HasOneBoundedBoardWithoutChangingEnvironmentContract()
        {
            yield return LoadScene();
            var environment = FindStable("environment.full.v005").gameObject;
            Assert.That(environment.GetComponentsInChildren<Renderer>(true), Has.Length.EqualTo(626));
            Assert.That(environment.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(549));
            Assert.That(environment.GetComponentsInChildren<CapsuleCollider>(true), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<BoardDrawingController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            var commands = Object.FindObjectsByType<BoardCommandInteractable>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(commands, Has.Length.EqualTo(4));
            Assert.That(commands.Select(item => item.Command).Distinct().Count(), Is.EqualTo(4));
            Assert.That(GameObject.Find("BOARD_INTERACTION_WO28"), Is.Not.Null);
            Assert.That(GameObject.Find("BOARD_DRAW_SURFACE"), Is.Not.Null);
            Assert.That(GameObject.Find("BOARD_UNDO_BUTTON"), Is.Not.Null);
            Assert.That(GameObject.Find("BOARD_COLOR_BUTTON"), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XrLocomotionDriver>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CoffeeInteractionController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(FindStableMatches("FLIPCHART"), Has.Length.EqualTo(1));
            Assert.That(FindStableMatches("MARKER"), Has.Length.EqualTo(1));
            Assert.That(FindStableMatches("ERASER"), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BoardFlow_MarkerEraserUndoSecondOwnerTrackingLossAndResetAreDeterministic()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var board = Object.FindFirstObjectByType<BoardDrawingController>(FindObjectsInactive.Include);
            var reset = Object.FindFirstObjectByType<VerticalSliceReset>(FindObjectsInactive.Include);
            var eraser = Object.FindObjectsByType<BoardCommandInteractable>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Command == BoardCommand.Eraser);
            var undo = Object.FindObjectsByType<BoardCommandInteractable>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Command == BoardCommand.Undo);
            Assert.That(board.RasterTexture.width, Is.EqualTo(BoardDrawingController.RasterWidth));
            Assert.That(board.RasterTexture.height, Is.EqualTo(BoardDrawingController.RasterHeight));
            Assert.That(board.NonEmptyPixels, Is.Zero);

            var left = RayAt(board, 0.15f, 0.5f);
            var right = RayAt(board, 0.85f, 0.5f);
            Assert.That(board.TryBeginStroke("source-a", left), Is.True);
            Assert.That(board.TryBeginStroke("source-b", right), Is.False);
            Assert.That(board.UpdateStroke("source-b", right), Is.False);
            Assert.That(board.UpdateStroke("source-a", right), Is.True);
            Assert.That(board.EndStroke("source-a"), Is.True);
            var markerPixels = board.NonEmptyPixels;
            Assert.That(markerPixels, Is.GreaterThan(100));

            Assert.That(eraser.TryActivate("source-a"), Is.True);
            Assert.That(board.Tool, Is.EqualTo(BoardTool.Eraser));
            Assert.That(board.TryBeginStroke("source-a", RayAt(board, 0.47f, 0.5f)), Is.True);
            Assert.That(board.UpdateStroke("source-a", RayAt(board, 0.53f, 0.5f)), Is.True);
            Assert.That(board.EndStroke("source-a"), Is.True);
            Assert.That(board.NonEmptyPixels, Is.GreaterThan(0).And.LessThan(markerPixels));
            Assert.That(undo.TryActivate("source-a"), Is.True);
            Assert.That(board.NonEmptyPixels, Is.EqualTo(markerPixels));

            var beforeCancelledStroke = board.CaptureFinalCardSnapshot();
            Assert.That(board.TryBeginStroke("source-a", RayAt(board, 0.3f, 0.2f)), Is.True);
            Assert.That(board.UpdateStroke("source-a", RayAt(board, 0.7f, 0.8f)), Is.True);
            Assert.That(board.CancelStroke("source-a"), Is.True);
            Assert.That(board.CaptureFinalCardSnapshot(), Is.EqualTo(beforeCancelledStroke));

            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            Assert.That(board.NonEmptyPixels, Is.Zero);
            Assert.That(board.Owner, Is.Empty);
            Assert.That(board.Tool, Is.EqualTo(BoardTool.Marker));
            Assert.That(board.Undo(), Is.False);
        }

        [UnityTest]
        public IEnumerator ColorCommand_ChangesOnlyFutureMarkerStrokesAndResetReturnsDefault()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var board = Object.FindFirstObjectByType<BoardDrawingController>(FindObjectsInactive.Include);
            var color = Object.FindObjectsByType<BoardCommandInteractable>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(item => item.Command == BoardCommand.ColorNext);
            var reset = Object.FindFirstObjectByType<VerticalSliceReset>(FindObjectsInactive.Include);
            Assert.That(board.MarkerColorIndex, Is.EqualTo(1));
            Assert.That(board.TryBeginStroke("color-test", RayAt(board, 0.2f, 0.3f)), Is.True);
            Assert.That(board.EndStroke("color-test"), Is.True);
            Assert.That(color.TryActivate("color-button"), Is.True);
            Assert.That(board.MarkerColorIndex, Is.EqualTo(2));
            Assert.That(board.TryBeginStroke("color-test", RayAt(board, 0.8f, 0.7f)), Is.True);
            Assert.That(board.EndStroke("color-test"), Is.True);
            var pixels = board.CaptureFinalCardSnapshot();
            Assert.That(pixels.Contains((byte)1), Is.True);
            Assert.That(pixels.Contains((byte)2), Is.True);
            Assert.That(pixels.Count(value => value == 1), Is.GreaterThan(0));
            Assert.That(pixels.Count(value => value == 2), Is.GreaterThan(0));

            for (var index = 0; index < 4; index++) Assert.That(color.TryActivate("cycle"), Is.True);
            Assert.That(board.MarkerColorIndex, Is.EqualTo(1), "palette must wrap from 5 to 1");
            reset.ResetSession(Time.unscaledTimeAsDouble);
            yield return null;
            Assert.That(board.MarkerColorIndex, Is.EqualTo(1));
            Assert.That(board.Tool, Is.EqualTo(BoardTool.Marker));
            Assert.That(board.NonEmptyPixels, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ControllerAndHandDrawing_OutrankTeleportAndTrackingLossCancels()
        {
            yield return LoadScene();
            MakeLifecycleSafe();
            yield return null;
            var board = Object.FindFirstObjectByType<BoardDrawingController>(FindObjectsInactive.Include);
            var priority = Object.FindFirstObjectByType<InteractionPriority>(FindObjectsInactive.Include);
            var teleport = Object.FindFirstObjectByType<HandTeleportIntentAdapter>(FindObjectsInactive.Include);
            var controller = Object.FindObjectsByType<XrControllerPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).First();
            controller.enabled = false;
            var ray = RayAt(board, 0.2f, 0.3f);
            var now = Time.unscaledTimeAsDouble;
            controller.ProcessSample(true, false, false, ray, now);
            controller.ProcessSample(true, true, false, ray, now + 0.05);
            Assert.That(board.Owner, Is.EqualTo(controller.Source));
            Assert.That(priority.PrecisionBusy, Is.True);
            Assert.That(teleport.OwnerSource, Is.Empty);
            controller.ProcessSample(true, false, false, ray, now + 0.1);
            Assert.That(board.Owner, Is.Empty);
            Assert.That(priority.PrecisionBusy, Is.False);
            var committed = board.NonEmptyPixels;

            var hand = Object.FindObjectsByType<XrHandsPointer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).First();
            hand.enabled = false;
            hand.ProcessSample(true, false, false, ray, now + 0.2);
            hand.ProcessSample(true, false, true, RayAt(board, 0.3f, 0.7f), now + 0.25);
            hand.ProcessSample(true, false, true, RayAt(board, 0.7f, 0.7f), now + 0.3);
            Assert.That(board.Owner, Is.EqualTo(hand.Source));
            hand.ProcessSample(false, false, true, default, now + 0.35);
            Assert.That(board.Owner, Is.Empty);
            Assert.That(priority.PrecisionBusy, Is.False);
            Assert.That(board.NonEmptyPixels, Is.EqualTo(committed),
                "tracking loss must cancel the in-progress hand stroke");
        }

        private static Ray RayAt(BoardDrawingController board, float u, float v)
        {
            var collider = board.SurfaceCollider;
            var local = collider.center + new Vector3((u - 0.5f) * collider.size.x,
                (v - 0.5f) * collider.size.y, 0f);
            var point = collider.transform.TransformPoint(local);
            var normal = collider.transform.forward.normalized;
            return new Ray(point + normal * 0.25f, -normal);
        }

        private static IEnumerator LoadScene()
        {
            var operation = SceneManager.LoadSceneAsync("ITPU_403_XR", LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            Physics.SyncTransforms();
        }

        private static Transform FindStable(string value) => FindStableMatches(value).Single().transform;

        private static StableId[] FindStableMatches(string value) =>
            Object.FindObjectsByType<StableId>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Where(item => item.Value == value).ToArray();

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
