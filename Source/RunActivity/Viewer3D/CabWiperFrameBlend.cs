// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Orts.Viewer3D
{
    public readonly struct CabWiperFrameBlend
    {
        public readonly int First;
        public readonly int Second;
        public readonly float Amount;

        CabWiperFrameBlend(int first, int second, float amount)
        {
            First = first;
            Second = second;
            Amount = amount;
        }

        // The CVF's SwitchVal positions may be unevenly spaced or reversed.
        public static CabWiperFrameBlend Select(float position, IReadOnlyList<double> values, int frameCount, bool reversed)
        {
            if (frameCount <= 1) return new CabWiperFrameBlend(0, 0, 0);
            position = MathHelper.Clamp(position, 0, 1);
            int count = Math.Min(values.Count, frameCount);
            int first, second;
            float amount;
            if (count > 1)
            {
                int upper = 0;
                while (upper < count && values[upper] < position) upper++;
                if (upper == 0 || upper == count || values[upper] == position)
                {
                    first = second = Math.Min(upper, count - 1);
                    amount = 0;
                }
                else
                {
                    first = upper - 1;
                    second = upper;
                    amount = (float)((position - values[first]) / (values[second] - values[first]));
                }
            }
            else
            {
                count = frameCount;
                float frame = position * (count - 1);
                first = (int)frame;
                second = Math.Min(first + 1, count - 1);
                amount = frame - first;
            }
            return reversed ? new CabWiperFrameBlend(count - 1 - first, count - 1 - second, amount)
                : new CabWiperFrameBlend(first, second, amount);
        }
    }
}
