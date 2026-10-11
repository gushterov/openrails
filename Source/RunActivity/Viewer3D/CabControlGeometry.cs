// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using Microsoft.Xna.Framework;

namespace Orts.Viewer3D
{
    public static class CabControlGeometry
    {
        // SpriteBatch rotates the scaled rectangle around its top-left screen position.
        public static Vector2 ToLocal(Vector2 offset, float rotation)
        {
            if (rotation == 0) return offset;
            var sin = (float)Math.Sin(rotation);
            var cos = (float)Math.Cos(rotation);
            return new Vector2(offset.X * cos + offset.Y * sin, -offset.X * sin + offset.Y * cos);
        }

        public static bool Contains(Rectangle rectangle, Vector2 point, float rotation)
        {
            if (rotation == 0) return rectangle.Contains(point.X, point.Y);
            var local = ToLocal(point - new Vector2(rectangle.X, rectangle.Y), rotation);
            return local.X >= 0 && local.X < rectangle.Width && local.Y >= 0 && local.Y < rectangle.Height;
        }

        public static float NormalizeMovement(Vector2 movement, float wheel, Vector2 size,
            bool vertical, bool reverse, float rotation)
        {
            var local = ToLocal(movement, rotation);
            // Wheel input stays on the selected control axis, regardless of screen angle.
            return (vertical ? (local.Y + wheel) / size.Y : (local.X + wheel) / size.X) * (reverse ? -1 : 1);
        }

        // Used by the optional cab control debug overlay. Picking uses Contains above.
        public static Rectangle GetBounds(Rectangle rectangle, float rotation)
        {
            if (rotation == 0) return rectangle;
            var right = ToLocal(new Vector2(rectangle.Width, 0), -rotation);
            var bottom = ToLocal(new Vector2(0, rectangle.Height), -rotation);
            var corner = right + bottom;
            int left = rectangle.X + (int)Math.Floor(Math.Min(0, Math.Min(right.X, Math.Min(bottom.X, corner.X))));
            int top = rectangle.Y + (int)Math.Floor(Math.Min(0, Math.Min(right.Y, Math.Min(bottom.Y, corner.Y))));
            int maxX = rectangle.X + (int)Math.Ceiling(Math.Max(0, Math.Max(right.X, Math.Max(bottom.X, corner.X))));
            int maxY = rectangle.Y + (int)Math.Ceiling(Math.Max(0, Math.Max(right.Y, Math.Max(bottom.Y, corner.Y))));
            return new Rectangle(left, top, maxX - left, maxY - top);
        }
    }
}
