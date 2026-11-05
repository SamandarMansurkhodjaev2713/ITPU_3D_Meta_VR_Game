using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class PaperBasketDomainUnityTests
    {
        [Test]
        public void ScoreRequiresTrackedDownwardMouthEntryAndContinuousDwell()
        {
            var probe = new PaperBasketProbe(0.15);
            Assert.That(probe.Tick(true, 1, false), Is.False);
            probe.EnterMouth(false, true);
            Assert.That(probe.Tick(true, 1, false), Is.False);
            probe.EnterMouth(true, false);
            Assert.That(probe.Tick(true, 1, false), Is.False);
            probe.EnterMouth(true, true);
            Assert.That(probe.Tick(true, 0.1, false), Is.False);
            Assert.That(probe.Tick(true, 0.05, false), Is.True);
            Assert.That(probe.Tick(true, 1, false), Is.False, "one throw scores only once");
        }

        [Test]
        public void LeavingInsideCancelsCandidateAndPauseFreezesDwell()
        {
            var probe = new PaperBasketProbe(0.15);
            probe.EnterMouth(true, true);
            Assert.That(probe.Tick(true, 0.1, true), Is.False);
            Assert.That(probe.Tick(true, 0.1, false), Is.False);
            Assert.That(probe.Tick(false, 0, false), Is.False);
            Assert.That(probe.Tick(true, 1, false), Is.False);
        }

        [Test]
        public void ResetAllowsANewIndependentScore()
        {
            var probe = new PaperBasketProbe(0.1);
            probe.EnterMouth(true, true);
            Assert.That(probe.Tick(true, 0.1, false), Is.True);
            probe.Reset();
            probe.EnterMouth(true, true);
            Assert.That(probe.Tick(true, 0.1, false), Is.True);
        }

        [Test]
        public void RecoverablePropNeverReturnsWhileHeldOrPausedAndTrackingLossKeepsOwnership()
        {
            var session = new Session();
            var prop = new RecoverableProp(session);
            Assert.That(prop.Hold(session.Token), Is.True);
            prop.LostTracking();
            Assert.That(prop.Held, Is.True);
            Assert.That(prop.TrackingValid, Is.False);
            Assert.That(prop.TryReturn(true, session.Token), Is.False);
            Assert.That(prop.Release(session.Token), Is.True);
            session.SetPause("paper.test", true);
            Assert.That(prop.TryReturn(true, session.Token), Is.False);
            session.SetPause("paper.test", false);
            Assert.That(prop.TryReturn(false, session.Token), Is.False);
            Assert.That(prop.TryReturn(true, session.Token), Is.True);
        }
    }
}
