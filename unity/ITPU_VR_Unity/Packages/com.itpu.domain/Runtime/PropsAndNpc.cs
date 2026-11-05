using System;

namespace ITPU.Domain
{
    public enum CoffeeState { Idle, CupReady, Brewing, Paused, Ready }

    public sealed class CoffeeMachine
    {
        private readonly double seconds;

        public bool CupPresent { get; private set; }
        public double Fill { get; private set; }
        public CoffeeState State { get; private set; }

        public CoffeeMachine(double seconds = 6)
        {
            Guard.Finite(seconds, "seconds");
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            this.seconds = seconds;
            Reset();
        }

        public void Reset()
        {
            CupPresent = false;
            Fill = 0;
            State = CoffeeState.Idle;
        }

        public void Dock()
        {
            CupPresent = true;
            if (Fill >= 1) State = CoffeeState.Ready;
            else if (State != CoffeeState.Brewing) State = CoffeeState.CupReady;
        }

        public void Remove()
        {
            CupPresent = false;
            if (State == CoffeeState.Brewing) State = CoffeeState.Paused;
            else if (State != CoffeeState.Paused) State = CoffeeState.Idle;
        }

        public bool Press()
        {
            if (!CupPresent || Fill >= 1 || State == CoffeeState.Brewing) return false;
            State = CoffeeState.Brewing;
            return true;
        }

        public void Tick(double dt, bool paused = false)
        {
            Guard.NonNegative(dt, "dt");
            if (paused || !CupPresent || State != CoffeeState.Brewing) return;
            Fill = Math.Min(1, Fill + Math.Min(dt, .25) / seconds);
            if (Fill <= 1 - 1e-9) return;
            Fill = 1;
            State = CoffeeState.Ready;
        }

        public bool Empty(bool held)
        {
            if (held) return false;
            Fill = 0;
            State = CupPresent ? CoffeeState.CupReady : CoffeeState.Idle;
            return true;
        }
    }

    public readonly struct ReleaseResult
    {
        public readonly bool Released;
        public readonly bool MayThrow;

        public ReleaseResult(bool released, bool mayThrow)
        {
            Released = released;
            MayThrow = mayThrow;
        }
    }

    public sealed class PropOwnership
    {
        public string Owner { get; private set; }
        public bool Tracked { get; private set; }

        public bool Grab(string owner)
        {
            Guard.Id(owner);
            if (Owner != null) return false;
            Owner = owner;
            Tracked = true;
            return true;
        }

        public ReleaseResult Release(string owner, bool intentional)
        {
            if (Owner != owner) return new ReleaseResult(false, false);
            var canThrow = intentional && Tracked;
            Owner = null;
            Tracked = false;
            return new ReleaseResult(true, canThrow);
        }

        public void TrackingLost()
        {
            Owner = null;
            Tracked = false;
        }

        public void Reset()
        {
            Owner = null;
            Tracked = false;
        }
    }

    // Session-fenced recovery state shared by controller and optical-hand adapters.
    // Tracking loss only invalidates throw velocity; it never releases ownership.
    public sealed class RecoverableProp
    {
        private SessionToken epoch;
        private readonly Session session;

        public bool Held { get; private set; }
        public bool TrackingValid { get; private set; }
        public long ReturnCount { get; private set; }

        public RecoverableProp(Session session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            Reset();
        }

        public void Reset()
        {
            epoch = session.Token;
            Held = false;
            TrackingValid = true;
            ReturnCount = 0;
        }

        public bool Hold(SessionToken token)
        {
            if (!session.Valid(token) || !token.Equals(epoch) || session.Paused || Held) return false;
            Held = true;
            TrackingValid = true;
            return true;
        }

        public void LostTracking() => TrackingValid = false;

        public bool Release(SessionToken token)
        {
            if (!session.Valid(token) || !token.Equals(epoch) || !Held) return false;
            Held = false;
            return true;
        }

        public bool TryReturn(bool destinationClear, SessionToken token)
        {
            if (!session.Valid(token) || !token.Equals(epoch) || session.Paused || Held || !destinationClear)
                return false;
            ReturnCount++;
            TrackingValid = true;
            return true;
        }
    }

    public enum NpcGesture { Idle, Greet, Explain, Point, Acknowledge, Humor }

    public sealed class NpcStation
    {
        private readonly double bodyLimit;
        private readonly double headLimit;
        private readonly double speed;

        public Vec3 Anchor { get; }
        public double BodyYaw { get; private set; }
        public double HeadYaw { get; private set; }
        public NpcGesture Gesture { get; private set; }

        public NpcStation(Vec3 anchor, double bodyLimit = 25, double headLimit = 35, double speed = 70)
        {
            Anchor = anchor;
            this.bodyLimit = Guard.NonNegative(bodyLimit, "bodyLimit");
            this.headLimit = Guard.NonNegative(headLimit, "headLimit");
            Guard.Finite(speed, "speed");
            if (speed <= 0) throw new ArgumentException("Speed must be positive");
            this.speed = speed;
            Reset();
        }

        public void Reset()
        {
            BodyYaw = HeadYaw = 0;
            Gesture = NpcGesture.Idle;
        }

        public void Look(double relativeYaw, double dt, bool tracked = true, bool paused = false)
        {
            Guard.Finite(relativeYaw, "yaw");
            Guard.NonNegative(dt, "dt");
            if (paused) return;
            var y = tracked && Math.Abs(relativeYaw) <= 110 ? relativeYaw : 0;
            var body = Guard.Clamp(y * .45, -bodyLimit, bodyLimit);
            var head = Guard.Clamp(y - body, -headLimit, headLimit);
            var step = speed * Math.Min(dt, .1);
            BodyYaw += Guard.Clamp(body - BodyYaw, -step, step);
            HeadYaw += Guard.Clamp(head - HeadYaw, -step, step);
        }

        public void React(NpcGesture gesture)
        {
            if (!Enum.IsDefined(typeof(NpcGesture), gesture)) throw new ArgumentException("Unknown gesture");
            Gesture = gesture;
        }
    }

    public sealed class ReactionGate
    {
        private readonly double cooldown;
        private double last = double.NegativeInfinity;

        public ReactionGate(double cooldown = 12)
        {
            this.cooldown = Guard.NonNegative(cooldown, "cooldown");
        }

        public bool TryReact(double now, bool mainSpeaking = false, bool visible = true)
        {
            Guard.Finite(now, "now");
            if (mainSpeaking || !visible || now - last < cooldown) return false;
            last = now;
            return true;
        }

        public void Reset() => last = double.NegativeInfinity;
    }
}
