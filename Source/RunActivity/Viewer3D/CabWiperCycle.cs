// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;

namespace Orts.Viewer3D
{
    /// <summary>One out-and-back stroke followed by an optional wait at park.</summary>
    public sealed class CabWiperCycle
    {
        readonly double Cycle;
        readonly double Delay;
        double Phase;
        bool Active;

        public CabWiperCycle(float cycleSeconds, float delaySeconds)
        {
            Cycle = float.IsFinite(cycleSeconds) ? Math.Max(0, cycleSeconds) : 0;
            Delay = float.IsFinite(delaySeconds) ? Math.Max(0, delaySeconds) : 0;
        }

        public bool IsWaiting => Active && Phase >= Cycle;
        public float Position => !Active || Cycle <= 0 || IsWaiting ? 0
            : (float)(2 * Math.Min(Phase, Cycle - Phase) / Cycle);

        // Report only real blade movement to the rain layer. A long frame can cross
        // several strokes and waits; one complete sweep represents repeated full strokes.
        // cycleStarted follows the animation clock, not the duration of a sound sample.
        // Coalesce missed starts after a long frame and do not play an already-finished wipe.
        public void Update(float seconds, bool enabled, Action<float, float> moved = null, Action cycleStarted = null)
        {
            if (Cycle <= 0 || !float.IsFinite(seconds)) return;
            double elapsed = Math.Max(0, seconds);
            if (enabled && elapsed == 0) return;
            bool starting = !Active;
            if (!Active)
            {
                if (!enabled) return;
                Active = true;
                Phase = 0; // First stroke starts immediately after switching on.
            }

            void Move(double phase, double duration)
            {
                if (duration > 0) moved?.Invoke((float)phase, (float)duration);
            }

            if (!enabled)
            {
                if (Phase > 0 && Phase < Cycle)
                {
                    double movement = Math.Min(elapsed, Cycle - Phase);
                    Move(Phase, movement);
                    Phase += movement;
                    if (Phase < Cycle) return; // Finish the current return to park.
                }
                Active = false;
                Phase = 0; // Cancel any remaining wait when switched off.
                return;
            }

            double period = Cycle + Delay;
            double end = Phase + elapsed;
            if (end < period)
            {
                if (Phase < Cycle) Move(Phase, Math.Min(elapsed, Cycle - Phase));
                Phase = end;
                if (starting && Phase < Cycle) cycleStarted?.Invoke();
                return;
            }

            if (Phase < Cycle) Move(Phase, Cycle - Phase);
            if (Math.Floor(end / period) > 1) Move(0, Cycle);
            Phase = end % period;
            Move(0, Math.Min(Phase, Cycle));
            if (Phase < Cycle) cycleStarted?.Invoke();
        }
    }
}
