using System;
using System.Linq;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class C03ChallengeController : MonoBehaviour
    {
        [SerializeField] private DemoPressInteractable bypassButton;
        [SerializeField] private DemoPressInteractable gateButton;
        [SerializeField] private DemoPressInteractable guestViewButton;
        [SerializeField] private DemoPressInteractable guestEditButton;
        [SerializeField] private DemoPressInteractable teamEditButton;
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private XrLifecycleState lifecycle;
        [SerializeField] private TextMesh statusLabel;
        [SerializeField] private TextMesh matrixLabel;
        [SerializeField] private TextMesh traceLabel;

        private C03AuthorizationChallenge challenge;
        private bool subscribed;
        private bool prerequisiteAnnounced;

        public C03Stage Stage => challenge?.Stage ?? C03Stage.BaselineReady;
        public bool GateInstalled => challenge != null && challenge.GateInstalled;
        public bool Completed => challenge != null && challenge.Completed;
        public bool PrerequisiteMet => challenge != null && challenge.PrerequisiteMet;
        public long PolicyRevision => challenge?.PolicyRevision ?? 0;
        public SessionToken CurrentToken => sessionAuthority == null ? default : sessionAuthority.Token;
        public TraceEntry[] TraceSnapshot => challenge?.TraceSnapshot() ?? Array.Empty<TraceEntry>();

        public void Configure(DemoPressInteractable bypass, DemoPressInteractable gate,
            DemoPressInteractable guestView, DemoPressInteractable guestEdit,
            DemoPressInteractable teamEdit, ChallengeSessionAuthority authority,
            XrLifecycleState lifecycleState, TextMesh status, TextMesh matrix, TextMesh trace)
        {
            Unsubscribe();
            bypassButton = bypass;
            gateButton = gate;
            guestViewButton = guestView;
            guestEditButton = guestEdit;
            teamEditButton = teamEdit;
            sessionAuthority = authority;
            lifecycle = lifecycleState;
            statusLabel = status;
            matrixLabel = matrix;
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
            if (prerequisiteAnnounced || !PrerequisiteMet || Stage != C03Stage.BaselineReady) return;
            prerequisiteAnnounced = true;
            RenderStatus("1. RUN BYPASS\nпокажи скрытый дефект");
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void EnsureDomain()
        {
            if (challenge != null || sessionAuthority == null) return;
            challenge = new C03AuthorizationChallenge(sessionAuthority.Session);
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled || bypassButton == null || gateButton == null ||
                guestViewButton == null || guestEditButton == null || teamEditButton == null ||
                sessionAuthority == null) return;
            bypassButton.Activated += OnBypass;
            gateButton.Activated += OnInstallGate;
            guestViewButton.Activated += OnGuestView;
            guestEditButton.Activated += OnGuestEdit;
            teamEditButton.Activated += OnTeamEdit;
            sessionAuthority.ResetOccurred += OnSessionReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (bypassButton != null) bypassButton.Activated -= OnBypass;
            if (gateButton != null) gateButton.Activated -= OnInstallGate;
            if (guestViewButton != null) guestViewButton.Activated -= OnGuestView;
            if (guestEditButton != null) guestEditButton.Activated -= OnGuestEdit;
            if (teamEditButton != null) teamEditButton.Activated -= OnTeamEdit;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred -= OnSessionReset;
            subscribed = false;
        }

        private void OnBypass()
        {
            EnsureDomain();
            var result = challenge.DemonstrateBaselineBypass(TrackingReady(), sessionAuthority.Token);
            if (!PrerequisiteMet) RenderStatus("LOCKED\nсначала C02 PASS");
            else if (result.Accepted) RenderStatus("ДЕФЕКТ: ALLOW\nDATA 0 → 1");
            else RenderStatus("BYPASS отклонён");
            RenderEvidence();
            EmitTelemetry("baseline-bypass", result);
        }

        private void OnInstallGate()
        {
            EnsureDomain();
            var installed = challenge.InstallGate(PermissionFlags.Correct, sessionAuthority.Token);
            RenderStatus(installed ? "GATE INSTALLED\n2. запусти AUTH-01..03" :
                !PrerequisiteMet ? "LOCKED\nсначала C02 PASS" : "Сначала RUN BYPASS");
            RenderEvidence();
            EmitTelemetry("install-gate", default, installed ? "installed" : "rejected");
        }

        private void OnGuestView() => ExecuteCase("auth-01", DemoRole.Guest,
            DemoOperation.ViewPublicProject, "AUTH-01");

        private void OnGuestEdit() => ExecuteCase("auth-02", DemoRole.Guest,
            DemoOperation.EditRoute, "AUTH-02");

        private void OnTeamEdit() => ExecuteCase("auth-03", DemoRole.Team,
            DemoOperation.EditRoute, "AUTH-03");

        private void ExecuteCase(string action, DemoRole role, DemoOperation operation, string label)
        {
            EnsureDomain();
            var result = challenge.Execute(role, operation, TrackingReady(), challenge.PolicyRevision,
                sessionAuthority.Token);
            if (!GateInstalled) RenderStatus("Сначала установи GATE");
            else if (Completed) RenderStatus("C03 PASS\nALLOW / DENY / ALLOW");
            else if (result.Accepted) RenderStatus(label + " PASS\n" +
                (result.Allowed ? "ALLOW" : "DENY") + "  DATA " +
                result.VersionBefore + " → " + result.VersionAfter);
            else RenderStatus(label + " FAIL\npolicy не прошла тест");
            RenderEvidence();
            EmitTelemetry(action, result);
        }

        private void OnSessionReset()
        {
            EnsureDomain();
            challenge.ResetForCurrentSession();
            prerequisiteAnnounced = false;
            RenderInitial();
            EmitTelemetry("reset", default, "clean-baseline");
        }

        private bool TrackingReady() => lifecycle != null && lifecycle.Focused && !lifecycle.Paused &&
                                        lifecycle.HeadTracked;

        private void RenderInitial()
        {
            RenderStatus(PrerequisiteMet ? "1. RUN BYPASS\nпокажи скрытый дефект" :
                "LOCKED\nсначала C02 PASS");
            RenderEvidence();
        }

        private void RenderStatus(string value)
        {
            if (statusLabel != null) statusLabel.text = value;
        }

        private void RenderEvidence()
        {
            if (matrixLabel != null)
            {
                matrixLabel.text = "AUTH MATRIX\n" +
                    "01 GUEST VIEW  " + Mark(challenge != null && challenge.GuestViewPassed) + "\n" +
                    "02 GUEST EDIT  " + Mark(challenge != null && challenge.GuestEditDenied) + "\n" +
                    "03 TEAM EDIT   " + Mark(challenge != null && challenge.TeamEditPassed);
            }
            if (traceLabel == null) return;
            var entries = TraceSnapshot;
            traceLabel.text = entries.Length == 0
                ? "C03 EVENT TRACE\n(empty)"
                : "C03 EVENT TRACE\n" + string.Join("\n", entries.Skip(Math.Max(0, entries.Length - 4))
                    .Select(entry => "#" + entry.Sequence + " " + entry.Event + " → " + entry.Outcome));
        }

        private static string Mark(bool value) => value ? "PASS" : "—";

        private void EmitTelemetry(string action, C03AttemptResult result, string outcome = null)
        {
            Debug.Log("ITPU_C03 action=" + action +
                      " outcome=" + (outcome ?? (result.Accepted ? "accepted" : "rejected")) +
                      " role=" + result.Role +
                      " operation=" + result.Operation +
                      " allowed=" + result.Allowed +
                      " mutated=" + result.Mutated +
                      " version=" + result.VersionBefore + "->" + result.VersionAfter +
                      " stage=" + Stage +
                      " gate=" + GateInstalled +
                      " policy_revision=" + PolicyRevision +
                      " prerequisite=" + PrerequisiteMet +
                      " completed=" + Completed +
                      " epoch=" + (sessionAuthority == null ? 0 : sessionAuthority.Token.Epoch));
        }
    }
}
