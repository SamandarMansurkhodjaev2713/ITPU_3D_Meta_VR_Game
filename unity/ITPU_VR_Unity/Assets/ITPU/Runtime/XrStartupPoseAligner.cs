using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class XrStartupPoseAligner : MonoBehaviour
    {
        [SerializeField] private XROrigin origin;
        [SerializeField] private Camera headCamera;
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private TrackedPoseDriver headPoseDriver;
        [SerializeField] private Vector3 targetHeadFloorPosition = new(-3.20f, 0f, -10.70f);
        [SerializeField] private Vector3 targetHeadForward = new(0.17364818f, 0f, -0.98480775f);
        private bool aligned;
        private string lastGateReason = "";
        private InputAction isTrackedAction;
        private readonly List<XRInputSubsystem> inputSubsystems = new();
        private TrackingOriginModeFlags settlingMode;
        private int settlingFrame = -1;
        private int settlingSamples;
        private const int RequiredStableFrames = 3;

        public bool IsAligned => aligned;
        public string LastGateReason => lastGateReason;
        public TrackingOriginModeFlags ActualTrackingOriginMode { get; private set; }
        public TrackingOriginModeFlags SupportedTrackingOriginModes { get; private set; }

        public void Configure(XROrigin xrOrigin, Camera camera, XrLifecycleState state,
            TrackedPoseDriver poseDriver, Vector3 headFloorPosition, Vector3 headForward)
        {
            origin = xrOrigin;
            headCamera = camera;
            lifecycle = state;
            headPoseDriver = poseDriver;
            targetHeadFloorPosition = headFloorPosition;
            targetHeadForward = headForward;
            aligned = false;
            ResetSettle();
        }

        private void Awake()
        {
            if (origin == null) origin = GetComponent<XROrigin>();
            if (headCamera == null && origin != null) headCamera = origin.Camera;
            if (lifecycle == null) lifecycle = GetComponent<XrLifecycleState>();
            if (headPoseDriver == null && headCamera != null)
                headPoseDriver = headCamera.GetComponent<TrackedPoseDriver>();
        }

        private void LateUpdate() => TryAlignTrackedPose();

        private void OnEnable()
        {
            EnsureTrackingAction();
            isTrackedAction.Enable();
            Application.onBeforeRender += AlignBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= AlignBeforeRender;
            isTrackedAction?.Disable();
        }

        private void OnDestroy() => isTrackedAction?.Dispose();

        private void AlignBeforeRender() => TryAlignTrackedPose();

        public void Rearm()
        {
            aligned = false;
            lastGateReason = "";
            ResetSettle();
            GetComponent<XrLocomotionDriver>()?.BeginStartupPlacement();
        }

        public bool TryAlignTrackedPose()
        {
            if (aligned) return false;
            if (origin == null || headCamera == null || headPoseDriver == null)
                return Wait("MISSING_REFERENCE");
            if (lifecycle != null && (!lifecycle.Focused || lifecycle.Paused))
                return Wait(lifecycle.Paused ? "PAUSED" : "NOT_FOCUSED");

            var trackingAction = headPoseDriver.trackingStateInput.action;
            EnsureTrackingAction();
            if (trackingAction == null || !trackingAction.enabled || !isTrackedAction.enabled)
                return Wait("POSE_ACTION_DISABLED");
            var trackingState = (InputTrackingState)trackingAction.ReadValue<int>();
            var inputSystemTracked = isTrackedAction.ReadValue<float>() > 0.5f;
            if (!TryGetActualTrackingMode(out var actualMode, out var supportedModes))
            {
                ResetSettle();
                return Wait("NO_RUNNING_XR_INPUT_SUBSYSTEM");
            }
            ActualTrackingOriginMode = actualMode;
            SupportedTrackingOriginModes = supportedModes;
            return TryAlignTrackedPoseFromInputState(trackingState, inputSystemTracked,
                actualMode, Time.frameCount);
        }

        private bool TryAlignTrackedPoseFromInputState(InputTrackingState trackingState,
            bool inputSystemTracked, TrackingOriginModeFlags actualMode, int frame)
        {
            if (aligned) return false;
            if (origin == null || headCamera == null || headPoseDriver == null)
                return Wait("MISSING_REFERENCE");
            if (lifecycle != null && (!lifecycle.Focused || lifecycle.Paused))
            {
                ResetSettle();
                return Wait(lifecycle.Paused ? "PAUSED" : "NOT_FOCUSED");
            }
            const InputTrackingState required = InputTrackingState.Position | InputTrackingState.Rotation;
            if ((trackingState & required) != required)
            {
                ResetSettle();
                return Wait("POSE_NOT_TRACKED_" + (int)trackingState);
            }
            if (actualMode != TrackingOriginModeFlags.Floor &&
                actualMode != TrackingOriginModeFlags.Device &&
                actualMode != TrackingOriginModeFlags.Unbounded)
            {
                ResetSettle();
                return Wait("TRACKING_ORIGIN_NOT_READY_" + actualMode);
            }
            if (!ObserveStableFrame(actualMode, frame))
                return Wait("POSE_SETTLING_" + settlingSamples + "_OF_" + RequiredStableFrames);

            var horizontalForward = Vector3.ProjectOnPlane(targetHeadForward, Vector3.up);
            if (horizontalForward.sqrMagnitude < 0.0001f) return Wait("INVALID_TARGET_FORWARD");
            horizontalForward.Normalize();

            var offset = origin.CameraFloorOffsetObject == null
                ? null
                : origin.CameraFloorOffsetObject.transform;
            if (offset == null) return Wait("MISSING_CAMERA_OFFSET");

            var rawHeadInOffset = offset.InverseTransformPoint(headCamera.transform.position);
            if (!Finite(rawHeadInOffset)) return Wait("NONFINITE_HEAD_POSE");
            var cachedTrackingMode = origin.CurrentTrackingOriginMode;
            var originBefore = origin.transform.position;
            var rotationBefore = origin.transform.rotation;
            var headBefore = headCamera.transform.position;
            var offsetBefore = offset.localPosition;
            var committed = false;

            var controller = GetComponent<CharacterController>();
            var controllerWasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            try
            {
                // Turn the initial absolute Guardian pose into a session-relative pose.
                // Subsequent physical movement remains available because only the first
                // tracked X/Z sample is cancelled on the Camera Offset parent.
                var offsetPosition = offset.localPosition;
                offsetPosition.x = -rawHeadInOffset.x;
                offsetPosition.z = -rawHeadInOffset.z;
                // Floor poses already contain physical head height. Device/Unbounded
                // poses are eye-relative and use the configured standing fallback.
                // For an early Unknown mode, infer from the raw tracked Y sample.
                offsetPosition.y = actualMode switch
                {
                    TrackingOriginModeFlags.Floor => 0f,
                    TrackingOriginModeFlags.Device => origin.CameraYOffset,
                    TrackingOriginModeFlags.Unbounded => origin.CameraYOffset,
                    _ => throw new System.InvalidOperationException("Tracking origin was validated before commit."),
                };
                offset.localPosition = offsetPosition;

                if (!origin.MatchOriginUpCameraForward(Vector3.up, horizontalForward))
                    return Wait("ROTATION_ALIGN_FAILED");
                var currentHead = headCamera.transform.position;
                var targetHead = new Vector3(targetHeadFloorPosition.x, currentHead.y,
                    targetHeadFloorPosition.z);
                if (!origin.MoveCameraToWorldLocation(targetHead)) return Wait("POSITION_ALIGN_FAILED");
                Physics.SyncTransforms();
                aligned = true;
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    origin.transform.SetPositionAndRotation(originBefore, rotationBefore);
                    offset.localPosition = offsetBefore;
                    Physics.SyncTransforms();
                }
                if (controller != null) controller.enabled = controllerWasEnabled;
            }

            var locomotion = GetComponent<XrLocomotionDriver>();
            locomotion?.SyncCapsuleToHead();
            locomotion?.CompleteStartupPlacement();
            Debug.Log("ITPU_XR_STARTUP_POSE ALIGNED" +
                      " trackingState=" + (int)trackingState +
                      " isTracked=" + inputSystemTracked +
                      " actualMode=" + actualMode +
                      " cachedMode=" + cachedTrackingMode +
                      " supportedModes=" + SupportedTrackingOriginModes +
                      " raw=" + Format(rawHeadInOffset) +
                      " offsetBefore=" + Format(offsetBefore) +
                      " originBefore=" + Format(originBefore) +
                      " headBefore=" + Format(headBefore) +
                      " offsetAfter=" + Format(offset.localPosition) +
                      " originAfter=" + Format(origin.transform.position) +
                      " headAfter=" + Format(headCamera.transform.position) +
                      " forwardAfter=" + Format(headCamera.transform.forward));
            return true;
        }

        private bool ObserveStableFrame(TrackingOriginModeFlags mode, int frame)
        {
            if (mode != settlingMode)
            {
                settlingMode = mode;
                settlingFrame = frame;
                settlingSamples = 1;
                return false;
            }
            if (frame == settlingFrame) return settlingSamples >= RequiredStableFrames;
            settlingFrame = frame;
            settlingSamples++;
            return settlingSamples >= RequiredStableFrames;
        }

        private void ResetSettle()
        {
            settlingMode = default;
            settlingFrame = -1;
            settlingSamples = 0;
        }

        private bool TryGetActualTrackingMode(out TrackingOriginModeFlags actualMode,
            out TrackingOriginModeFlags supportedModes)
        {
            actualMode = default;
            supportedModes = default;
            inputSubsystems.Clear();
            SubsystemManager.GetSubsystems(inputSubsystems);
            foreach (var subsystem in inputSubsystems)
            {
                if (subsystem == null || !subsystem.running) continue;
                actualMode = subsystem.GetTrackingOriginMode();
                supportedModes = subsystem.GetSupportedTrackingOriginModes();
                return true;
            }
            return false;
        }

        private bool Wait(string reason)
        {
            if (lastGateReason == reason) return false;
            lastGateReason = reason;
            Debug.Log("ITPU_XR_STARTUP_POSE WAIT reason=" + reason);
            return false;
        }

        private void EnsureTrackingAction()
        {
            isTrackedAction ??= new InputAction("HMD Is Tracked", InputActionType.Button,
                "<XRHMD>/isTracked");
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static string Format(Vector3 value) =>
            $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }
}
