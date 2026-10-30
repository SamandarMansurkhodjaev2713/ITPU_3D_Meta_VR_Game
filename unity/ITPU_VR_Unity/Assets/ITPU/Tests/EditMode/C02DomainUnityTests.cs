using System.Linq;
using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class C02DomainUnityTests
    {
        [Test]
        public void BrokenDepartureThenWaitHoldResumeCompletesActualC02()
        {
            var session = new Session();
            var challenge = new C02WaitChallenge(session, new[] { "GUEST_CHECKPOINT" });
            var token = session.Token;

            Assert.That(challenge.Step(true, false, true, token), Is.False, "C01 is a real prerequisite");
            Assert.That(session.Complete(Challenge.C01, token), Is.True);
            Assert.That(challenge.Step(true, false, true, token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.BrokenGuideLeft));
            Assert.That(challenge.InstallWaitRule(token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitRuleInstalled));

            Assert.That(challenge.Step(true, false, true, token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitingForGuest));
            var index = challenge.WaypointIndex;
            Assert.That(challenge.Step(true, false, true, token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitingForGuest));
            Assert.That(challenge.WaypointIndex, Is.EqualTo(index));

            Assert.That(challenge.Step(false, true, true, token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.GuestConfirmed));
            Assert.That(challenge.Step(false, false, true, token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.Verified));
            Assert.That(challenge.Completed, Is.True);
            Assert.That(session.IsCompleted(Challenge.C02), Is.True);
            Assert.That(challenge.TraceSnapshot().Select(item => item.Event), Does.Contain("GuideLeftWithoutGuest"));
            Assert.That(challenge.TraceSnapshot().Count(item => item.Event == "WaitForGuest"), Is.EqualTo(2));
        }

        [Test]
        public void PauseTrackingAndResetCannotAdvanceOrReuseStaleToken()
        {
            var session = new Session();
            var challenge = new C02WaitChallenge(session, new[] { "GUEST_CHECKPOINT" });
            var stale = session.Token;
            Assert.That(session.Complete(Challenge.C01, stale), Is.True);
            Assert.That(challenge.Step(true, false, true, stale), Is.True);
            Assert.That(challenge.InstallWaitRule(stale), Is.True);

            session.SetPause("test.pause", true);
            Assert.That(challenge.Step(true, false, true, stale), Is.False);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitRuleInstalled));
            session.SetPause("test.pause", false);
            Assert.That(challenge.Step(true, false, false, stale), Is.False);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitRuleInstalled));
            Assert.That(challenge.Step(true, false, true, stale), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitingForGuest));

            session.Reset();
            challenge.ResetForCurrentSession();
            Assert.That(challenge.Step(true, false, true, stale), Is.False);
            Assert.That(challenge.InstallWaitRule(stale), Is.False);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.BrokenReady));
            Assert.That(challenge.TraceSnapshot(), Is.Empty);
            Assert.That(challenge.PrerequisiteMet, Is.False);
        }

        [Test]
        public void ConfirmedForwardTeleportWaitsAndBacktrackCannotRewindGuide()
        {
            var session = new Session();
            var challenge = new C02WaitChallenge(session, new[] { "POINT_A", "POINT_B" });
            var token = session.Token;
            Assert.That(session.Complete(Challenge.C01, token), Is.True);
            Assert.That(challenge.Step(true, false, true, token), Is.True);
            Assert.That(challenge.InstallWaitRule(token), Is.True);

            Assert.That(challenge.ConfirmedTeleport("POINT_B", true, token), Is.True);
            Assert.That(challenge.Stage, Is.EqualTo(C02Stage.WaitingForGuest));
            Assert.That(challenge.WaypointIndex, Is.EqualTo(1));
            Assert.That(challenge.ConfirmedTeleport("POINT_A", true, token), Is.False);
            Assert.That(challenge.WaypointIndex, Is.EqualTo(1));
            Assert.That(challenge.Step(false, true, true, token), Is.True);
            Assert.That(challenge.Step(false, false, true, token), Is.True);
            Assert.That(challenge.Completed, Is.True);
        }
    }
}
