using ITPU.Domain;
using NUnit.Framework;

namespace ITPU.XR.Tests.EditMode
{
    public sealed class CoffeeDomainUnityTests
    {
        [Test]
        public void PressWithoutCup_IsRejectedAndDoesNotMutateFill()
        {
            var machine = new CoffeeMachine(1);
            Assert.That(machine.Press(), Is.False);
            machine.Tick(1);
            Assert.That(machine.State, Is.EqualTo(CoffeeState.Idle));
            Assert.That(machine.Fill, Is.Zero);
        }

        [Test]
        public void RemoveDuringBrew_PausesAndRedockCanResumeToReady()
        {
            var machine = new CoffeeMachine(1);
            machine.Dock();
            Assert.That(machine.State, Is.EqualTo(CoffeeState.CupReady));
            Assert.That(machine.Press(), Is.True);
            machine.Tick(0.4);
            var partial = machine.Fill;
            Assert.That(partial, Is.GreaterThan(0));

            machine.Remove();
            Assert.That(machine.State, Is.EqualTo(CoffeeState.Paused));
            machine.Tick(0.5);
            Assert.That(machine.Fill, Is.EqualTo(partial).Within(1e-9));

            machine.Dock();
            Assert.That(machine.State, Is.EqualTo(CoffeeState.CupReady));
            Assert.That(machine.Press(), Is.True);
            for (var index = 0; index < 4; index++) machine.Tick(0.25);
            Assert.That(machine.State, Is.EqualTo(CoffeeState.Ready));
            Assert.That(machine.Fill, Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void GlobalPauseAndReset_AreDeterministic()
        {
            var machine = new CoffeeMachine(1);
            machine.Dock();
            Assert.That(machine.Press(), Is.True);
            machine.Tick(0.5, true);
            Assert.That(machine.Fill, Is.Zero);
            Assert.That(machine.State, Is.EqualTo(CoffeeState.Brewing));

            machine.Reset();
            Assert.That(machine.State, Is.EqualTo(CoffeeState.Idle));
            Assert.That(machine.CupPresent, Is.False);
            Assert.That(machine.Fill, Is.Zero);
        }

        [Test]
        public void EmptyRefusesHeldCup()
        {
            var machine = new CoffeeMachine(1);
            machine.Dock();
            Assert.That(machine.Press(), Is.True);
            machine.Tick(0.25);
            Assert.That(machine.Empty(true), Is.False);
            Assert.That(machine.Fill, Is.GreaterThan(0));
            Assert.That(machine.Empty(false), Is.True);
            Assert.That(machine.State, Is.EqualTo(CoffeeState.CupReady));
            Assert.That(machine.Fill, Is.Zero);
        }

        [Test]
        public void TrackingLoss_DoesNotReleaseHeldPropOrPermitRecovery()
        {
            var session = new Session();
            var prop = new RecoverableProp(session);
            var token = session.Token;

            Assert.That(prop.Hold(token), Is.True);
            prop.LostTracking();

            Assert.That(prop.Held, Is.True);
            Assert.That(prop.TrackingValid, Is.False);
            Assert.That(prop.TryReturn(true, token), Is.False);
            Assert.That(prop.Release(token), Is.True);
            Assert.That(prop.TryReturn(true, token), Is.True);
        }
    }
}
