using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class XrControllerPointer : MonoBehaviour
    {
        [SerializeField] private bool leftHand;
        [SerializeField] private float maxDistance = 6f;
        [SerializeField] private LayerMask rayMask = ~0;
        [SerializeField] private HandTeleportIntentAdapter teleport;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private XrPointerVisual pointerVisual;

        private readonly List<XRHandSubsystem> handSubsystems = new();
        private InputAction triggerAction;
        private InputAction gripAction;
        private bool pressed;
        private bool gripped;
        private bool neutralRequired = true;
        private bool uiCaptured;
        private XrGrabTarget heldTarget;
        private BoardDrawingController activeBoard;
        private Vector3 previousGripPosition;
        private Quaternion previousGripRotation = Quaternion.identity;
        private double previousGripTime;
        private Vector3 releaseVelocity;
        private Vector3 releaseAngularVelocity;

        public string Source => leftHand ? "left-controller" : "right-controller";
        public InputAction TriggerAction => triggerAction;
        public InputAction GripAction => gripAction;
        public XrPointerVisual PointerVisual => pointerVisual;
        public float MaxDistance => maxDistance;

        public void Configure(bool useLeftHand, HandTeleportIntentAdapter adapter,
            InteractionPriority interactionPriority, XrLifecycleState state)
        {
            leftHand = useLeftHand;
            teleport = adapter;
            priority = interactionPriority;
            lifecycle = state;
            RebuildAction();
        }

        public void ConfigureVisual(XrPointerVisual visual) => pointerVisual = visual;

        private void Awake()
        {
            teleport ??= GetComponentInParent<HandTeleportIntentAdapter>();
            priority ??= GetComponentInParent<InteractionPriority>();
            lifecycle ??= GetComponentInParent<XrLifecycleState>();
            pointerVisual ??= GetComponent<XrPointerVisual>();
            EnsureAction();
        }

        private void OnEnable()
        {
            EnsureAction();
            triggerAction.Enable();
            gripAction.Enable();
        }

        private void OnDisable()
        {
            triggerAction?.Disable();
            gripAction?.Disable();
            teleport?.CancelForSource(Source, "CONTROLLER_POINTER_DISABLED");
            heldTarget?.MarkTrackingLost(Source);
            activeBoard?.CancelStroke(Source);
            activeBoard = null;
            ResetPointerState();
        }

        private void OnDestroy()
        {
            triggerAction?.Dispose();
            gripAction?.Dispose();
        }

        private void Update()
        {
            var tracked = lifecycle != null &&
                          (leftHand ? lifecycle.MoveControllerTracked : lifecycle.TurnControllerTracked);
            ProcessSample(ShouldUseControllerPointerForCurrentSource(tracked), triggerAction.IsPressed(),
                gripAction.IsPressed(), new Ray(transform.position, transform.forward),
                Time.unscaledTimeAsDouble);
        }

        private bool ShouldUseControllerPointerForCurrentSource(bool controllerTracked)
        {
            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);
            foreach (var subsystem in handSubsystems)
            {
                if (!subsystem.running) continue;
                var handedness = leftHand ? Handedness.Left : Handedness.Right;
                var known = subsystem.TryGetExtendedData(handedness, out HandTrackingDataSource source);
                return ShouldUseControllerPointer(known, source, controllerTracked);
            }
            return controllerTracked;
        }

        public static bool ShouldUseControllerPointer(bool sourceKnown, HandTrackingDataSource source,
            bool controllerTracked) => sourceKnown
            ? controllerTracked && source == HandTrackingDataSource.Controller
            : controllerTracked;

        public void ProcessSample(bool tracked, bool samplePressed, Ray ray, double now)
        {
            ProcessSample(tracked, samplePressed, false, ray, now);
        }

        public void ProcessSample(bool tracked, bool samplePressed, bool sampleGripped, Ray ray, double now)
        {
            var wasPressed = pressed;
            var wasGripped = gripped;
            pressed = samplePressed;
            gripped = sampleGripped;
            if (!tracked)
            {
                pointerVisual?.Hide();
                teleport?.CancelForSource(Source, "CONTROLLER_TRACKING_LOST");
                heldTarget?.MarkTrackingLost(Source);
                activeBoard?.CancelStroke(Source);
                activeBoard = null;
                neutralRequired = true;
                uiCaptured = false;
                return;
            }

            var validRay = ray.direction.sqrMagnitude >= 0.9f;
            RaycastHit hit = default;
            var hasHit = validRay && Physics.Raycast(ray, out hit, maxDistance, rayMask,
                QueryTriggerInteraction.Collide);
            var grabTarget = hasHit ? hit.collider.GetComponentInParent<XrGrabTarget>() : null;
            var interactable = hasHit ? hit.collider.GetComponentInParent<DemoPressInteractable>() : null;
            var boardCommand = hasHit ? hit.collider.GetComponentInParent<BoardCommandInteractable>() : null;
            var board = hasHit ? hit.collider.GetComponentInParent<BoardDrawingController>() : null;
            var surface = hasHit ? hit.collider.GetComponentInParent<TeleportSurface>() : null;
            var targetKind = grabTarget != null || interactable != null || boardCommand != null || board != null
                ? XrAimTargetKind.Interactable
                : surface != null && surface.isActiveAndEnabled
                    ? XrAimTargetKind.Teleport
                    : hasHit ? XrAimTargetKind.Invalid : XrAimTargetKind.None;
            pointerVisual?.Present(lifecycle == null || lifecycle.ContextSafe, ray, maxDistance, hasHit,
                hasHit ? hit.point : default, hasHit ? hit.normal : default, targetKind);

            if (activeBoard != null)
            {
                teleport?.CancelForSource(Source, "BOARD_DRAWING_PRIORITY");
                if (!validRay || lifecycle != null && !lifecycle.ContextSafe)
                {
                    activeBoard.CancelStroke(Source);
                    activeBoard = null;
                    neutralRequired = true;
                    return;
                }
                if (pressed)
                {
                    activeBoard.UpdateStroke(Source, ray);
                    return;
                }
                if (wasPressed) activeBoard.EndStroke(Source);
                else activeBoard.CancelStroke(Source);
                activeBoard = null;
                neutralRequired = true;
                return;
            }

            if (heldTarget != null)
            {
                teleport?.CancelForSource(Source, "HELD_OBJECT_PRIORITY");
                if (!validRay)
                {
                    heldTarget.MarkTrackingLost(Source);
                    return;
                }
                if (gripped)
                {
                    var pose = PoseFromRay(ray);
                    UpdateReleaseVelocity(pose, now);
                    heldTarget.UpdateGrab(Source, pose);
                }
                else
                {
                    heldTarget.TryRelease(Source, wasGripped, releaseVelocity, releaseAngularVelocity);
                    heldTarget = null;
                    neutralRequired = true;
                }
                return;
            }

            if (neutralRequired)
            {
                if (!pressed && !gripped && teleport.ObserveNeutral(now, Source)) neutralRequired = false;
                return;
            }
            if (uiCaptured)
            {
                if (!pressed)
                {
                    uiCaptured = false;
                    neutralRequired = true;
                }
                return;
            }

            if (!hasHit)
            {
                if (teleport.IsOwnedBy(Source)) teleport.CancelForSource(Source, "NO_TARGET");
                return;
            }

            if (grabTarget != null)
            {
                teleport.CancelForSource(Source, "GRAB_PRIORITY");
                if (pressed && !wasPressed && teleport.CanDirectInteract(true, false) &&
                    grabTarget.TryPrimaryActivate(Source + "-trigger"))
                {
                    uiCaptured = true;
                    return;
                }
                if (gripped && !wasGripped && teleport.CanDirectInteract(true, false))
                {
                    var pose = PoseFromRay(ray);
                    if (grabTarget.TryBeginGrab(Source, pose))
                    {
                        heldTarget = grabTarget;
                        releaseVelocity = Vector3.zero;
                        releaseAngularVelocity = Vector3.zero;
                        previousGripPosition = pose.position;
                        previousGripRotation = pose.rotation;
                        previousGripTime = now;
                    }
                }
                return;
            }

            if (interactable != null)
            {
                teleport.CancelForSource(Source, "INTERACTABLE_PRIORITY");
                if (pressed && !wasPressed && teleport.CanDirectInteract(true, false))
                {
                    uiCaptured = true;
                    priority.SetUiBusy(true);
                    try { interactable.TryActivate(Source + "-trigger"); }
                    finally { priority.SetUiBusy(false); }
                }
                return;
            }

            if (boardCommand != null)
            {
                teleport.CancelForSource(Source, "BOARD_COMMAND_PRIORITY");
                if (pressed && !wasPressed && teleport.CanDirectInteract(true, false))
                {
                    uiCaptured = true;
                    priority.SetUiBusy(true);
                    try { boardCommand.TryActivate(Source + "-trigger"); }
                    finally { priority.SetUiBusy(false); }
                }
                return;
            }

            if (board != null)
            {
                teleport.CancelForSource(Source, "BOARD_DRAWING_PRIORITY");
                if (pressed && !wasPressed && teleport.CanDirectInteract(true, false) &&
                    board.TryBeginStroke(Source, ray))
                    activeBoard = board;
                return;
            }

            if (surface == null || !surface.isActiveAndEnabled)
            {
                if (teleport.IsOwnedBy(Source)) teleport.CancelForSource(Source, "NON_TELEPORT_SURFACE");
                return;
            }
            var anchor = surface.GetComponent<StableId>()?.Value ?? surface.name;
            var pathClear = teleport.PathClearFromHead(ray.origin);
            if (!pressed)
            {
                if (teleport.IsOwnedBy(Source))
                {
                    teleport.ReleaseAndCommit(now, Source, true, false, pathClear,
                        surface.isActiveAndEnabled);
                    neutralRequired = true;
                }
                else teleport.ObserveNeutral(now, Source);
                return;
            }
            if (!wasPressed)
            {
                if (!teleport.BeginIntent(now, anchor, Source, hit.point, hit.collider, true, false,
                        pathClear, surface.isActiveAndEnabled))
                    neutralRequired = true;
            }
            else if (teleport.IsOwnedBy(Source))
            {
                teleport.UpdateIntent(now, Source, hit.point, true, false, pathClear,
                    surface.isActiveAndEnabled);
            }
        }

        public void ResetPointerState()
        {
            activeBoard?.CancelStroke(Source);
            activeBoard = null;
            pressed = false;
            gripped = false;
            neutralRequired = true;
            uiCaptured = false;
            pointerVisual?.Hide();
        }

        private void EnsureAction()
        {
            if (triggerAction == null || gripAction == null) RebuildAction();
        }

        private void RebuildAction()
        {
            var wasEnabled = triggerAction?.enabled == true;
            triggerAction?.Disable();
            triggerAction?.Dispose();
            gripAction?.Disable();
            gripAction?.Dispose();
            triggerAction = new InputAction(Source + "-select", InputActionType.Button);
            var usage = leftHand ? "LeftHand" : "RightHand";
            triggerAction.AddBinding($"<XRController>{{{usage}}}/triggerPressed");
            triggerAction.AddBinding($"<XRController>{{{usage}}}/trigger");
            gripAction = new InputAction(Source + "-grab", InputActionType.Button);
            gripAction.AddBinding($"<XRController>{{{usage}}}/gripPressed");
            gripAction.AddBinding($"<XRController>{{{usage}}}/grip");
            if (wasEnabled || isActiveAndEnabled)
            {
                triggerAction.Enable();
                gripAction.Enable();
            }
        }

        private static Pose PoseFromRay(Ray ray)
        {
            var rotation = ray.direction.sqrMagnitude < 0.9f
                ? Quaternion.identity
                : Quaternion.LookRotation(ray.direction.normalized, Vector3.up);
            return new Pose(ray.origin, rotation);
        }

        private void UpdateReleaseVelocity(Pose pose, double now)
        {
            var dt = now - previousGripTime;
            if (dt > 0.001 && dt <= 0.1)
            {
                releaseVelocity = Vector3.ClampMagnitude((pose.position - previousGripPosition) / (float)dt, 4f);
                var delta = pose.rotation * Quaternion.Inverse(previousGripRotation);
                delta.ToAngleAxis(out var angle, out var axis);
                if (angle > 180f) angle -= 360f;
                releaseAngularVelocity = axis.sqrMagnitude < 0.5f
                    ? Vector3.zero
                    : Vector3.ClampMagnitude(axis.normalized * (angle * Mathf.Deg2Rad / (float)dt), 12f);
            }
            previousGripPosition = pose.position;
            previousGripRotation = pose.rotation;
            previousGripTime = now;
        }
    }
}
