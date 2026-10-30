using System.Collections.Generic;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class HandTeleportIntentAdapter : MonoBehaviour
    {
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private XrLocomotionDriver locomotion;
        [SerializeField] private Camera headCamera;
        [SerializeField] private Transform targetVisual;
        [SerializeField] private LayerMask obstacleMask = Physics.DefaultRaycastLayers;
        [SerializeField] private float destinationDriftTolerance = 0.12f;

        private readonly HashSet<string> neutralSources = new();
        private readonly RaycastHit[] pathHits = new RaycastHit[16];
        private TeleportIntentGate gate;
        private TeleportFacts currentFacts;
        private TeleportIntent pending;
        private string ownerSource;
        private string frozenAnchor;
        private Vector3 frozenTarget;
        private Collider frozenSurface;
        private int sessionRevision = 1;
        private bool subscribed;
        private double ownerBeganAt;

        public TeleportIntentState State => gate?.State ?? TeleportIntentState.Idle;
        public string LastReason => gate?.Reason ?? "NOT_INITIALIZED";
        public string OwnerSource => ownerSource ?? "";
        public Vector3 FrozenTarget => frozenTarget;

        public void Configure(XrLifecycleState state, InteractionPriority interactionPriority,
            XrLocomotionDriver locomotionDriver, Camera camera, Transform visual = null)
        {
            Unsubscribe();
            lifecycle = state;
            priority = interactionPriority;
            locomotion = locomotionDriver;
            headCamera = camera;
            targetVisual = visual;
            EnsureReady();
            if (isActiveAndEnabled) Subscribe();
            SetVisual(false);
        }

        private void Awake() => EnsureReady();

        private void EnsureReady()
        {
            gate ??= new TeleportIntentGate();
            lifecycle ??= GetComponent<XrLifecycleState>();
            priority ??= GetComponent<InteractionPriority>();
            locomotion ??= GetComponent<XrLocomotionDriver>();
            if (headCamera == null) headCamera = Camera.main;
        }

        private void OnEnable()
        {
            EnsureReady();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            Cancel("ADAPTER_DISABLED", true);
        }

        private void Subscribe()
        {
            if (subscribed) return;
            if (lifecycle != null) lifecycle.Changed += OnContextChanged;
            if (priority != null) priority.Changed += OnContextChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (lifecycle != null) lifecycle.Changed -= OnContextChanged;
            if (priority != null) priority.Changed -= OnContextChanged;
            subscribed = false;
        }

        public bool IsOwnedBy(string source) => !string.IsNullOrEmpty(source) && ownerSource == source;

        public bool CanDirectInteract(bool sourceTracked, bool systemGesture)
        {
            EnsureReady();
            return isActiveAndEnabled && sourceTracked && !systemGesture && lifecycle != null &&
                   lifecycle.ContextSafe && priority != null && !priority.UiBusy && !priority.Modal && !priority.PrecisionBusy &&
                   !priority.HoldingObject && !priority.TeleportActive;
        }

        public bool ObserveNeutral(double now, string source)
        {
            EnsureReady();
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(source) || ownerSource != null) return false;
            if (gate.State != TeleportIntentState.Idle && !gate.Neutral(now)) return false;
            neutralSources.Add(source);
            return true;
        }

        public bool BeginIntent(double now, string anchor, string source, Vector3 floorTarget,
            Collider surfaceCollider, bool sourceTracked, bool systemGesture, bool pathClear,
            bool targetEnabled)
        {
            EnsureReady();
            if (!isActiveAndEnabled || ownerSource != null || !neutralSources.Remove(source) ||
                !CanIntentActuate(sourceTracked, systemGesture) || surfaceCollider == null)
                return false;

            frozenAnchor = anchor;
            frozenTarget = floorTarget;
            frozenSurface = surfaceCollider;
            currentFacts = BuildFacts(anchor, source, floorTarget, sourceTracked, systemGesture,
                pathClear, targetEnabled);
            if (!gate.Aim(now, currentFacts))
            {
                ClearOwner(false);
                return false;
            }

            ownerSource = source;
            ownerBeganAt = now;
            priority.SetTeleportActive(true);
            SetVisual(true);
            return true;
        }

        public bool UpdateIntent(double now, string source, Vector3 currentTarget, bool sourceTracked,
            bool systemGesture, bool pathClear, bool targetEnabled)
        {
            EnsureReady();
            if (!isActiveAndEnabled || !IsOwnedBy(source)) return false;
            if (Vector3.Distance(currentTarget, frozenTarget) > destinationDriftTolerance)
            {
                CancelForSource(source, "DESTINATION_MOVED");
                return false;
            }
            if (!CanIntentActuate(sourceTracked, systemGesture) || !pathClear || !targetEnabled ||
                locomotion == null || !locomotion.CanTeleportTo(frozenTarget, frozenSurface))
            {
                CancelForSource(source, "CONTEXT_CHANGED");
                return false;
            }
            if (!gate.Tick(now, currentFacts))
            {
                ClearOwner(false);
                return false;
            }
            if (gate.State == TeleportIntentState.Aim && now - ownerBeganAt + 1e-9 >= 0.15)
                gate.Press(now, currentFacts);
            return gate.State == TeleportIntentState.Aim || gate.State == TeleportIntentState.Armed;
        }

        public bool ReleaseAndCommit(double now, string source, bool sourceTracked, bool systemGesture,
            bool pathClear, bool targetEnabled)
        {
            EnsureReady();
            if (!isActiveAndEnabled || !IsOwnedBy(source)) return false;
            currentFacts = BuildFacts(frozenAnchor, source, frozenTarget, sourceTracked, systemGesture,
                pathClear, targetEnabled);
            if (!CanIntentActuate(sourceTracked, systemGesture) || !gate.Tick(now, currentFacts) ||
                gate.State != TeleportIntentState.Armed)
            {
                CancelForSource(source, "RELEASE_CONTEXT_INVALID");
                return false;
            }

            pending = gate.Release(now, currentFacts);
            if (pending == null)
            {
                ClearOwner(false);
                return false;
            }
            // The single mover validates the actual post-root capsule before the semantic commit.
            if (!locomotion.CanTeleportTo(frozenTarget, frozenSurface))
            {
                CancelForSource(source, "MOVER_REJECTED");
                return false;
            }
            if (!gate.Commit(now, pending, currentFacts))
            {
                ClearOwner(false);
                return false;
            }
            var moved = locomotion.TryTeleportTo(frozenTarget, frozenSurface);
            if (!moved) gate.Cancel("MOVE_REJECTED_AFTER_REVALIDATION");
            ClearOwner(false);
            return moved;
        }

        public bool PathClearFromHead(Vector3 pointerOrigin)
        {
            EnsureReady();
            if (!isActiveAndEnabled || headCamera == null) return false;
            var delta = pointerOrigin - headCamera.transform.position;
            var distance = delta.magnitude;
            if (distance < 0.001f) return true;
            var count = Physics.RaycastNonAlloc(headCamera.transform.position, delta / distance, pathHits,
                distance, obstacleMask, QueryTriggerInteraction.Ignore);
            if (count >= pathHits.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var candidate = pathHits[index].collider;
                if (candidate == null || candidate.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        public void CancelForSource(string source, string reason)
        {
            if (!IsOwnedBy(source)) return;
            Cancel(reason, true);
        }

        public void Cancel(string reason = "CANCELLED", bool requireNewNeutral = true)
        {
            EnsureReady();
            gate.Cancel(reason);
            if (requireNewNeutral) neutralSources.Clear();
            ClearOwner(false);
        }

        public void ResetState(double now, int revision)
        {
            EnsureReady();
            sessionRevision = revision;
            gate.Reset(now);
            neutralSources.Clear();
            ClearOwner(false);
        }

        private TeleportFacts BuildFacts(string anchor, string source, Vector3 floorTarget,
            bool sourceTracked, bool systemGesture, bool pathClear, bool targetEnabled)
        {
            var landingClear = false;
            var headClear = false;
            var moverValid = locomotion != null && locomotion.EvaluateTeleport(floorTarget, frozenSurface,
                out landingClear, out headClear);
            var distance = headCamera == null ? double.PositiveInfinity :
                Vector3.Distance(headCamera.transform.position, floorTarget);
            return new TeleportFacts(anchor, source, $"WO-17-session-{sessionRevision}", distance,
                sourceTracked,
                lifecycle != null && lifecycle.HeadTracked,
                lifecycle != null && lifecycle.Focused,
                lifecycle == null || lifecycle.Paused,
                priority == null || priority.UiBusy || priority.PrecisionBusy || priority.Modal,
                priority == null || priority.HoldingObject,
                systemGesture,
                pathClear,
                moverValid && landingClear,
                moverValid && headClear,
                targetEnabled);
        }

        private bool CanIntentActuate(bool sourceTracked, bool systemGesture) =>
            sourceTracked && !systemGesture && lifecycle != null && lifecycle.ContextSafe &&
            priority != null && !priority.UiBusy && !priority.PrecisionBusy && !priority.Modal &&
            !priority.HoldingObject;

        private void OnContextChanged()
        {
            if (ownerSource == null) return;
            if (lifecycle == null || !lifecycle.ContextSafe || priority == null || priority.UiBusy ||
                priority.PrecisionBusy || priority.Modal || priority.HoldingObject)
                Cancel("CONTEXT_CHANGED", true);
        }

        private void ClearOwner(bool keepNeutral)
        {
            if (!keepNeutral) neutralSources.Clear();
            ownerSource = null;
            ownerBeganAt = 0;
            frozenAnchor = null;
            frozenSurface = null;
            currentFacts = null;
            pending = null;
            if (priority != null) priority.SetTeleportActive(false);
            SetVisual(false);
        }

        private void SetVisual(bool visible)
        {
            if (targetVisual == null) return;
            if (visible) targetVisual.position = frozenTarget + Vector3.up * 0.015f;
            if (targetVisual.gameObject.activeSelf != visible) targetVisual.gameObject.SetActive(visible);
        }
    }
}
