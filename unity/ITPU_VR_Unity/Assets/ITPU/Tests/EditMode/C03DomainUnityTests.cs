using System.Linq;
using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class C03DomainUnityTests
    {
        [Test]
        public void BaselineIsIsolatedAndExactMatrixCompletesCurrentRevision()
        {
            var session = ReadyForC03();
            var challenge = new C03AuthorizationChallenge(session);
            var token = session.Token;
            var baseline = challenge.DemonstrateBaselineBypass(true, token);
            Assert.That(baseline.Accepted, Is.True);
            Assert.That(baseline.Allowed, Is.True);
            Assert.That(baseline.Mutated, Is.True);
            Assert.That((baseline.VersionBefore, baseline.VersionAfter), Is.EqualTo((0, 1)));

            Assert.That(challenge.InstallGate(PermissionFlags.Correct, token), Is.True);
            var revision = challenge.PolicyRevision;
            var view = challenge.Execute(DemoRole.Guest, DemoOperation.ViewPublicProject,
                true, revision, token);
            Assert.That(view.Accepted && view.Allowed && !view.Mutated, Is.True);
            Assert.That((view.VersionBefore, view.VersionAfter), Is.EqualTo((0, 0)));
            var deny = challenge.Execute(DemoRole.Guest, DemoOperation.EditRoute,
                true, revision, token);
            Assert.That(deny.Accepted && !deny.Allowed && !deny.Mutated, Is.True);
            Assert.That((deny.VersionBefore, deny.VersionAfter), Is.EqualTo((0, 0)));
            var edit = challenge.Execute(DemoRole.Team, DemoOperation.EditRoute,
                true, revision, token);
            Assert.That(edit.Accepted && edit.Allowed && edit.Mutated, Is.True);
            Assert.That((edit.VersionBefore, edit.VersionAfter), Is.EqualTo((0, 1)));
            Assert.That(challenge.Completed, Is.True);
            Assert.That(session.Ready, Is.True);
        }

        [TestCase(true, true, true)]
        [TestCase(false, false, false)]
        public void AllowAllAndDenyAllCannotComplete(bool guestView, bool guestEdit, bool teamEdit)
        {
            var session = ReadyForC03();
            var challenge = new C03AuthorizationChallenge(session);
            var token = session.Token;
            challenge.DemonstrateBaselineBypass(true, token);
            Assert.That(challenge.InstallGate(new PermissionFlags(guestView, guestEdit, teamEdit), token), Is.True);
            var revision = challenge.PolicyRevision;
            challenge.Execute(DemoRole.Guest, DemoOperation.ViewPublicProject, true, revision, token);
            challenge.Execute(DemoRole.Guest, DemoOperation.EditRoute, true, revision, token);
            challenge.Execute(DemoRole.Team, DemoOperation.EditRoute, true, revision, token);
            Assert.That(challenge.Completed, Is.False);
            Assert.That(session.IsCompleted(Challenge.C03), Is.False);
        }

        [Test]
        public void StaleRevisionPauseTrackingAndResetProduceNoTraceOrMutation()
        {
            var session = ReadyForC03();
            var challenge = new C03AuthorizationChallenge(session);
            var staleToken = session.Token;
            challenge.DemonstrateBaselineBypass(true, staleToken);
            challenge.InstallGate(PermissionFlags.Correct, staleToken);
            var staleRevision = challenge.PolicyRevision;
            challenge.InstallGate(PermissionFlags.Correct, staleToken);
            var count = challenge.TraceSnapshot().Length;
            Assert.That(challenge.Execute(DemoRole.Guest, DemoOperation.ViewPublicProject,
                true, staleRevision, staleToken).Accepted, Is.False);
            Assert.That(challenge.Execute(DemoRole.Guest, DemoOperation.ViewPublicProject,
                false, challenge.PolicyRevision, staleToken).Accepted, Is.False);
            session.SetPause("test", true);
            Assert.That(challenge.Execute(DemoRole.Guest, DemoOperation.ViewPublicProject,
                true, challenge.PolicyRevision, staleToken).Accepted, Is.False);
            Assert.That(challenge.TraceSnapshot(), Has.Length.EqualTo(count));

            session.SetPause("test", false);
            session.Reset();
            challenge.ResetForCurrentSession();
            Assert.That(challenge.Execute(DemoRole.Guest, DemoOperation.ViewPublicProject,
                true, challenge.PolicyRevision, staleToken).Accepted, Is.False);
            Assert.That(challenge.Stage, Is.EqualTo(C03Stage.BaselineReady));
            Assert.That(challenge.PrerequisiteMet, Is.False);
            Assert.That(challenge.TraceSnapshot(), Is.Empty);
        }

        [Test]
        public void DenyTraceChecksBeforeRejectAndNeverExecutes()
        {
            var policy = GraphCompiler.Compile(GraphCompiler.Template(Challenge.C03, true));
            var sandbox = new AuthSandbox(policy);
            var result = sandbox.Execute(DemoRole.Guest, DemoOperation.EditRoute);
            Assert.That(result.Allowed || result.Mutated, Is.False);
            Assert.That(sandbox.RouteVersion, Is.Zero);
            Assert.That(sandbox.Trace.Snapshot().Select(item => item.Event),
                Is.EqualTo(new[] { "ReceiveDemoRequest", "RequirePermission", "RejectDemoOperation" }));
        }

        private static Session ReadyForC03()
        {
            var session = new Session();
            var token = session.Token;
            Assert.That(session.Complete(Challenge.C01, token), Is.True);
            Assert.That(session.Complete(Challenge.C02, token), Is.True);
            return session;
        }
    }
}
