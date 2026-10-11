// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Microsoft.Xna.Framework;

namespace Orts.Viewer3D
{
    public static class CabGaugeGeometry
    {
        // All dimensions are in screen pixels, as used by SpriteBatch rotation.
        // Reverse fills keep their bottom/left or top/right corner at the zero end.
        public static Vector2 GetFillPosition(Vector2 position, Vector2 fullSize, Vector2 filledSize,
            bool vertical, bool reverse, float rotation)
        {
            if (!reverse)
                return position;

            var anchor = position + (vertical ? new Vector2(0, fullSize.Y) : new Vector2(fullSize.X, 0));
            var length = vertical ? filledSize.Y : filledSize.X;
            var sin = (float)Math.Sin(rotation);
            var cos = (float)Math.Cos(rotation);
            var offset = vertical ? new Vector2(-length * sin, length * cos) : new Vector2(length * cos, length * sin);
            return anchor - offset;
        }
    }
}
