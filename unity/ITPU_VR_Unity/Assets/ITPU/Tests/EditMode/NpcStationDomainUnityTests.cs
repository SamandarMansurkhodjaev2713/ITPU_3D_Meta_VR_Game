using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class NpcStationDomainUnityTests
    {
        [Test]
        public void Look_IsBoundedAndReturnsTowardNeutralWhenTrackingIsLost()
        {
            var station = new NpcStation(new Vec3(1d, 2d, 3d));

            for (var index = 0; index < 20; index++) station.Look(100d, 0.1d);

            Assert.That(station.Anchor, Is.EqualTo(new Vec3(1d, 2d, 3d)));
            Assert.That(station.BodyYaw, Is.EqualTo(25d).Within(0.001d));
            Assert.That(station.HeadYaw, Is.EqualTo(35d).Within(0.001d));

            for (var index = 0; index < 20; index++) station.Look(100d, 0.1d, tracked: false);

            Assert.That(station.BodyYaw, Is.EqualTo(0d).Within(0.001d));
            Assert.That(station.HeadYaw, Is.EqualTo(0d).Within(0.001d));
        }

        [Test]
        public void Reset_ClearsLookAndGestureWithoutChangingAnchor()
        {
            var anchor = new Vec3(-2d, 0.25d, 4d);
            var station = new NpcStation(anchor);
            station.Look(-80d, 0.1d);
            station.React(NpcGesture.Explain);

            station.Reset();

            Assert.That(station.Anchor, Is.EqualTo(anchor));
            Assert.That(station.BodyYaw, Is.Zero);
            Assert.That(station.HeadYaw, Is.Zero);
            Assert.That(station.Gesture, Is.EqualTo(NpcGesture.Idle));
        }

        [Test]
        public void Look_DoesNotAdvanceWhilePaused()
        {
            var station = new NpcStation(new Vec3(0d, 0d, 0d));

            station.Look(70d, 0.1d, paused: true);

            Assert.That(station.BodyYaw, Is.Zero);
            Assert.That(station.HeadYaw, Is.Zero);
        }
    }
}
