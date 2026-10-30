using ITPU.Domain;
using System.Globalization;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class XrLocomotionDriver : MonoBehaviour
    {
        [SerializeField] private XROrigin origin;
        [SerializeField] private Camera headCamera;
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private InteractionPriority priority;
        [SerializeField] private XrStartupPoseAligner startupPoseAligner;
        [SerializeField] private LayerMask obstacleMask = Physics.DefaultRaycastLayers;
        [SerializeField] private float minimumBodyHeight = 0.9f;
        [SerializeField] private float maximumBodyHeight = 2.15f;
        [SerializeField] private float maximumRoomScaleOffset = 0.75f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float maximumFallSpeed = -8f;
        [SerializeField] private bool automaticInput = true;
        [SerializeField] private bool smoothTurnEnabled;

        private readonly Collider[] overlapBuffer = new Collider[32];
        private readonly WalkSample walkSample = new();
        private CharacterController characterController;
        private ControllerWalkPolicy policy;
        private InputAction moveAction;
        private InputAction turnAction;
        private bool ownsActions;
        private float verticalVelocity;
        private CollisionFlags lastGroundingCollision;
        private float lastGroundingDeltaY;
        private static readonly double[] VerticalDiagnosticTimes = { 0.0, 0.25, 0.5, 1.0, 2.0, 4.0, 8.0 };
        private double verticalDiagnosticStartedAt = -1.0;
        private int verticalDiagnosticSample;
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
        private const int DiagnosticEventBudget = 48;
        private const float CollisionLimitedEpsilon = 0.001f;
        private int diagnosticEventCount;
        private bool moveDiagnosticActive;
        private Vector3 moveDiagnosticStart;
        private double moveDiagnosticStartedAt;
        private CollisionFlags moveDiagnosticCollisions;
        private int moveDiagnosticFrames;
        private int moveDiagnosticCollisionLimitedSamples;
        private bool moveDiagnosticCollisionLogged;
        private bool turnDiagnosticAwaitingNeutral;
        private double turnDiagnosticStartedAt;
        private int turnDiagnosticSuppressedSamples;
        private bool smoothTurnDiagnosticActive;
        private double smoothTurnDiagnosticStartedAt;
        private float smoothTurnRequestedDegrees;
        private float smoothTurnActualOriginDegrees;
        private float smoothTurnActualHeadDegrees;
        private int smoothTurnDiagnosticSamples;
        private Vector3 smoothTurnHeadStart;
#endif

        public string LastPolicyReason => policy?.Reason ?? "NOT_INITIALIZED";
        public int AppliedMoveCount { get; private set; }
        public int AppliedTurnCount { get; private set; }
        public int AppliedTeleportCount { get; private set; }
        public CharacterController Body => characterController;
        public float MaximumRoomScaleOffset => maximumRoomScaleOffset;
        public bool SmoothTurnEnabled => smoothTurnEnabled;
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
        public Vector3 LastRequestedMove { get; private set; }
        public Vector3 LastActualMove { get; private set; }
        public CollisionFlags LastMoveCollisionFlags { get; private set; }
        public int CollisionLimitedMoveCount { get; private set; }
#endif

        public void Configure(XROrigin xrOrigin, Camera camera, XrLifecycleState state,
            InteractionPriority interactionPriority)
        {
            origin = xrOrigin;
            headCamera = camera;
            lifecycle = state;
            priority = interactionPriority;
            EnsureReady();
            SyncCapsuleToHead();
        }

        public void SetAutomaticInput(bool enabled) => automaticInput = enabled;

        public void SetSmoothTurnEnabled(bool enabled)
        {
            EnsureReady();
            if (smoothTurnEnabled == enabled) return;
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndTurnDiagnostic("MODE_CHANGED", false);
            EndSmoothTurnDiagnostic("MODE_CHANGED", false);
#endif
            smoothTurnEnabled = enabled;
            policy.SetMode(true, enabled);
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EmitDiagnostic("ITPU_XR_TURN_MODE mode=" + (enabled ? "SMOOTH_45_DPS" : "SNAP_30") +
                           " neutralRequired=True");
#endif
        }

        public void SetStartupPoseAligner(XrStartupPoseAligner aligner)
        {
            startupPoseAligner = aligner;
            if (aligner != null && !aligner.IsAligned) BeginStartupPlacement();
        }

        public void BeginStartupPlacement()
        {
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndMoveDiagnostic("STARTUP_PENDING");
            EndTurnDiagnostic("STARTUP_PENDING", false);
            EndSmoothTurnDiagnostic("STARTUP_PENDING", false);
#endif
            verticalVelocity = 0f;
            lastGroundingCollision = CollisionFlags.None;
            lastGroundingDeltaY = 0f;
            verticalDiagnosticStartedAt = -1.0;
            verticalDiagnosticSample = 0;
            policy?.Cancel("STARTUP_PENDING");
        }

        public void CompleteStartupPlacement()
        {
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndMoveDiagnostic("STARTUP_COMPLETE");
            EndTurnDiagnostic("STARTUP_COMPLETE", false);
            EndSmoothTurnDiagnostic("STARTUP_COMPLETE", false);
#endif
            verticalVelocity = 0f;
            lastGroundingCollision = CollisionFlags.None;
            lastGroundingDeltaY = 0f;
            verticalDiagnosticStartedAt = Time.unscaledTimeAsDouble;
            verticalDiagnosticSample = 0;
            policy?.Cancel("STARTUP_NEUTRAL_REQUIRED");
        }

        private void Awake() => EnsureReady();

        private void EnsureReady()
        {
            characterController ??= GetComponent<CharacterController>();
            if (policy == null)
            {
                policy = new ControllerWalkPolicy();
                policy.SetMode(true, smoothTurnEnabled);
            }
            if (origin == null) origin = GetComponent<XROrigin>();
            if (headCamera == null && origin != null) headCamera = origin.Camera;
            if (lifecycle == null) lifecycle = GetComponent<XrLifecycleState>();
            if (priority == null) priority = GetComponent<InteractionPriority>();
            if (moveAction != null) return;

            ownsActions = true;
            moveAction = new InputAction("ControllerMove", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            turnAction = new InputAction("ControllerTurn", InputActionType.Value, expectedControlType: "Axis");
            turnAction.AddBinding("<XRController>{RightHand}/primary2DAxis/x");
        }

        private void OnEnable()
        {
            EnsureReady();
            if (lifecycle != null) lifecycle.Changed += CancelForLifecycle;
            moveAction.Enable();
            turnAction.Enable();
        }

        private void OnDisable()
        {
            if (lifecycle != null) lifecycle.Changed -= CancelForLifecycle;
            moveAction?.Disable();
            turnAction?.Disable();
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndMoveDiagnostic("DISABLED");
            EndTurnDiagnostic("DISABLED", false);
            EndSmoothTurnDiagnostic("DISABLED", false);
#endif
            policy?.Cancel("DISABLED");
            verticalVelocity = 0f;
        }

        private void OnDestroy()
        {
            if (!ownsActions) return;
            moveAction?.Dispose();
            turnAction?.Dispose();
        }

        private void Update()
        {
            SyncCapsuleToHead();
            if (!automaticInput) return;
            ProcessInput(moveAction.ReadValue<Vector2>(), turnAction.ReadValue<float>(),
                Time.unscaledDeltaTime, Time.unscaledTimeAsDouble);
            TickGrounding(Time.unscaledDeltaTime);
            if (Debug.isDebugBuild) EmitBoundedVerticalDiagnostic();
        }

        public bool ProcessInput(Vector2 move, float turn, double deltaTime, double now)
        {
            EnsureReady();
            if (!isActiveAndEnabled) return false;
            if (startupPoseAligner != null && !startupPoseAligner.IsAligned)
            {
                policy?.Cancel("STARTUP_PENDING");
                return false;
            }
            SyncCapsuleToHead();
            var yaw = headCamera == null ? transform.eulerAngles.y : headCamera.transform.eulerAngles.y;
            var safe = lifecycle != null && lifecycle.ContextSafe && priority != null &&
                       !priority.Modal && !priority.PrecisionBusy && !priority.UiBusy &&
                       !priority.TeleportActive;
            walkSample.Time = now;
            walkSample.X = move.x;
            walkSample.Y = move.y;
            walkSample.Turn = turn;
            walkSample.Yaw = yaw;
            walkSample.HeadTracked = lifecycle != null && lifecycle.HeadTracked;
            walkSample.MoveTracked = lifecycle != null && lifecycle.MoveControllerTracked;
            walkSample.TurnTracked = lifecycle != null && lifecycle.TurnControllerTracked;
            walkSample.Focused = lifecycle != null && lifecycle.Focused;
            walkSample.Paused = lifecycle == null || lifecycle.Paused;
            walkSample.Modal = priority == null || priority.Modal || priority.UiBusy;
            walkSample.PrecisionBusy = priority == null || priority.PrecisionBusy;
            walkSample.Teleporting = priority == null || priority.TeleportActive;
            walkSample.Carrying = priority != null && priority.HoldingObject;

            var pending = policy.Update(walkSample, deltaTime, now);
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            if (moveDiagnosticActive && move.sqrMagnitude <=
                ControllerWalkDefaults.Deadzone * ControllerWalkDefaults.Deadzone)
                EndMoveDiagnostic("NEUTRAL");
            else if (moveDiagnosticActive && policy.Reason != "READY")
                EndMoveDiagnostic(policy.Reason);

            if (turnDiagnosticAwaitingNeutral)
            {
                if (Mathf.Abs(turn) <= ControllerWalkDefaults.TurnOff)
                    EndTurnDiagnostic("NEUTRAL", true);
                else if (Mathf.Abs(turn) >= ControllerWalkDefaults.TurnOn)
                    turnDiagnosticSuppressedSamples++;
            }
            if (turnDiagnosticAwaitingNeutral && policy.Reason != "READY")
                EndTurnDiagnostic(policy.Reason, false);
            if (smoothTurnDiagnosticActive)
            {
                if (Mathf.Abs(turn) <= ControllerWalkDefaults.TurnOff)
                    EndSmoothTurnDiagnostic("NEUTRAL", true);
                else if (policy.Reason != "READY")
                    EndSmoothTurnDiagnostic(policy.Reason, false);
            }
#endif
            if (pending == null) return false;
            var command = policy.Consume(pending, now, safe);
            if (command == null)
            {
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
                if (moveDiagnosticActive) EndMoveDiagnostic(policy.Reason);
                if (turnDiagnosticAwaitingNeutral) EndTurnDiagnostic(policy.Reason, false);
                if (smoothTurnDiagnosticActive) EndSmoothTurnDiagnostic(policy.Reason, false);
#endif
                return false;
            }
            var value = command.Value;

            if (value.Dx != 0 || value.Dz != 0)
            {
                var before = transform.position;
                var requested = new Vector3((float)value.Dx, 0f, (float)value.Dz);
                var collision = characterController.Move(requested);
                var actual = transform.position - before;
                AppliedMoveCount++;
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
                LastRequestedMove = requested;
                LastActualMove = actual;
                LastMoveCollisionFlags = collision;
                if (!moveDiagnosticActive)
                {
                    moveDiagnosticActive = true;
                    moveDiagnosticStart = before;
                    moveDiagnosticStartedAt = now;
                    moveDiagnosticCollisions = CollisionFlags.None;
                    moveDiagnosticFrames = 0;
                    moveDiagnosticCollisionLimitedSamples = 0;
                    moveDiagnosticCollisionLogged = false;
                    EmitDiagnostic("ITPU_XR_LOCOMOTION MOVE_APPLIED" +
                                   " input=" + Vector2Text(move) +
                                   " requested=" + Vector(requested) +
                                   " actual=" + Vector(actual) +
                                   " collision=" + collision +
                                   " before=" + Vector(before) +
                                   " after=" + Vector(transform.position) +
                                   " headYaw=" + Number(yaw) +
                                   " moveCount=" + AppliedMoveCount);
                }
                moveDiagnosticCollisions |= collision;
                moveDiagnosticFrames++;
                var horizontalError = Vector2.Distance(
                    new Vector2(requested.x, requested.z), new Vector2(actual.x, actual.z));
                var collisionLimited = (collision & CollisionFlags.Sides) != 0 &&
                                       horizontalError > CollisionLimitedEpsilon;
                if (collisionLimited)
                {
                    CollisionLimitedMoveCount++;
                    moveDiagnosticCollisionLimitedSamples++;
                    if (!moveDiagnosticCollisionLogged)
                    {
                        moveDiagnosticCollisionLogged = true;
                        EmitDiagnostic("ITPU_XR_LOCOMOTION MOVE_COLLISION_LIMITED" +
                                       " requested=" + Vector(requested) +
                                       " actual=" + Vector(actual) +
                                       " error=" + Number(horizontalError) +
                                       " collision=" + collision +
                                       " collisionLimitedCount=" + CollisionLimitedMoveCount);
                    }
                }
#endif
            }
            if (value.TurnDegrees != 0)
            {
                var yawBefore = transform.eulerAngles.y;
                var headBefore = headCamera == null ? transform.position : headCamera.transform.position;
                var headYawBefore = headCamera == null ? yawBefore : headCamera.transform.eulerAngles.y;
                if (origin == null) transform.Rotate(0f, (float)value.TurnDegrees, 0f, Space.World);
                else origin.RotateAroundCameraUsingOriginUp((float)value.TurnDegrees);
                SyncCapsuleToHead();
                AppliedTurnCount++;
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
                var yawAfter = transform.eulerAngles.y;
                var headAfter = headCamera == null ? transform.position : headCamera.transform.position;
                var headYawAfter = headCamera == null ? yawAfter : headCamera.transform.eulerAngles.y;
                var actualOriginDegrees = Mathf.DeltaAngle(yawBefore, yawAfter);
                var actualHeadDegrees = Mathf.DeltaAngle(headYawBefore, headYawAfter);
                if (smoothTurnEnabled)
                {
                    if (!smoothTurnDiagnosticActive)
                    {
                        smoothTurnDiagnosticActive = true;
                        smoothTurnDiagnosticStartedAt = now;
                        smoothTurnRequestedDegrees = 0f;
                        smoothTurnActualOriginDegrees = 0f;
                        smoothTurnActualHeadDegrees = 0f;
                        smoothTurnDiagnosticSamples = 0;
                        smoothTurnHeadStart = headBefore;
                        EmitDiagnostic("ITPU_XR_LOCOMOTION SMOOTH_TURN_BEGIN" +
                                       " input=" + Number(turn) +
                                       " turnCount=" + AppliedTurnCount);
                    }
                    smoothTurnRequestedDegrees += (float)value.TurnDegrees;
                    smoothTurnActualOriginDegrees += actualOriginDegrees;
                    smoothTurnActualHeadDegrees += actualHeadDegrees;
                    smoothTurnDiagnosticSamples++;
                }
                else
                {
                    EmitDiagnostic("ITPU_XR_LOCOMOTION TURN_APPLIED" +
                                   " input=" + Number(turn) +
                                   " requestedDegrees=" + Number(value.TurnDegrees) +
                                   " actualOriginDegrees=" + Number(actualOriginDegrees) +
                                   " actualHeadDegrees=" + Number(actualHeadDegrees) +
                                   " headShift=" + Number(Vector3.Distance(headBefore, headAfter)) +
                                   " turnCount=" + AppliedTurnCount);
                    turnDiagnosticAwaitingNeutral = true;
                    turnDiagnosticStartedAt = now;
                    turnDiagnosticSuppressedSamples = 0;
                }
#endif
            }
            return true;
        }

        public void TickGrounding(double deltaTime)
        {
            EnsureReady();
            if (!isActiveAndEnabled || characterController == null || !characterController.enabled || deltaTime <= 0 ||
                deltaTime > ControllerWalkDefaults.MaxDt || lifecycle == null || !lifecycle.ContextSafe ||
                (startupPoseAligner != null && !startupPoseAligner.IsAligned))
            {
                verticalVelocity = 0f;
                return;
            }
            if (characterController.isGrounded && verticalVelocity < 0f) verticalVelocity = -0.5f;
            else verticalVelocity = Mathf.Max(maximumFallSpeed, verticalVelocity + gravity * (float)deltaTime);
            var beforeY = transform.position.y;
            lastGroundingCollision = characterController.Move(transform.up * (verticalVelocity * (float)deltaTime));
            lastGroundingDeltaY = transform.position.y - beforeY;
        }

        private void EmitBoundedVerticalDiagnostic()
        {
            var now = Time.unscaledTimeAsDouble;
            if (verticalDiagnosticStartedAt < 0.0 ||
                verticalDiagnosticSample >= VerticalDiagnosticTimes.Length) return;
            var elapsed = now - verticalDiagnosticStartedAt;
            if (elapsed + 0.0001 < VerticalDiagnosticTimes[verticalDiagnosticSample]) return;

            var offset = origin == null ? null : origin.CameraFloorOffsetObject;
            var mode = origin == null ? default : origin.CurrentTrackingOriginMode;
            Debug.Log("ITPU_XR_VERTICAL_MOTION" +
                      " sample=" + verticalDiagnosticSample++ +
                      " frame=" + Time.frameCount +
                      " elapsed=" + Number(elapsed) +
                      " origin=" + Vector(transform.position) +
                      " headWorld=" + Vector(headCamera == null ? default : headCamera.transform.position) +
                      " headLocal=" + Vector(headCamera == null ? default : headCamera.transform.localPosition) +
                      " offsetLocal=" + Vector(offset == null ? default : offset.transform.localPosition) +
                      " grounded=" + (characterController != null && characterController.isGrounded) +
                      " verticalVelocity=" + Number(verticalVelocity) +
                      " groundingDeltaY=" + Number(lastGroundingDeltaY) +
                      " collision=" + lastGroundingCollision +
                      " bodyCenter=" + Vector(characterController == null ? default : characterController.center) +
                      " bodyHeight=" + Number(characterController == null ? 0f : characterController.height) +
                      " cachedMode=" + mode +
                      " actualMode=" + (startupPoseAligner == null
                          ? default(TrackingOriginModeFlags)
                          : startupPoseAligner.ActualTrackingOriginMode) +
                      " focused=" + (lifecycle != null && lifecycle.Focused) +
                      " paused=" + (lifecycle == null || lifecycle.Paused) +
                      " headTracked=" + (lifecycle != null && lifecycle.HeadTracked) +
                      " aligned=" + (startupPoseAligner == null || startupPoseAligner.IsAligned));
        }

        private static string Number(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

        private static string Vector(Vector3 value) =>
            "(" + Number(value.x) + "," + Number(value.y) + "," + Number(value.z) + ")";

        private static string Vector2Text(Vector2 value) =>
            "(" + Number(value.x) + "," + Number(value.y) + ")";

#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
        private void EmitDiagnostic(string message)
        {
            if (diagnosticEventCount >= DiagnosticEventBudget) return;
            diagnosticEventCount++;
            Debug.Log(message);
        }

        private void EndMoveDiagnostic(string reason)
        {
            if (!moveDiagnosticActive) return;
            var end = transform.position;
            EmitDiagnostic("ITPU_XR_LOCOMOTION MOVE_BURST_END" +
                           " reason=" + reason +
                           " duration=" + Number(Mathf.Max(0f,
                               (float)(Time.unscaledTimeAsDouble - moveDiagnosticStartedAt))) +
                           " start=" + Vector(moveDiagnosticStart) +
                           " end=" + Vector(end) +
                           " displacement=" + Number(Vector3.Distance(moveDiagnosticStart, end)) +
                           " collision=" + moveDiagnosticCollisions +
                           " appliedSamples=" + moveDiagnosticFrames +
                           " collisionLimitedSamples=" + moveDiagnosticCollisionLimitedSamples +
                           " moveCount=" + AppliedMoveCount);
            moveDiagnosticActive = false;
            moveDiagnosticCollisions = CollisionFlags.None;
            moveDiagnosticFrames = 0;
            moveDiagnosticCollisionLimitedSamples = 0;
            moveDiagnosticCollisionLogged = false;
        }

        private void EndTurnDiagnostic(string reason, bool neutral)
        {
            if (!turnDiagnosticAwaitingNeutral) return;
            EmitDiagnostic("ITPU_XR_LOCOMOTION TURN_BURST_END" +
                           " reason=" + reason +
                           " heldSeconds=" + Number(Mathf.Max(0f,
                               (float)(Time.unscaledTimeAsDouble - turnDiagnosticStartedAt))) +
                           " appliedInBurst=1" +
                           " suppressedSamples=" + turnDiagnosticSuppressedSamples +
                           " neutral=" + neutral +
                           " turnCount=" + AppliedTurnCount);
            turnDiagnosticAwaitingNeutral = false;
            turnDiagnosticSuppressedSamples = 0;
        }

        private void EndSmoothTurnDiagnostic(string reason, bool neutral)
        {
            if (!smoothTurnDiagnosticActive) return;
            var headNow = headCamera == null ? transform.position : headCamera.transform.position;
            EmitDiagnostic("ITPU_XR_LOCOMOTION SMOOTH_TURN_BURST_END" +
                           " reason=" + reason +
                           " duration=" + Number(Mathf.Max(0f,
                               (float)(Time.unscaledTimeAsDouble - smoothTurnDiagnosticStartedAt))) +
                           " requestedDegrees=" + Number(smoothTurnRequestedDegrees) +
                           " actualOriginDegrees=" + Number(smoothTurnActualOriginDegrees) +
                           " actualHeadDegrees=" + Number(smoothTurnActualHeadDegrees) +
                           " headShift=" + Number(Vector3.Distance(smoothTurnHeadStart, headNow)) +
                           " appliedSamples=" + smoothTurnDiagnosticSamples +
                           " neutral=" + neutral +
                           " turnCount=" + AppliedTurnCount);
            smoothTurnDiagnosticActive = false;
            smoothTurnRequestedDegrees = 0f;
            smoothTurnActualOriginDegrees = 0f;
            smoothTurnActualHeadDegrees = 0f;
            smoothTurnDiagnosticSamples = 0;
        }
#endif

        public bool SyncCapsuleToHead()
        {
            EnsureReady();
            if (characterController == null || headCamera == null) return false;
            var localHead = transform.InverseTransformPoint(headCamera.transform.position);
            var horizontal = Vector2.ClampMagnitude(new Vector2(localHead.x, localHead.z),
                maximumRoomScaleOffset);
            var minimum = Mathf.Max(minimumBodyHeight,
                characterController.radius * 2f + characterController.skinWidth * 2f);
            var height = Mathf.Clamp(localHead.y, minimum, maximumBodyHeight);
            characterController.height = height;
            characterController.center = new Vector3(horizontal.x, height * 0.5f, horizontal.y);
            return true;
        }

        public bool EvaluateTeleport(Vector3 targetFloorPosition, Collider allowedSurface,
            out bool landingClear, out bool headClear)
        {
            landingClear = false;
            headClear = false;
            EnsureReady();
            if (!Finite(targetFloorPosition) || origin == null || headCamera == null || characterController == null)
                return false;
            if (allowedSurface != null)
            {
                var surface = allowedSurface.GetComponentInParent<TeleportSurface>();
                if (surface == null || !surface.isActiveAndEnabled) return false;
                if (Vector3.Distance(allowedSurface.ClosestPoint(targetFloorPosition), targetFloorPosition) > 0.08f)
                    return false;
            }
            SyncCapsuleToHead();

            var currentFeet = new Vector3(headCamera.transform.position.x, transform.position.y,
                headCamera.transform.position.z);
            var delta = targetFloorPosition - currentFeet;
            var up = transform.up;
            var radiusScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            var radius = characterController.radius * radiusScale;
            var height = Mathf.Max(radius * 2f, characterController.height * Mathf.Abs(transform.lossyScale.y));
            var center = transform.TransformPoint(characterController.center) + delta;
            var halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            var bottom = center - up * halfSegment;
            var top = center + up * halfSegment;

            landingClear = CapsuleClear(bottom, top, radius, allowedSurface);
            var finalHead = headCamera.transform.position + delta;
            headClear = SphereClear(finalHead, radius, allowedSurface);
            return landingClear && headClear;
        }

        public bool CanTeleportTo(Vector3 targetFloorPosition, Collider allowedSurface = null)
        {
            if (!isActiveAndEnabled || lifecycle == null || !lifecycle.ContextSafe || priority == null || priority.UiBusy ||
                priority.PrecisionBusy || priority.Modal || priority.HoldingObject)
                return false;
            return EvaluateTeleport(targetFloorPosition, allowedSurface, out var landing, out var head) &&
                   landing && head;
        }

        public bool TryTeleportTo(Vector3 targetFloorPosition, Collider allowedSurface = null)
        {
            EnsureReady();
            if (!isActiveAndEnabled || priority == null || !priority.TeleportActive || !CanTeleportTo(targetFloorPosition, allowedSurface))
                return false;

#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndMoveDiagnostic("TELEPORT_NEUTRAL_REQUIRED");
            EndTurnDiagnostic("TELEPORT_NEUTRAL_REQUIRED", false);
            EndSmoothTurnDiagnostic("TELEPORT_NEUTRAL_REQUIRED", false);
#endif
            var currentFeet = new Vector3(headCamera.transform.position.x, transform.position.y,
                headCamera.transform.position.z);
            var delta = targetFloorPosition - currentFeet;
            var wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position += delta;
            characterController.enabled = wasEnabled;
            SyncCapsuleToHead();
            verticalVelocity = 0f;
            AppliedTeleportCount++;
            policy.Cancel("TELEPORT_NEUTRAL_REQUIRED");
            return true;
        }

        public void ResetPolicy()
        {
            EnsureReady();
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndMoveDiagnostic("RESET");
            EndTurnDiagnostic("RESET", false);
            EndSmoothTurnDiagnostic("RESET", false);
#endif
            policy.Reset();
            verticalVelocity = 0f;
            SyncCapsuleToHead();
        }

        private bool CapsuleClear(Vector3 bottom, Vector3 top, float radius, Collider allowedSurface)
        {
            var count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapBuffer, obstacleMask,
                QueryTriggerInteraction.Ignore);
            if (count >= overlapBuffer.Length) return false;
            for (var index = 0; index < count; index++)
                if (!Ignored(overlapBuffer[index], allowedSurface)) return false;
            return true;
        }

        private bool SphereClear(Vector3 center, float radius, Collider allowedSurface)
        {
            var count = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer, obstacleMask,
                QueryTriggerInteraction.Ignore);
            if (count >= overlapBuffer.Length) return false;
            for (var index = 0; index < count; index++)
                if (!Ignored(overlapBuffer[index], allowedSurface)) return false;
            return true;
        }

        private bool Ignored(Collider candidate, Collider allowedSurface)
        {
            if (candidate == null || candidate == characterController || candidate == allowedSurface) return true;
            return candidate.transform.IsChildOf(transform);
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private void CancelForLifecycle()
        {
#if UNITY_INCLUDE_INSTRUMENTATION || UNITY_EDITOR
            EndMoveDiagnostic("LIFECYCLE_NEUTRAL_REQUIRED");
            EndTurnDiagnostic("LIFECYCLE_NEUTRAL_REQUIRED", false);
            EndSmoothTurnDiagnostic("LIFECYCLE_NEUTRAL_REQUIRED", false);
#endif
            policy?.Cancel("LIFECYCLE_NEUTRAL_REQUIRED");
            verticalVelocity = 0f;
        }
    }
}
