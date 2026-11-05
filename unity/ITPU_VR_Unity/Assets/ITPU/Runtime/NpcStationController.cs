using System;
using System.Collections.Generic;
using System.Linq;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class NpcStationController : MonoBehaviour
    {
        private const string IdleState = "NPC01_Idle";

        [SerializeField] private Animator animator;
        [SerializeField] private Transform chestAim;
        [SerializeField] private Transform headAim;
        [SerializeField] private Transform attentionTarget;
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private XrLifecycleState lifecycle;

        private readonly Dictionary<string, List<(SkinnedMeshRenderer renderer, int index)>> expressions =
            new(StringComparer.Ordinal);
        private NpcStation station;
        private Vector3 anchorPosition;
        private Quaternion anchorRotation;
        private Quaternion chestNeutral;
        private Quaternion headNeutral;
        private bool subscribed;

        public Vector3 AnchorPosition => anchorPosition;
        public Quaternion AnchorRotation => anchorRotation;
        public double BodyYaw => station?.BodyYaw ?? 0d;
        public double HeadYaw => station?.HeadYaw ?? 0d;
        public NpcGesture Gesture => station?.Gesture ?? NpcGesture.Idle;
        public bool RootMotionDisabled => animator == null || !animator.applyRootMotion;
        public int ExpressionBindingCount => expressions.Values.Sum(items => items.Count);

        public void Configure(Animator importedAnimator, Transform chestPivot, Transform headPivot,
            Transform target, ChallengeSessionAuthority authority, XrLifecycleState lifecycleState)
        {
            Unsubscribe();
            animator = importedAnimator;
            chestAim = chestPivot;
            headAim = headPivot;
            attentionTarget = target;
            sessionAuthority = authority;
            lifecycle = lifecycleState;
            InitializeStation();
            Subscribe();
        }

        public void SetAttentionTarget(Transform target) => attentionTarget = target;

        public bool RequestGesture(NpcGesture gesture)
        {
            EnsureStation();
            if (lifecycle != null && !lifecycle.ContextSafe) return false;
            station.React(gesture);
            var state = StateFor(gesture);
            if (animator != null && animator.runtimeAnimatorController != null &&
                animator.HasState(0, Animator.StringToHash(state)))
                animator.CrossFadeInFixedTime(state, 0.12f, 0, 0f);
            return true;
        }

        public void SetExpression(string expression, float weight01)
        {
            if (!expressions.TryGetValue(expression, out var bindings))
                throw new ArgumentException("Unknown NPC expression: " + expression, nameof(expression));
            var weight = Mathf.Clamp01(weight01) * 100f;
            foreach (var binding in bindings)
                binding.renderer.SetBlendShapeWeight(binding.index, weight);
        }

        public void ResetStation()
        {
            EnsureStation();
            station.Reset();
            transform.SetPositionAndRotation(anchorPosition, anchorRotation);
            if (chestAim != null) chestAim.localRotation = chestNeutral;
            if (headAim != null) headAim.localRotation = headNeutral;
            foreach (var binding in expressions.Values.SelectMany(items => items))
                binding.renderer.SetBlendShapeWeight(binding.index, 0f);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                if (animator.runtimeAnimatorController != null &&
                    animator.HasState(0, Animator.StringToHash(IdleState)))
                    animator.Play(IdleState, 0, 0f);
                animator.Update(0f);
            }
        }

        public void SimulateLook(double relativeYaw, double deltaTime, bool tracked = true, bool paused = false)
        {
            EnsureStation();
            station.Look(relativeYaw, deltaTime, tracked, paused);
            ApplyAim();
        }

        public void EnforceAnchor()
        {
            EnsureStation();
            if (animator != null) animator.applyRootMotion = false;
            transform.SetPositionAndRotation(anchorPosition, anchorRotation);
        }

        private void Awake() => InitializeStation();
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            EnsureStation();
            if (attentionTarget == null)
            {
                station.Look(0d, Time.deltaTime, false, lifecycle == null || lifecycle.Paused);
                ApplyAim();
                return;
            }
            var direction = attentionTarget.position - transform.position;
            direction.y = 0f;
            var tracked = lifecycle == null || lifecycle.HeadTracked;
            var paused = lifecycle != null && (!lifecycle.Focused || lifecycle.Paused);
            var relativeYaw = direction.sqrMagnitude < 0.0001f
                ? 0f
                : Mathf.DeltaAngle(anchorRotation.eulerAngles.y,
                    Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y);
            station.Look(relativeYaw, Time.deltaTime, tracked, paused);
            ApplyAim();
        }

        private void LateUpdate() => EnforceAnchor();

        private void InitializeStation()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;
            if (chestAim == null) chestAim = FindDescendant("chest_aim");
            if (headAim == null) headAim = FindDescendant("head_aim");
            anchorPosition = transform.position;
            anchorRotation = transform.rotation;
            chestNeutral = chestAim == null ? Quaternion.identity : chestAim.localRotation;
            headNeutral = headAim == null ? Quaternion.identity : headAim.localRotation;
            station = new NpcStation(new Vec3(anchorPosition.x, anchorPosition.y, anchorPosition.z));
            BuildExpressionMap();
        }

        private void EnsureStation()
        {
            if (station == null) InitializeStation();
        }

        private void BuildExpressionMap()
        {
            expressions.Clear();
            foreach (var name in new[] { "blink_L", "blink_R", "smile", "jawOpen", "browRaise" })
                expressions[name] = new List<(SkinnedMeshRenderer, int)>();
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var imported = mesh.GetBlendShapeName(index);
                    foreach (var name in expressions.Keys.ToArray())
                        if (imported.Equals(name, StringComparison.Ordinal) ||
                            imported.EndsWith("__" + name, StringComparison.Ordinal) ||
                            imported.EndsWith("." + name, StringComparison.Ordinal))
                            expressions[name].Add((renderer, index));
                }
            }
        }

        private Transform FindDescendant(string exactName) => GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name.Equals(exactName, StringComparison.OrdinalIgnoreCase));

        private void ApplyAim()
        {
            if (chestAim != null)
                chestAim.localRotation = chestNeutral * Quaternion.Euler(0f, (float)station.BodyYaw, 0f);
            if (headAim != null)
                headAim.localRotation = headNeutral * Quaternion.Euler(0f, (float)station.HeadYaw, 0f);
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled) return;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred += ResetStation;
            if (lifecycle != null) lifecycle.Changed += OnLifecycleChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred -= ResetStation;
            if (lifecycle != null) lifecycle.Changed -= OnLifecycleChanged;
            subscribed = false;
        }

        private void OnLifecycleChanged()
        {
            if (lifecycle != null && !lifecycle.ContextSafe)
                ResetStation();
        }

        private static string StateFor(NpcGesture gesture) => gesture switch
        {
            NpcGesture.Greet => "NPC01_Greet",
            NpcGesture.Explain => "NPC01_Explain",
            NpcGesture.Point => "NPC01_PointNear",
            _ => IdleState,
        };
    }
}
