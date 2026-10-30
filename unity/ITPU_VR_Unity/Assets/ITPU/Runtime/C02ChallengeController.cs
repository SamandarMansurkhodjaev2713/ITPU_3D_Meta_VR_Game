using System;
using System.Linq;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class C02ChallengeController : MonoBehaviour
    {
        public const string Checkpoint = "GUEST_CHECKPOINT";

        [SerializeField] private DemoPressInteractable stepButton;
        [SerializeField] private DemoPressInteractable waitRuleButton;
        [SerializeField] private DemoPressInteractable guestButton;
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private C02GuidePresenter presenter;
        [SerializeField] private TextMesh statusLabel;
        [SerializeField] private TextMesh traceLabel;

        private C02WaitChallenge challenge;
        private bool subscribed;
        private bool prerequisiteAnnounced;

        public C02Stage Stage => challenge?.Stage ?? C02Stage.BrokenReady;
        public bool WaitRuleInstalled => challenge != null && challenge.WaitRuleInstalled;
        public bool Completed => challenge != null && challenge.Completed;
        public bool PrerequisiteMet => challenge != null && challenge.PrerequisiteMet;
        public SessionToken CurrentToken => sessionAuthority == null ? default : sessionAuthority.Token;
        public TraceEntry[] TraceSnapshot => challenge?.TraceSnapshot() ?? Array.Empty<TraceEntry>();
        public C02GuidePresenter Presenter => presenter;

        public void Configure(DemoPressInteractable step, DemoPressInteractable addWait,
            DemoPressInteractable guestArrived, ChallengeSessionAuthority authority,
            XrLifecycleState lifecycleState, C02GuidePresenter guidePresenter,
            TextMesh status, TextMesh trace)
        {
            Unsubscribe();
            stepButton = step;
            waitRuleButton = addWait;
            guestButton = guestArrived;
            sessionAuthority = authority;
            lifecycle = lifecycleState;
            presenter = guidePresenter;
            statusLabel = status;
            traceLabel = trace;
            EnsureDomain();
            Subscribe();
            RenderInitial();
        }

        private void Awake()
        {
            EnsureDomain();
            RenderInitial();
        }

        private void Update()
        {
            if (prerequisiteAnnounced || !PrerequisiteMet || Stage != C02Stage.BrokenReady) return;
            prerequisiteAnnounced = true;
            Render("1. STEP\nгид уйдёт без гостя");
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        public bool ApplyConfirmedTeleport(string waypoint)
        {
            EnsureDomain();
            var changed = challenge.ConfirmedTeleport(waypoint, TrackingReady(), sessionAuthority.Token);
            if (changed)
            {
                presenter.Present(Stage);
                Render("WAITING\nгид ждёт гостя");
            }
            EmitTelemetry("teleport-resync", changed ? "accepted" : "rejected");
            return changed;
        }

        private void EnsureDomain()
        {
            if (challenge != null || sessionAuthority == null) return;
            challenge = new C02WaitChallenge(sessionAuthority.Session, new[] { Checkpoint });
            presenter?.ResetPresentation();
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled || stepButton == null || waitRuleButton == null ||
                guestButton == null || sessionAuthority == null) return;
            stepButton.Activated += OnStep;
            waitRuleButton.Activated += OnAddWait;
            guestButton.Activated += OnGuestArrived;
            sessionAuthority.ResetOccurred += OnSessionReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (stepButton != null) stepButton.Activated -= OnStep;
            if (waitRuleButton != null) waitRuleButton.Activated -= OnAddWait;
            if (guestButton != null) guestButton.Activated -= OnGuestArrived;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred -= OnSessionReset;
            subscribed = false;
        }

        private void OnStep()
        {
            EnsureDomain();
            if (!PrerequisiteMet)
            {
                Render("LOCKED\nсначала C01 PASS");
                EmitTelemetry("step", "c01-required");
                return;
            }
            var changed = challenge.Step(true, false, TrackingReady(), sessionAuthority.Token);
            presenter.Present(Stage);
            switch (Stage)
            {
                case C02Stage.BrokenGuideLeft:
                    Render("ДЕФЕКТ ВИДЕН\nгид ушёл без гостя");
                    break;
                case C02Stage.WaitingForGuest:
                    Render("WAITING\nгид остаётся на месте");
                    break;
                case C02Stage.Verified:
                    Render("C02 PASS\nожидание проверено");
                    break;
                default:
                    Render(changed ? "STEP принят" : "STEP отклонён");
                    break;
            }
            EmitTelemetry("step", changed ? "accepted" : "rejected");
        }

        private void OnAddWait()
        {
            EnsureDomain();
            var changed = challenge.InstallWaitRule(sessionAuthority.Token);
            if (changed) presenter.Present(Stage);
            Render(changed ? "WAIT FOR GUEST добавлен\n2. STEP ещё раз" :
                !PrerequisiteMet ? "LOCKED\nсначала C01 PASS" : "Сначала покажи дефект");
            EmitTelemetry("add-wait", changed ? "installed" : "rejected");
        }

        private void OnGuestArrived()
        {
            EnsureDomain();
            var changed = challenge.Step(false, true, TrackingReady(), sessionAuthority.Token);
            if (changed) presenter.Present(Stage);
            Render(changed && Stage == C02Stage.GuestConfirmed
                ? "ГОСТЬ ОБНАРУЖЕН\n4. STEP для продолжения"
                : "Гость пока не ожидается");
            EmitTelemetry("guest-arrived", changed ? "accepted" : "rejected");
        }

        private void OnSessionReset()
        {
            EnsureDomain();
            challenge.ResetForCurrentSession();
            presenter.ResetPresentation();
            prerequisiteAnnounced = false;
            RenderInitial();
            EmitTelemetry("reset", "clean-broken-baseline");
        }

        private bool TrackingReady() => lifecycle != null && lifecycle.Focused && !lifecycle.Paused &&
                                        lifecycle.HeadTracked;

        private void RenderInitial() => Render(PrerequisiteMet
            ? "1. STEP\nгид уйдёт без гостя"
            : "LOCKED\nсначала C01 PASS");

        private void Render(string status)
        {
            if (statusLabel != null) statusLabel.text = status;
            if (traceLabel == null) return;
            var entries = TraceSnapshot;
            traceLabel.text = entries.Length == 0
                ? "C02 EVENT TRACE\n(empty)"
                : "C02 EVENT TRACE\n" + string.Join("\n", entries.Skip(Math.Max(0, entries.Length - 4))
                    .Select(entry => "#" + entry.Sequence + " " + entry.Event + " → " + entry.Outcome));
        }

        private void EmitTelemetry(string action, string outcome)
        {
            Debug.Log("ITPU_C02 action=" + action +
                      " outcome=" + outcome +
                      " stage=" + Stage +
                      " wait_rule=" + WaitRuleInstalled +
                      " prerequisite=" + PrerequisiteMet +
                      " completed=" + Completed +
                      " epoch=" + (sessionAuthority == null ? 0 : sessionAuthority.Token.Epoch));
        }
    }
}
