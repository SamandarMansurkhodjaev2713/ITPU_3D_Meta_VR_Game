using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class XrHandsPointer : MonoBehaviour
    {
        [SerializeField] private bool leftHand;
        [SerializeField] private float maxDistance = 6f;
        [SerializeField] private float pinchOnDistance = 0.025f;
        [SerializeField] private float pinchOffDistance = 0.04f;
        [SerializeField] private float hoverBeforeIntent = 0.15f;
        [SerializeField] private LayerMask rayMask = ~0;
        [SerializeField] private HandTeleportIntentAdapter teleport;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private Transform trackingSpace;
        [SerializeField] private XrPointerVisual pointerVisual;

        private readonly List<XRHandSubsystem> subsystems = new();
        private bool pinched;
        private bool neutralRequired = true;
        private bool uiCaptured;
        private string hoverAnchor = "";
        private double hoverBeganAt;

        public string Source => leftHand ? "left-hand" : "right-hand";
        public XrPointerVisual PointerVisual => pointerVisual;
        public float MaxDistance => maxDistance;

        public void Configure(bool useLeftHand, HandTeleportIntentAdapter adapter,
            InteractionPriority interactionPriority, XrLifecycleState state, Transform xrTrackingSpace)
        {
            leftHand = useLeftHand;
            teleport = adapter;
            priority = interactionPriority;
            lifecycle = state;
            trackingSpace = xrTrackingSpace;
        }

        public void ConfigureVisual(XrPointerVisual visual) => pointerVisual = visual;

        private void Awake()
        {
            teleport ??= GetComponent<HandTeleportIntentAdapter>();
            priority ??= GetComponent<InteractionPriority>();
            lifecycle ??= GetComponent<XrLifecycleState>();
            trackingSpace ??= transform;
            pointerVisual ??= GetComponent<XrPointerVisual>();
        }

        private void OnDisable()
        {
            teleport?.CancelForSource(Source, "POINTER_DISABLED");
            ResetPointerState();
        }

        private void Update()
        {
            var subsystem = GetRunningSubsystem();
            if (subsystem == null)
            {
                ProcessSample(false, false, false, default, Time.unscaledTimeAsDouble);
                return;
            }
            if (UsesControllerData(subsystem))
            {
                ProcessSample(false, false, false, default, Time.unscaledTimeAsDouble);
                return;
            }
            var hand = leftHand ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked || !TryWorldPose(hand, XRHandJointID.Wrist, out var wrist) ||
                !TryWorldPose(hand, XRHandJointID.IndexTip, out var index) ||
                !TryWorldPose(hand, XRHandJointID.ThumbTip, out var thumb))
            {
                ProcessSample(false, false, false, default, Time.unscaledTimeAsDouble);
                return;
            }

            var pinchDistance = Vector3.Distance(index.position, thumb.position);
            var samplePinched = pinched ? pinchDistance < pinchOffDistance : pinchDistance <= pinchOnDistance;
            Ray ray;
            if (!TryMetaAimRay(out ray))
            {
                var direction = (index.position - wrist.position).normalized;
                ray = new Ray(index.position, direction.sqrMagnitude < 0.9f ? Vector3.zero : direction);
            }
            ProcessSample(true, ReadSystemGesture(), samplePinched, ray, Time.unscaledTimeAsDouble);
        }

        public void ProcessSample(bool tracked, bool systemGesture, bool samplePinched, Ray ray, double now)
        {
            var wasPinched = pinched;
            pinched = samplePinched;
            if (!tracked)
            {
                LoseTracking();
                return;
            }

            var validRay = ray.direction.sqrMagnitude >= 0.9f;
            RaycastHit hit = default;
            var hasHit = validRay && Physics.Raycast(ray, out hit, maxDistance, rayMask,
                QueryTriggerInteraction.Collide);
            var interactable = hasHit ? hit.collider.GetComponentInParent<DemoPressInteractable>() : null;
            var surface = hasHit ? hit.collider.GetComponentInParent<TeleportSurface>() : null;
            var targetKind = interactable != null
                ? XrAimTargetKind.Interactable
                : surface != null && surface.isActiveAndEnabled
                    ? XrAimTargetKind.Teleport
                    : hasHit ? XrAimTargetKind.Invalid : XrAimTargetKind.None;
            pointerVisual?.Present(!systemGesture && (lifecycle == null || lifecycle.ContextSafe), ray,
                maxDistance, hasHit, hasHit ? hit.point : default, hasHit ? hit.normal : default,
                targetKind);

            if (neutralRequired)
            {
                if (!pinched && teleport.ObserveNeutral(now, Source)) neutralRequired = false;
                return;
            }
            if (uiCaptured)
            {
                if (!pinched)
                {
                    uiCaptured = false;
                    neutralRequired = true;
                }
                return;
            }

            if (!hasHit)
            {
                hoverAnchor = "";
                if (teleport.IsOwnedBy(Source)) teleport.CancelForSource(Source, "NO_TARGET");
                if (!pinched) teleport.ObserveNeutral(now, Source);
                return;
            }

            if (interactable != null)
            {
                hoverAnchor = "";
                teleport.CancelForSource(Source, "INTERACTABLE_PRIORITY");
                if (pinched && !wasPinched && teleport.CanDirectInteract(true, systemGesture))
                {
                    uiCaptured = true;
                    priority.SetUiBusy(true);
                    try { interactable.TryActivate(Source + "-pinch"); }
                    finally { priority.SetUiBusy(false); }
                }
                return;
            }

            if (surface == null || !surface.isActiveAndEnabled)
            {
                hoverAnchor = "";
                if (teleport.IsOwnedBy(Source)) teleport.CancelForSource(Source, "NON_TELEPORT_SURFACE");
                return;
            }

            var anchor = surface.GetComponent<StableId>()?.Value ?? surface.name;
            var pathClear = teleport.PathClearFromHead(ray.origin);
            if (!pinched)
            {
                if (teleport.IsOwnedBy(Source))
                {
                    teleport.ReleaseAndCommit(now, Source, true, systemGesture, pathClear,
                        surface.isActiveAndEnabled);
                    neutralRequired = true;
                    hoverAnchor = "";
                    return;
                }
                if (anchor != hoverAnchor)
                {
                    hoverAnchor = anchor;
                    hoverBeganAt = now;
                }
                teleport.ObserveNeutral(now, Source);
                return;
            }

            if (!wasPinched)
            {
                if (anchor != hoverAnchor || now - hoverBeganAt + 1e-9 < hoverBeforeIntent)
                {
                    neutralRequired = true;
                    return;
                }
                if (!teleport.BeginIntent(now, anchor, Source, hit.point, hit.collider, true,
                        systemGesture, pathClear, surface.isActiveAndEnabled))
                    neutralRequired = true;
            }
            else if (teleport.IsOwnedBy(Source))
            {
                teleport.UpdateIntent(now, Source, hit.point, true, systemGesture, pathClear,
                    surface.isActiveAndEnabled);
            }
        }

        public void ResetPointerState()
        {
            pinched = false;
            neutralRequired = true;
            uiCaptured = false;
            hoverAnchor = "";
            hoverBeganAt = 0;
            pointerVisual?.Hide();
        }

        public static Pose TrackingToWorldPose(Transform space, Pose trackingPose)
        {
            if (space == null) return trackingPose;
            return new Pose(space.TransformPoint(trackingPose.position), space.rotation * trackingPose.rotation);
        }

        private XRHandSubsystem GetRunningSubsystem()
        {
            subsystems.Clear();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
                if (subsystem.running) return subsystem;
            return null;
        }

        private bool TryWorldPose(XRHand hand, XRHandJointID id, out Pose pose)
        {
            if (!hand.GetJoint(id).TryGetPose(out var trackingPose))
            {
                pose = default;
                return false;
            }
            pose = TrackingToWorldPose(trackingSpace, trackingPose);
            return true;
        }

        private bool ReadSystemGesture()
        {
            var aim = leftHand ? MetaAimHand.left : MetaAimHand.right;
            if (aim?.aimFlags == null) return false;
            var flags = (MetaAimFlags)(ulong)aim.aimFlags.ReadValue();
            return (flags & (MetaAimFlags.SystemGesture | MetaAimFlags.MenuPressed)) != 0;
        }

        private bool TryMetaAimRay(out Ray ray)
        {
            var aim = leftHand ? MetaAimHand.left : MetaAimHand.right;
            if (aim?.aimFlags == null || aim.devicePosition == null || aim.deviceRotation == null)
            {
                ray = default;
                return false;
            }
            var flags = (MetaAimFlags)(ulong)aim.aimFlags.ReadValue();
            if ((flags & MetaAimFlags.Valid) == 0)
            {
                ray = default;
                return false;
            }
            var localPose = new Pose(aim.devicePosition.ReadValue(), aim.deviceRotation.ReadValue());
            var worldPose = TrackingToWorldPose(trackingSpace, localPose);
            ray = new Ray(worldPose.position, worldPose.rotation * Vector3.forward);
            return ray.direction.sqrMagnitude >= 0.9f;
        }

        private bool UsesControllerData(XRHandSubsystem subsystem)
        {
            var handedness = leftHand ? Handedness.Left : Handedness.Right;
            var sourceKnown = subsystem.TryGetExtendedData(handedness, out HandTrackingDataSource source);
            var controllerTracked = lifecycle != null &&
                                    (leftHand ? lifecycle.MoveControllerTracked : lifecycle.TurnControllerTracked);
            return !ShouldUseOpticalPointer(sourceKnown, source, controllerTracked);
        }

        public static bool ShouldUseOpticalPointer(bool sourceKnown, HandTrackingDataSource source,
            bool controllerTracked) => sourceKnown
            ? source != HandTrackingDataSource.Controller
            : !controllerTracked;

        private void LoseTracking()
        {
            teleport?.CancelForSource(Source, "HAND_TRACKING_LOST");
            pinched = false;
            neutralRequired = true;
            uiCaptured = false;
            hoverAnchor = "";
            pointerVisual?.Hide();
        }
    }
}
