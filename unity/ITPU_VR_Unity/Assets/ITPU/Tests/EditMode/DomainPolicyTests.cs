using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class DomainPolicyTests
    {
        [Test]
        public void ControllerWalk_RequiresNeutralAfterFocusLoss()
        {
            var policy = new ControllerWalkPolicy();
            var sample = ValidWalk(0.02);
            Assert.That(policy.Update(sample, 0.02, 0.02), Is.Null);
            sample.Time = 0.04;
            sample.Y = 1;
            var moving = policy.Update(sample, 0.02, 0.04);
            Assert.That(moving, Is.Not.Null);
            var consumed = policy.Consume(moving, 0.04, true);
            Assert.That(consumed, Is.Not.Null);
            Assert.That(consumed.Value.Sequence, Is.EqualTo(moving.Value.Sequence));

            sample.Time = 0.06;
            sample.Focused = false;
            Assert.That(policy.Update(sample, 0.02, 0.06), Is.Null);
            sample.Time = 0.08;
            sample.Focused = true;
            Assert.That(policy.Update(sample, 0.02, 0.08), Is.Null, "held stick must not resume");
            sample.Time = 0.10;
            sample.Y = 0;
            Assert.That(policy.Update(sample, 0.02, 0.10), Is.Null);
            sample.Time = 0.12;
            sample.Y = 1;
            Assert.That(policy.Update(sample, 0.02, 0.12), Is.Not.Null, "neutral rearmed motion");
        }

        [Test]
        public void ControllerWalk_SnapIsThirtyDegreesAndRequiresNeutralRearm()
        {
            var policy = new ControllerWalkPolicy();
            var sample = ValidWalk(1.0);
            Assert.That(policy.Update(sample, 0.02, 1.0), Is.Null);

            sample.Time = 1.02;
            sample.Turn = 1;
            var first = policy.Update(sample, 0.02, 1.02);
            Assert.That(first, Is.Not.Null);
            var firstConsumed = policy.Consume(first, 1.02, true);
            Assert.That(firstConsumed, Is.Not.Null);
            Assert.That(firstConsumed.Value.TurnDegrees, Is.EqualTo(30.0));

            for (var index = 0; index < 20; index++)
            {
                var now = 1.04 + index * 0.02;
                sample.Time = now;
                Assert.That(policy.Update(sample, 0.02, now), Is.Null,
                    "held turn must not repeat after cooldown without neutral");
            }

            sample.Time = 1.46;
            sample.Turn = 0;
            Assert.That(policy.Update(sample, 0.02, 1.46), Is.Null);
            sample.Time = 1.48;
            sample.Turn = -1;
            var second = policy.Update(sample, 0.02, 1.48);
            Assert.That(second, Is.Not.Null);
            var secondConsumed = policy.Consume(second, 1.48, true);
            Assert.That(secondConsumed, Is.Not.Null);
            Assert.That(secondConsumed.Value.TurnDegrees, Is.EqualTo(-30.0));
        }

        [Test]
        public void ControllerWalk_SmoothModeScalesToFortyFiveDegreesPerSecondAndModeChangesRearm()
        {
            var policy = new ControllerWalkPolicy();
            var sample = ValidWalk(2.0);
            Assert.That(policy.Update(sample, 0.02, 2.0), Is.Null);

            policy.SetMode(true, true);
            sample.Time = 2.02;
            sample.Turn = 1;
            Assert.That(policy.Update(sample, 0.02, 2.02), Is.Null,
                "a held stick cannot turn immediately after the mode change");
            sample.Time = 2.04;
            sample.Turn = 0;
            Assert.That(policy.Update(sample, 0.02, 2.04), Is.Null);

            sample.Time = 2.06;
            sample.Turn = 1;
            var full = policy.Consume(policy.Update(sample, 0.02, 2.06), 2.06, true);
            Assert.That(full, Is.Not.Null);
            Assert.That(full.Value.TurnDegrees, Is.EqualTo(0.9).Within(0.0001));

            sample.Time = 2.08;
            sample.Turn = -0.625;
            var partial = policy.Consume(policy.Update(sample, 0.02, 2.08), 2.08, true);
            Assert.That(partial, Is.Not.Null);
            Assert.That(partial.Value.TurnDegrees, Is.EqualTo(-0.45).Within(0.0001));

            sample.Time = 2.10;
            sample.Turn = ControllerWalkDefaults.TurnOff;
            Assert.That(policy.Update(sample, 0.02, 2.10), Is.Null,
                "smooth turn must stop inside the neutral threshold");

            policy.SetMode(true, false);
            sample.Time = 2.12;
            sample.Turn = 1;
            Assert.That(policy.Update(sample, 0.02, 2.12), Is.Null,
                "switching back to snap also requires neutral");
            sample.Time = 2.14;
            sample.Turn = 0;
            Assert.That(policy.Update(sample, 0.02, 2.14), Is.Null);
            sample.Time = 2.16;
            sample.Turn = 1;
            var snap = policy.Consume(policy.Update(sample, 0.02, 2.16), 2.16, true);
            Assert.That(snap, Is.Not.Null);
            Assert.That(snap.Value.TurnDegrees, Is.EqualTo(30.0));
        }

        [Test]
        public void TeleportGate_UiAndHeldObjectOutrankTeleport()
        {
            var gate = new TeleportIntentGate();
            Assert.That(gate.Aim(0.0, Facts(uiBusy: true)), Is.False);
            Assert.That(gate.Aim(0.1, Facts(holding: true)), Is.False);
            Assert.That(gate.Aim(0.2, Facts()), Is.True);
            Assert.That(gate.Press(0.36, Facts()), Is.True);
            var intent = gate.Release(0.50, Facts());
            Assert.That(intent, Is.Not.Null);
            Assert.That(gate.Commit(0.51, intent, Facts()), Is.True);
            Assert.That(gate.State, Is.EqualTo(TeleportIntentState.Cooldown));
        }

        [Test]
        public void ControllerDefaults_MatchProductContract()
        {
            Assert.That(ControllerWalkDefaults.Snap, Is.EqualTo(30.0));
            Assert.That(ControllerWalkDefaults.SmoothDps, Is.EqualTo(45.0));
            Assert.That(ControllerWalkDefaults.Speed, Is.EqualTo(1.2));
            Assert.That(ControllerWalkDefaults.MaxAge, Is.LessThanOrEqualTo(0.2));
        }

        private static WalkSample ValidWalk(double time) => new()
        {
            Time = time,
            X = 0,
            Y = 0,
            Turn = 0,
            Yaw = 0,
            HeadTracked = true,
            MoveTracked = true,
            TurnTracked = true,
            Focused = true,
            Paused = false,
            Modal = false,
            PrecisionBusy = false,
            Teleporting = false,
            Carrying = false
        };

        private static TeleportFacts Facts(bool uiBusy = false, bool holding = false) =>
            new("floor.403", "left-hand", "WO-17-v1", 2.0,
                true, true, true, false, uiBusy, holding, false,
                true, true, true, true);
    }
}
