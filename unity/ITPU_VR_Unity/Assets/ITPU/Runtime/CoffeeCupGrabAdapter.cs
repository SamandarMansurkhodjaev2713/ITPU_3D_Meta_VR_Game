using System;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CoffeeCupGrabAdapter : XrGrabTarget
    {
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private CoffeeInteractionController coffee;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform homeAnchor;
        [SerializeField] private Transform dockAnchor;
        [SerializeField] private float dockDistance = 0.14f;
        [SerializeField] private float minimumDockUp = 0.65f;
        [SerializeField] private float recoveryBelowY = -1.5f;
        [SerializeField] private float recoveryDistance = 7f;
        [SerializeField] private float recoveryDelay = 0.75f;

        private readonly Collider[] overlapBuffer = new Collider[16];
        private RecoverableProp recovery;
        private string owner;
        private bool trackingValid;
        private bool docked;
        private Vector3 gripOffset;
        private Quaternion rotationOffset = Quaternion.identity;
        private double unsafeSince = -1;
        private bool subscribed;

        public override string Owner => owner ?? "";
        public override bool Held => !string.IsNullOrEmpty(owner);
        public bool TrackingValid => trackingValid;
        public bool Docked => docked;
        public Rigidbody Body => body;
        public Transform HomeAnchor => homeAnchor;
        public Transform DockAnchor => dockAnchor;

        public void Configure(ChallengeSessionAuthority authority, InteractionPriority interactionPriority,
            CoffeeInteractionController controller, Rigidbody rigidbody, Transform home, Transform dock)
        {
            Unsubscribe();
            sessionAuthority = authority;
            priority = interactionPriority;
            coffee = controller;
            body = rigidbody;
            homeAnchor = home;
            dockAnchor = dock;
            EnsureReady();
            Subscribe();
        }

        private void Awake() => EnsureReady();

        private void OnEnable()
        {
            EnsureReady();
            Subscribe();
        }

        private void OnDisable()
        {
            if (Held) MarkTrackingLost(owner);
            Unsubscribe();
        }

        private void Update()
        {
            if (Held || docked || body == null || sessionAuthority == null || sessionAuthority.Session.Paused)
            {
                unsafeSince = -1;
                return;
            }
            var home = homeAnchor == null ? transform.position : homeAnchor.position;
            var unsafePosition = transform.position.y < recoveryBelowY ||
                                 Vector3.Distance(transform.position, home) > recoveryDistance;
            if (!unsafePosition)
            {
                unsafeSince = -1;
                return;
            }
            if (unsafeSince < 0)
            {
                unsafeSince = Time.unscaledTimeAsDouble;
                return;
            }
            if (Time.unscaledTimeAsDouble - unsafeSince < recoveryDelay || !DestinationClear()) return;
            if (recovery != null && recovery.TryReturn(true, sessionAuthority.Token)) ResetToHome();
        }

        public override bool TryBeginGrab(string source, Pose gripPose)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(source) || Held || sessionAuthority == null ||
                sessionAuthority.Session.Paused || recovery == null ||
                !recovery.Hold(sessionAuthority.Token)) return false;
            owner = source;
            trackingValid = true;
            unsafeSince = -1;
            gripOffset = Quaternion.Inverse(gripPose.rotation) * (transform.position - gripPose.position);
            rotationOffset = Quaternion.Inverse(gripPose.rotation) * transform.rotation;
            if (docked)
            {
                docked = false;
                coffee?.NotifyCupRemoved();
            }
            PrepareKinematic();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            priority?.SetHoldingObject(source, true);
            return true;
        }

        public override bool UpdateGrab(string source, Pose gripPose)
        {
            if (owner != source || body == null) return false;
            trackingValid = true;
            var targetPosition = gripPose.position + gripPose.rotation * gripOffset;
            var targetRotation = gripPose.rotation * rotationOffset;
            body.MovePosition(targetPosition);
            body.MoveRotation(targetRotation);
            return true;
        }

        public override bool TryRelease(string source, bool intentional, Vector3 linearVelocity,
            Vector3 angularVelocity)
        {
            if (owner != source || recovery == null || sessionAuthority == null ||
                !recovery.Release(sessionAuthority.Token)) return false;
            var mayThrow = intentional && trackingValid;
            var releasedSource = owner;
            owner = null;
            trackingValid = false;
            priority?.SetHoldingObject(releasedSource, false);
            if (CanDock())
            {
                SnapToDock();
                return true;
            }
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = mayThrow && IsFinite(linearVelocity) ? linearVelocity : Vector3.zero;
            body.angularVelocity = mayThrow && IsFinite(angularVelocity) ? angularVelocity : Vector3.zero;
            return true;
        }

        public override bool MarkTrackingLost(string source)
        {
            if (owner != source || recovery == null) return false;
            trackingValid = false;
            recovery.LostTracking();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            return true;
        }

        public bool SnapToDock()
        {
            EnsureReady();
            if (Held || dockAnchor == null || body == null) return false;
            PrepareKinematic();
            SetBodyPose(dockAnchor.position, dockAnchor.rotation);
            docked = true;
            unsafeSince = -1;
            coffee?.NotifyCupDocked();
            return true;
        }

        public void ResetToHome()
        {
            EnsureReady();
            var releasedSource = owner;
            owner = null;
            trackingValid = true;
            docked = false;
            unsafeSince = -1;
            recovery?.Reset();
            priority?.SetHoldingObject(releasedSource, false);
            PrepareKinematic();
            if (homeAnchor != null)
                SetBodyPose(homeAnchor.position, homeAnchor.rotation);
            coffee?.NotifyCupRemoved();
        }

        public bool CanDock()
        {
            if (dockAnchor == null || Held) return false;
            return Vector3.Distance(transform.position, dockAnchor.position) <= dockDistance &&
                   Vector3.Dot(transform.up, dockAnchor.up) >= minimumDockUp;
        }

        private void EnsureReady()
        {
            body ??= GetComponent<Rigidbody>();
            if (sessionAuthority == null)
                sessionAuthority = FindAnyObjectByType<ChallengeSessionAuthority>(FindObjectsInactive.Include);
            if (priority == null)
                priority = FindAnyObjectByType<InteractionPriority>(FindObjectsInactive.Include);
            if (recovery == null && sessionAuthority != null)
                recovery = new RecoverableProp(sessionAuthority.Session);
        }

        private void Subscribe()
        {
            if (subscribed || sessionAuthority == null || !isActiveAndEnabled) return;
            sessionAuthority.ResetOccurred += ResetToHome;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred -= ResetToHome;
            subscribed = false;
        }

        private void PrepareKinematic()
        {
            if (body == null) return;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }

        private void SetBodyPose(Vector3 position, Quaternion rotation)
        {
            if (body == null)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        private bool DestinationClear()
        {
            if (homeAnchor == null) return true;
            var count = Physics.OverlapSphereNonAlloc(homeAnchor.position, 0.09f, overlapBuffer,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count >= overlapBuffer.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var candidate = overlapBuffer[index];
                if (candidate == null || candidate.transform.IsChildOf(transform)) continue;
                var otherBody = candidate.attachedRigidbody;
                if (otherBody != null && otherBody != body && !otherBody.isKinematic) return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
