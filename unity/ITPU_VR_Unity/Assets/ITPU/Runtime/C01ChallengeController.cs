using System;
using System.Linq;
using ITPU.Domain;
using UnityEngine;

namespace ITPU.XR
{
    [DisallowMultipleComponent]
    public sealed class C01ChallengeController : MonoBehaviour
    {
        public const string Kitchen = "KITCHEN";
        public const string Niche413 = "NICHE_413";
        private const string Origin = "WORK_403";

        [SerializeField] private DemoPressInteractable routeButton;
        [SerializeField] private DemoPressInteractable linkButton;
        [SerializeField] private DemoPressInteractable destinationButton;
        [SerializeField] private ChallengeSessionAuthority sessionAuthority;
        [SerializeField] private C01RoutePresenter routePresenter;
        [SerializeField] private TextMesh statusLabel;
        [SerializeField] private TextMesh destinationLabel;
        [SerializeField] private TextMesh traceLabel;

        private C01RouteChallenge challenge;
        private long sequence;
        private bool subscribed;

        public bool HandlerLinked => challenge != null && challenge.HandlerLinked;
        public bool Completed => challenge != null && challenge.Completed;
        public C01Stage Stage => challenge?.Stage ?? C01Stage.MissingHandler;
        public string SelectedDestination => challenge?.SelectedDestination ?? Kitchen;
        public SessionToken CurrentToken => sessionAuthority == null ? default : sessionAuthority.Token;
        public TraceEntry[] TraceSnapshot => challenge?.TraceSnapshot() ?? Array.Empty<TraceEntry>();
        public C01RoutePresenter Presenter => routePresenter;

        public void Configure(DemoPressInteractable showRoute, DemoPressInteractable attachHandler,
            DemoPressInteractable selectDestination, ChallengeSessionAuthority authority,
            C01RoutePresenter presenter, TextMesh status,
            TextMesh destination, TextMesh trace)
        {
            Unsubscribe();
            routeButton = showRoute;
            linkButton = attachHandler;
            destinationButton = selectDestination;
            sessionAuthority = authority;
            routePresenter = presenter;
            statusLabel = status;
            destinationLabel = destination;
            traceLabel = trace;
            EnsureDomain();
            Subscribe();
            Render("1. SHOW ROUTE\nбез handler");
        }

        private void Awake()
        {
            EnsureDomain();
            Render("1. SHOW ROUTE\nбез handler");
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void EnsureDomain()
        {
            if (challenge != null || sessionAuthority == null) return;
            challenge = new C01RouteChallenge(sessionAuthority.Session, CreateTopology(), Origin, Kitchen, Niche413);
            sequence = 0;
            routePresenter?.Clear();
        }

        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled) return;
            if (routeButton == null || linkButton == null || destinationButton == null || sessionAuthority == null)
                return;
            routeButton.Activated += OnRoutePressed;
            linkButton.Activated += OnLinkPressed;
            destinationButton.Activated += OnDestinationPressed;
            sessionAuthority.ResetOccurred += OnSessionReset;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (routeButton != null) routeButton.Activated -= OnRoutePressed;
            if (linkButton != null) linkButton.Activated -= OnLinkPressed;
            if (destinationButton != null) destinationButton.Activated -= OnDestinationPressed;
            if (sessionAuthority != null) sessionAuthority.ResetOccurred -= OnSessionReset;
            subscribed = false;
        }

        private void OnRoutePressed()
        {
            EnsureDomain();
            var result = challenge.Press(++sequence, sessionAuthority.Token);
            if (result.Accepted)
            {
                if (!routePresenter.Present(result.Route, result.Destination))
                    throw new InvalidOperationException("C01 presentation is missing a domain route waypoint.");
                Render(result.Completed
                    ? "C01 PASS\nKITCHEN → NICHE 413 → KITCHEN"
                    : "Маршрут: " + Friendly(result.Destination));
                EmitTelemetry("show-route", result.Accepted ? "accepted" : "rejected");
            }
            else if (result.NoHandler)
            {
                routePresenter.Clear();
                Render("НЕТ HANDLER\nмаршрут не изменён");
                EmitTelemetry("show-route", "no-handler");
            }
            else
            {
                Render("Событие отклонено\nтекущей сессией");
                EmitTelemetry("show-route", "session-rejected");
            }
        }

        private void OnLinkPressed()
        {
            EnsureDomain();
            var changed = challenge.AttachHandler(sessionAuthority.Token);
            Render(changed ? "OnPressed → ShowRoute\nподключено" : "Handler уже\nподключён");
            EmitTelemetry("link-handler", changed ? "linked" : "unchanged");
        }

        private void OnDestinationPressed()
        {
            EnsureDomain();
            var next = challenge.SelectedDestination == Kitchen ? Niche413 : Kitchen;
            var selected = challenge.SelectDestination(next, sessionAuthority.Token);
            Render(selected ? "Цель: " + Friendly(next) : "Цель не изменена");
            EmitTelemetry("select-target", selected ? "selected" : "rejected");
        }

        private void OnSessionReset()
        {
            EnsureDomain();
            challenge.ResetForCurrentSession();
            sequence = 0;
            routePresenter.Clear();
            Render("RESET\n1. SHOW ROUTE без handler");
            EmitTelemetry("reset", "clean-baseline");
        }

        private void Render(string status)
        {
            if (statusLabel != null) statusLabel.text = status;
            if (destinationLabel != null)
                destinationLabel.text = "TARGET\n" + Friendly(SelectedDestination);
            if (traceLabel == null) return;
            var entries = TraceSnapshot;
            traceLabel.text = entries.Length == 0
                ? "EVENT TRACE\n(empty)"
                : "EVENT TRACE\n" + string.Join("\n", entries.Skip(Math.Max(0, entries.Length - 5))
                    .Select(entry => "#" + entry.Sequence + " " + entry.Event + " → " + entry.Outcome));
        }

        private void EmitTelemetry(string action, string outcome)
        {
            Debug.Log("ITPU_C01 action=" + action +
                      " outcome=" + outcome +
                      " stage=" + Stage +
                      " handler=" + HandlerLinked +
                      " selected=" + SelectedDestination +
                      " route_points=" + (routePresenter == null ? 0 : routePresenter.VisiblePointCount) +
                      " completed=" + Completed +
                      " epoch=" + (sessionAuthority == null ? 0 : sessionAuthority.Token.Epoch));
        }

        private static string Friendly(string destination) =>
            destination == Niche413 ? "NICHE 413" : destination;

        private static Topology CreateTopology() => new(
            new[]
            {
                Origin, "HALL_LEFT", "RECEPTION_FRONT", "HALL_TOP", Niche413, "HALL_RIGHT", Kitchen,
            },
            new[]
            {
                Tuple.Create(Origin, "HALL_LEFT"),
                Tuple.Create("HALL_LEFT", "RECEPTION_FRONT"),
                Tuple.Create("RECEPTION_FRONT", "HALL_TOP"),
                Tuple.Create("HALL_TOP", Niche413),
                Tuple.Create("HALL_TOP", "HALL_RIGHT"),
                Tuple.Create("HALL_RIGHT", Kitchen),
            });
    }
}
