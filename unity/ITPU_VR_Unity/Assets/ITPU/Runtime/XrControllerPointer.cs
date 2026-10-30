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
        private bool pressed;
        private bool neutralRequired = true;
        private bool uiCaptured;

        public string Source => leftHand ? "left-controller" : "right-controller";
        public InputAction TriggerAction => triggerAction;
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
        }

        private void OnDisable()
        {
            triggerAction?.Disable();
            teleport?.CancelForSource(Source, "CONTROLLER_POINTER_DISABLED");
            ResetPointerState();
        }

        private void OnDestroy() => triggerAction?.Dispose();

        private void Update()
        {
            var tracked = lifecycle != null &&
                          (leftHand ? lifecycle.MoveControllerTracked : lifecycle.TurnControllerTracked);
            ProcessSample(ShouldUseControllerPointerForCurrentSource(tracked), triggerAction.IsPressed(),
                new Ray(transform.position, transform.forward),
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
            var wasPressed = pressed;
            pressed = samplePressed;
            if (!tracked)
            {
                pointerVisual?.Hide();
                teleport.CancelForSource(Source, "CONTROLLER_TRACKING_LOST");
                neutralRequired = true;
                uiCaptured = false;
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
            pointerVisual?.Present(lifecycle == null || lifecycle.ContextSafe, ray, maxDistance, hasHit,
                hasHit ? hit.point : default, hasHit ? hit.normal : default, targetKind);

            if (neutralRequired)
            {
                if (!pressed && teleport.ObserveNeutral(now, Source)) neutralRequired = false;
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
            pressed = false;
            neutralRequired = true;
            uiCaptured = false;
            pointerVisual?.Hide();
        }

        private void EnsureAction()
        {
            if (triggerAction == null) RebuildAction();
        }

        private void RebuildAction()
        {
            var wasEnabled = triggerAction?.enabled == true;
            triggerAction?.Disable();
            triggerAction?.Dispose();
            triggerAction = new InputAction(Source + "-select", InputActionType.Button);
            var usage = leftHand ? "LeftHand" : "RightHand";
            triggerAction.AddBinding($"<XRController>{{{usage}}}/triggerPressed");
            triggerAction.AddBinding($"<XRController>{{{usage}}}/trigger");
            if (wasEnabled || isActiveAndEnabled) triggerAction.Enable();
        }
    }
}
