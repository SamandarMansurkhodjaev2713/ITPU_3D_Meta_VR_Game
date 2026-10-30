using System;

namespace ITPU.Domain
{
    public enum C03Stage
    {
        BaselineReady,
        BaselineBypassProven,
        GateInstalled,
        GuestViewPassed,
        GuestEditDenied,
        Verified,
    }

    public readonly struct C03AttemptResult
    {
        public readonly bool Accepted, Allowed, Mutated, Completed;
        public readonly int VersionBefore, VersionAfter;
        public readonly DemoRole Role;
        public readonly DemoOperation Operation;
        public readonly C03Stage Stage;

        public C03AttemptResult(bool accepted, bool allowed, bool mutated, bool completed,
            int versionBefore, int versionAfter, DemoRole role, DemoOperation operation, C03Stage stage)
        {
            Accepted = accepted;
            Allowed = allowed;
            Mutated = mutated;
            Completed = completed;
            VersionBefore = versionBefore;
            VersionAfter = versionAfter;
            Role = role;
            Operation = operation;
            Stage = stage;
        }
    }

    // Bounded C03 state machine. Every request runs on a fresh sandbox copy, so
    // the intentional baseline defect can never touch production or another case.
    public sealed class C03AuthorizationChallenge
    {
        private readonly Session session;
        private readonly TraceBuffer trace = new TraceBuffer();
        private SessionToken epoch;
        private CompiledGraph policy;
        private bool guestViewPassed, guestEditDenied, teamEditPassed;

        public C03Stage Stage { get; private set; }
        public bool GateInstalled { get { return policy != null; } }
        public bool Completed { get; private set; }
        public bool GuestViewPassed { get { return guestViewPassed; } }
        public bool GuestEditDenied { get { return guestEditDenied; } }
        public bool TeamEditPassed { get { return teamEditPassed; } }
        public long PolicyRevision { get; private set; }
        public bool PrerequisiteMet
        {
            get { return session.IsCompleted(Challenge.C01) && session.IsCompleted(Challenge.C02); }
        }
        public PermissionFlags PolicyFlags
        {
            get { return policy == null ? default(PermissionFlags) : policy.Permissions; }
        }

        public C03AuthorizationChallenge(Session session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            ResetForCurrentSession();
        }

        public TraceEntry[] TraceSnapshot() { return trace.Snapshot(); }

        public C03AttemptResult DemonstrateBaselineBypass(bool tracking, SessionToken token)
        {
            if (!Active(token) || !tracking || !PrerequisiteMet || Stage != C03Stage.BaselineReady)
                return Rejected(DemoRole.Guest, DemoOperation.EditRoute);
            var sandbox = new AuthSandbox(GraphCompiler.Compile(GraphCompiler.Template(Challenge.C03, false)));
            var before = sandbox.RouteVersion;
            var result = sandbox.Execute(DemoRole.Guest, DemoOperation.EditRoute);
            var after = sandbox.RouteVersion;
            var proven = result.Allowed && result.Mutated && before == 0 && after == 1;
            if (proven)
            {
                Stage = C03Stage.BaselineBypassProven;
                trace.Add(token, 3, "baseline", "DemoBypass", "ALLOW_0_TO_1");
            }
            return new C03AttemptResult(proven, result.Allowed, result.Mutated, false,
                before, after, DemoRole.Guest, DemoOperation.EditRoute, Stage);
        }

        public bool InstallGate(PermissionFlags flags, SessionToken token)
        {
            if (!Active(token) || !PrerequisiteMet || Stage == C03Stage.BaselineReady) return false;
            var definition = GraphCompiler.Template(Challenge.C03, true);
            definition.Permissions = flags;
            policy = GraphCompiler.Compile(definition);
            session.InvalidateFrom(Challenge.C03);
            checked { PolicyRevision++; }
            guestViewPassed = guestEditDenied = teamEditPassed = false;
            Completed = false;
            Stage = C03Stage.GateInstalled;
            trace.Add(token, 3, "check", "RequirePermissionInstalled", "policy-revision");
            return true;
        }

        public C03AttemptResult Execute(DemoRole role, DemoOperation operation, bool tracking,
            long expectedPolicyRevision, SessionToken token)
        {
            if (!Active(token) || !tracking || expectedPolicyRevision != PolicyRevision ||
                !PrerequisiteMet || policy == null || Completed ||
                !Enum.IsDefined(typeof(DemoRole), role) || !Enum.IsDefined(typeof(DemoOperation), operation))
                return Rejected(role, operation);
            // A new copy for every test is a product invariant, not just test setup.
            var sandbox = new AuthSandbox(policy);
            var before = sandbox.RouteVersion;
            var result = sandbox.Execute(role, operation);
            var after = sandbox.RouteVersion;
            var accepted = Observe(role, operation, result, before, after, token);
            return new C03AttemptResult(accepted, result.Allowed, result.Mutated, Completed,
                before, after, role, operation, Stage);
        }

        public void ResetForCurrentSession()
        {
            epoch = session.Token;
            session.InvalidateFrom(Challenge.C03);
            checked { PolicyRevision++; }
            policy = null;
            guestViewPassed = guestEditDenied = teamEditPassed = false;
            Completed = false;
            Stage = C03Stage.BaselineReady;
            trace.Clear();
        }

        private bool Observe(DemoRole role, DemoOperation operation, DemoResult result,
            int before, int after, SessionToken token)
        {
            bool accepted;
            string outcome;
            if (role == DemoRole.Guest && operation == DemoOperation.ViewPublicProject)
            {
                accepted = result.Allowed && !result.Mutated && before == 0 && after == 0;
                guestViewPassed = accepted;
                if (accepted) Stage = C03Stage.GuestViewPassed;
                outcome = accepted ? "ALLOW_0_TO_0" : "AUTH01_FAIL";
            }
            else if (role == DemoRole.Guest && operation == DemoOperation.EditRoute)
            {
                accepted = !result.Allowed && !result.Mutated && before == 0 && after == 0;
                guestEditDenied = accepted;
                if (accepted) Stage = C03Stage.GuestEditDenied;
                outcome = accepted ? "DENY_0_TO_0" : "AUTH02_FAIL";
            }
            else if (role == DemoRole.Team && operation == DemoOperation.EditRoute)
            {
                accepted = result.Allowed && result.Mutated && before == 0 && after == 1;
                teamEditPassed = accepted;
                outcome = accepted ? "ALLOW_0_TO_1" : "AUTH03_FAIL";
            }
            else
            {
                accepted = false;
                outcome = "UNKNOWN_DENY";
            }
            trace.Add(token, 3, "request", "AuthorizationCase", outcome);
            if (guestViewPassed && guestEditDenied && teamEditPassed && AuthSandbox.Verify(policy))
            {
                Completed = session.Complete(Challenge.C03, token);
                if (Completed)
                {
                    Stage = C03Stage.Verified;
                    trace.Add(token, 3, "complete", "C03Verified", "ALLOW_DENY_ALLOW");
                }
            }
            return accepted;
        }

        private C03AttemptResult Rejected(DemoRole role, DemoOperation operation)
        {
            return new C03AttemptResult(false, false, false, Completed, 0, 0, role, operation, Stage);
        }

        private bool Active(SessionToken token)
        {
            return token.Equals(epoch) && session.Valid(token) && !session.Paused;
        }
    }
}
