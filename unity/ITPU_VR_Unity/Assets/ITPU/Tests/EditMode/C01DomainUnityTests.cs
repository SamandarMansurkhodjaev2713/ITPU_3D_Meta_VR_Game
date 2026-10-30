using System;
using System.Linq;
using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class C01DomainUnityTests
    {
        [Test]
        public void ActualNoHandlerThenKitchenNicheKitchenIsRequired()
        {
            var session = new Session();
            var challenge = new C01RouteChallenge(session, Map(), "WORK_403", "KITCHEN", "NICHE_413");
            var token = session.Token;

            var missing = challenge.Press(1, token);
            Assert.That(missing.Accepted, Is.False);
            Assert.That(missing.NoHandler, Is.True);
            Assert.That(missing.Route, Is.Empty);
            Assert.That(challenge.AttachHandler(token), Is.True);
            Assert.That(challenge.RouteSnapshot(), Is.Empty, "linking alone must not invent a route");

            Assert.That(challenge.Press(2, token).Stage, Is.EqualTo(C01Stage.FirstRoute));
            Assert.That(challenge.SelectDestination("NICHE_413", token), Is.True);
            Assert.That(challenge.Press(3, token).Stage, Is.EqualTo(C01Stage.SecondRoute));
            Assert.That(challenge.SelectDestination("KITCHEN", token), Is.True);
            var verified = challenge.Press(4, token);
            Assert.That(verified.Completed, Is.True);
            Assert.That(verified.Stage, Is.EqualTo(C01Stage.Verified));
            Assert.That(challenge.TraceSnapshot().Count(item => item.Event == "ShowRoute"), Is.EqualTo(3));
        }

        [Test]
        public void DuplicateAndResetCannotAdvanceOrReuseStaleToken()
        {
            var session = new Session();
            var challenge = new C01RouteChallenge(session, Map(), "WORK_403", "KITCHEN", "NICHE_413");
            var stale = session.Token;
            Assert.That(challenge.AttachHandler(stale), Is.True);
            Assert.That(challenge.Press(1, stale).Accepted, Is.True);
            var traceCount = challenge.TraceSnapshot().Length;
            Assert.That(challenge.Press(1, stale).Accepted, Is.False);
            Assert.That(challenge.TraceSnapshot(), Has.Length.EqualTo(traceCount));

            session.Reset();
            challenge.ResetForCurrentSession();
            Assert.That(challenge.Press(2, stale).Accepted, Is.False);
            Assert.That(challenge.SelectDestination("NICHE_413", stale), Is.False);
            Assert.That(challenge.AttachHandler(stale), Is.False);
            Assert.That(challenge.RouteSnapshot(), Is.Empty);
            Assert.That(challenge.TraceSnapshot(), Is.Empty);
            Assert.That(challenge.Stage, Is.EqualTo(C01Stage.MissingHandler));
            Assert.That(challenge.Completed, Is.False);
            Assert.That(challenge.SelectedDestination, Is.EqualTo("KITCHEN"));
            Assert.That(challenge.AttachHandler(session.Token), Is.True);
        }

        [Test]
        public void TraceTimeClampsClockRollback()
        {
            long now = 100;
            var trace = new TraceBuffer(4, () => now);
            var token = new Session().Token;
            trace.Add(token, 1, "press", "OnPressed", "first");
            now = 109;
            trace.Add(token, 1, "route", "ShowRoute", "second");
            now = 103;
            trace.Add(token, 1, "route", "ShowRoute", "rollback");
            Assert.That(trace.Snapshot().Select(item => item.RelativeTimeMs),
                Is.EqualTo(new long[] { 0, 9, 9 }));
        }

        private static Topology Map() => new(
            new[]
            {
                "WORK_403", "HALL_LEFT", "RECEPTION_FRONT", "HALL_TOP", "NICHE_413", "HALL_RIGHT", "KITCHEN",
            },
            new[]
            {
                Tuple.Create("WORK_403", "HALL_LEFT"),
                Tuple.Create("HALL_LEFT", "RECEPTION_FRONT"),
                Tuple.Create("RECEPTION_FRONT", "HALL_TOP"),
                Tuple.Create("HALL_TOP", "NICHE_413"),
                Tuple.Create("HALL_TOP", "HALL_RIGHT"),
                Tuple.Create("HALL_RIGHT", "KITCHEN"),
            });
    }
}
