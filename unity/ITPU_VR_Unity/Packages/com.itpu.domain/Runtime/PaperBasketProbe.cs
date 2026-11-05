using System;

namespace ITPU.Domain
{
    public sealed class PaperBasketProbe
    {
        private readonly double dwell;
        private double time;
        private bool entered;
        private bool scored;

        public PaperBasketProbe(double dwell = .15)
        {
            this.dwell = Guard.NonNegative(dwell, "dwell");
        }

        public void Reset()
        {
            entered = false;
            scored = false;
            time = 0;
        }

        public void EnterMouth(bool movingDown, bool trackedRelease)
        {
            entered = movingDown && trackedRelease;
            time = 0;
        }

        public bool Tick(bool inside, double dt, bool paused)
        {
            Guard.NonNegative(dt, "dt");
            if (paused || scored || !entered) return false;
            if (!inside)
            {
                entered = false;
                time = 0;
                return false;
            }
            time += Math.Min(dt, .25);
            if (time + 1e-9 < dwell) return false;
            scored = true;
            return true;
        }
    }
}
