using System;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class VerticalSliceReset : MonoBehaviour
    {
        [SerializeField] private XrLocomotionDriver locomotion;
        [SerializeField] private HandTeleportIntentAdapter teleport;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private DemoPressInteractable[] interactables;
        [SerializeField] private XrHandsPointer[] handPointers;
        [SerializeField] private XrControllerPointer[] controllerPointers;
        [SerializeField] private XrStartupPoseAligner startupPoseAligner;
        [SerializeField] private LocomotionTurnModeControl turnModeControl;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private int lastResetFrame = -1;

        public int SessionRevision { get; private set; } = 1;
        public event Action ResetPerformed;

        public void Configure(XrLocomotionDriver driver, HandTeleportIntentAdapter adapter,
            InteractionPriority interactionPriority, DemoPressInteractable[] resettableInteractables,
            XrHandsPointer[] hands, XrControllerPointer[] controllers,
            XrStartupPoseAligner poseAligner = null, LocomotionTurnModeControl modeControl = null)
        {
            locomotion = driver;
            teleport = adapter;
            priority = interactionPriority;
            interactables = resettableInteractables;
            handPointers = hands;
            controllerPointers = controllers;
            startupPoseAligner = poseAligner;
            turnModeControl = modeControl;
            CaptureInitialPose();
        }

        private void Awake() => CaptureInitialPose();

        private void CaptureInitialPose()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        public void ResetSession(double now)
        {
            if (lastResetFrame == Time.frameCount) return;
            lastResetFrame = Time.frameCount;
            checked { SessionRevision++; }
            priority?.ResetTransient();
            teleport?.ResetState(now, SessionRevision);
            if (turnModeControl != null) turnModeControl.ResetToDefault();
            else locomotion?.SetSmoothTurnEnabled(false);
            locomotion?.ResetPolicy();
            foreach (var pointer in handPointers ?? System.Array.Empty<XrHandsPointer>())
                pointer?.ResetPointerState();
            foreach (var pointer in controllerPointers ?? System.Array.Empty<XrControllerPointer>())
                pointer?.ResetPointerState();

            var controller = GetComponent<CharacterController>();
            var wasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            transform.SetPositionAndRotation(initialPosition, initialRotation);
            if (controller != null) controller.enabled = wasEnabled;
            startupPoseAligner?.Rearm();
            startupPoseAligner?.TryAlignTrackedPose();
            locomotion?.SyncCapsuleToHead();
            foreach (var interactable in interactables ?? System.Array.Empty<DemoPressInteractable>())
                interactable?.ResetState();
            ResetPerformed?.Invoke();
        }

        public void AddResettableInteractables(params DemoPressInteractable[] additions)
        {
            if (additions == null || additions.Length == 0) return;
            var existing = interactables ?? Array.Empty<DemoPressInteractable>();
            var merged = new DemoPressInteractable[existing.Length + additions.Length];
            var count = 0;
            foreach (var item in existing)
                if (item != null && Array.IndexOf(merged, item, 0, count) < 0) merged[count++] = item;
            foreach (var item in additions)
                if (item != null && Array.IndexOf(merged, item, 0, count) < 0) merged[count++] = item;
            if (count != merged.Length) Array.Resize(ref merged, count);
            interactables = merged;
        }
    }
}
