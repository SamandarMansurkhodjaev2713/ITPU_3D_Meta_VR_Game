using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    public enum PaperPropState
    {
        Sheet,
        Crumpled,
        Held,
        Thrown,
        Scored,
        Miss,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PaperPropGrabAdapter : XrGrabTarget
    {
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform homeAnchor;
        [SerializeField] private GameObject sheetVisual;
        [SerializeField] private GameObject crumpledVisual;
        [SerializeField] private BoxCollider sheetCollider;
        [SerializeField] private SphereCollider crumpledCollider;
        [SerializeField] private float recoveryBelowY = -1.5f;
        [SerializeField] private float recoveryDistance = 7f;
        [SerializeField] private float unsafeRecoveryDelay = 0.75f;
        [SerializeField] private float missRecoveryDelay = 4f;
        [SerializeField] private float scoredReturnDelay = 0.75f;

        private readonly Collider[] overlapBuffer = new Collider[16];
        private RecoverableProp recovery;
        private PaperBasketProbe basketProbe;
        private string owner;
        private bool trackingValid;
        private bool lastReleaseTracked;
        private Vector3 gripOffset;
        private Quaternion rotationOffset = Quaternion.identity;
        private double unsafeSince = -1;
        private double restingSince = -1;
        private double scoredAt = -1;
        private bool subscribed;

        public override string Owner => owner ?? "";
        public override bool Held => !string.IsNullOrEmpty(owner);
        public bool TrackingValid => trackingValid;
        public bool LastReleaseTracked => lastReleaseTracked;
        public PaperPropState State { get; private set; }
        public int ScoreCount { get; private set; }
        public int PoolReturnCount { get; private set; }
        public Rigidbody Body => body;
        public Transform HomeAnchor => homeAnchor;

        public void Configure(ChallengeSessionAuthority authority, InteractionPriority interactionPriority,
            Rigidbody rigidbody, Transform home, GameObject sheet, GameObject crumpled,
            BoxCollider sheetHitbox, SphereCollider crumpledHitbox)
        {
            Unsubscribe();
            sessionAuthority = authority;
            priority = interactionPriority;
            body = rigidbody;
            homeAnchor = home;
            sheetVisual = sheet;
            crumpledVisual = crumpled;
            sheetCollider = sheetHitbox;
            crumpledCollider = crumpledHitbox;
            EnsureReady();
            ResetSlot(true);
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
            if (Held || body == null || sessionAuthority == null || sessionAuthority.Session.Paused)
            {
                unsafeSince = -1;
                restingSince = -1;
                return;
            }
            if (State == PaperPropState.Scored)
            {
                if (scoredAt >= 0 && Time.unscaledTimeAsDouble - scoredAt >= scoredReturnDelay)
                    TryReturnToPool();
                return;
            }
            if (State != PaperPropState.Thrown && State != PaperPropState.Miss) return;
            var unsafePosition = transform.position.y < recoveryBelowY ||
                                 Vector3.Distance(transform.position, homeAnchor.position) > recoveryDistance;
            if (unsafePosition)
            {
                if (unsafeSince < 0) unsafeSince = Time.unscaledTimeAsDouble;
                if (Time.unscaledTimeAsDouble - unsafeSince >= unsafeRecoveryDelay) TryReturnToPool();
                return;
            }
            unsafeSince = -1;
            var resting = body.linearVelocity.sqrMagnitude < 0.0025f && body.angularVelocity.sqrMagnitude < 0.01f;
            if (!resting)
            {
                restingSince = -1;
                return;
            }
            if (restingSince < 0) restingSince = Time.unscaledTimeAsDouble;
            if (Time.unscaledTimeAsDouble - restingSince < missRecoveryDelay) return;
            State = PaperPropState.Miss;
            TryReturnToPool();
        }

        public override bool TryPrimaryActivate(string source)
        {
            EnsureReady();
            if (State != PaperPropState.Sheet || Held || string.IsNullOrWhiteSpace(source) ||
                sessionAuthority == null || sessionAuthority.Session.Paused) return false;
            SetCrumpled(true);
            State = PaperPropState.Crumpled;
            return true;
        }

        public override bool TryBeginGrab(string source, Pose gripPose)
        {
            EnsureReady();
            if (State != PaperPropState.Crumpled || string.IsNullOrWhiteSpace(source) || Held ||
                sessionAuthority == null || sessionAuthority.Session.Paused || recovery == null ||
                !recovery.Hold(sessionAuthority.Token)) return false;
            owner = source;
            trackingValid = true;
            lastReleaseTracked = false;
            unsafeSince = restingSince = scoredAt = -1;
            gripOffset = Quaternion.Inverse(gripPose.rotation) * (transform.position - gripPose.position);
            rotationOffset = Quaternion.Inverse(gripPose.rotation) * transform.rotation;
            PrepareKinematic();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            State = PaperPropState.Held;
            priority?.SetHoldingObject(source, true);
            return true;
        }

        public override bool UpdateGrab(string source, Pose gripPose)
        {
            if (owner != source || body == null) return false;
            trackingValid = true;
            body.MovePosition(gripPose.position + gripPose.rotation * gripOffset);
            body.MoveRotation(gripPose.rotation * rotationOffset);
            return true;
        }

        public override bool TryRelease(string source, bool intentional, Vector3 linearVelocity,
            Vector3 angularVelocity)
        {
            if (owner != source || recovery == null || sessionAuthority == null ||
                !recovery.Release(sessionAuthority.Token)) return false;
            var releasedSource = owner;
            var mayThrow = intentional && trackingValid;
            owner = null;
            trackingValid = false;
            lastReleaseTracked = mayThrow;
            priority?.SetHoldingObject(releasedSource, false);
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = mayThrow && IsFinite(linearVelocity)
                ? Vector3.ClampMagnitude(linearVelocity, 5f)
                : Vector3.zero;
            body.angularVelocity = mayThrow && IsFinite(angularVelocity)
                ? Vector3.ClampMagnitude(angularVelocity, 14f)
                : Vector3.zero;
            State = PaperPropState.Thrown;
            unsafeSince = restingSince = -1;
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

        public bool NotifyBasketMouth(bool movingDown)
        {
            if (State != PaperPropState.Thrown || Held || basketProbe == null) return false;
            basketProbe.EnterMouth(movingDown, lastReleaseTracked);
            return movingDown && lastReleaseTracked;
        }

        public bool TickBasketInside(bool inside, float dt)
        {
            if (State != PaperPropState.Thrown || basketProbe == null || sessionAuthority == null) return false;
            if (!basketProbe.Tick(inside, dt, sessionAuthority.Session.Paused)) return false;
            State = PaperPropState.Scored;
            checked { ScoreCount++; }
            scoredAt = Time.unscaledTimeAsDouble;
            PrepareKinematic();
            return true;
        }

        public void ResetToHome() => ResetSlot(true);

        public bool TryReturnToPool()
        {
            EnsureReady();
            if (Held || recovery == null || sessionAuthority == null || !DestinationClear() ||
                !recovery.TryReturn(true, sessionAuthority.Token)) return false;
            checked { PoolReturnCount++; }
            ResetSlot(false);
            return true;
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
            basketProbe ??= new PaperBasketProbe(0.15);
        }

        private void ResetSlot(bool resetCounters)
        {
            EnsureReady();
            var releasedSource = owner;
            owner = null;
            if (!string.IsNullOrEmpty(releasedSource)) priority?.SetHoldingObject(releasedSource, false);
            trackingValid = true;
            lastReleaseTracked = false;
            unsafeSince = restingSince = scoredAt = -1;
            recovery?.Reset();
            basketProbe?.Reset();
            if (resetCounters)
            {
                ScoreCount = 0;
                PoolReturnCount = 0;
            }
            SetCrumpled(false);
            State = PaperPropState.Sheet;
            PrepareKinematic();
            if (homeAnchor != null) SetBodyPose(homeAnchor.position, homeAnchor.rotation);
        }

        private void SetCrumpled(bool value)
        {
            if (sheetVisual != null) sheetVisual.SetActive(!value);
            if (crumpledVisual != null) crumpledVisual.SetActive(value);
            if (sheetCollider != null) sheetCollider.enabled = !value;
            if (crumpledCollider != null) crumpledCollider.enabled = value;
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
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        private bool DestinationClear()
        {
            if (homeAnchor == null) return false;
            var count = Physics.OverlapSphereNonAlloc(homeAnchor.position, 0.045f, overlapBuffer,
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

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
