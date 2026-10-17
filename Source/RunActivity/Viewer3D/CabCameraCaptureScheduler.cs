// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;

namespace Orts.Viewer3D
{
    /// <summary>Spaces captures across game frames instead of refreshing all monitors together.</summary>
    /// <remarks>Owned by the viewer and used only on the updater thread, across both render buffers.</remarks>
    public sealed class CabCameraCaptureScheduler<T> where T : class
    {
        sealed class Capture
        {
            public double Due;
            public bool Active;
        }

        readonly Func<T, double> UpdateRate;
        readonly Dictionary<T, Capture> Captures = new Dictionary<T, Capture>();
        readonly List<T> Inactive = new List<T>();
        double Time;
        double NextCapture;
        const double Tolerance = 1e-9;

        public CabCameraCaptureScheduler(Func<T, double> updateRate) => UpdateRate = updateRate;

        /// <summary>Returns at most one due feed; hidden feeds are forgotten rather than queued.</summary>
        public T Select(IReadOnlyList<T> visibleFeeds, double elapsedRealSeconds)
        {
            if (double.IsFinite(elapsedRealSeconds) && elapsedRealSeconds > 0) Time += elapsedRealSeconds;
            foreach (var capture in Captures.Values) capture.Active = false;
            double totalRate = 0;
            T selected = null;
            double earliest = double.PositiveInfinity;
            foreach (var feed in visibleFeeds)
            {
                double rate = UpdateRate(feed);
                if (!double.IsFinite(rate) || rate <= 0) continue;
                if (!Captures.TryGetValue(feed, out var capture))
                    Captures.Add(feed, capture = new Capture { Due = Time });
                if (capture.Active) continue;
                capture.Active = true;
                totalRate += rate;
                if (capture.Due < earliest)
                {
                    earliest = capture.Due;
                    selected = feed;
                }
            }
            Inactive.Clear();
            foreach (var capture in Captures)
                if (!capture.Value.Active) Inactive.Add(capture.Key);
            foreach (var feed in Inactive) Captures.Remove(feed);

            if (selected == null)
            {
                NextCapture = Time;
                return null;
            }
            double spacing = 1 / totalRate;
            // Adding feeds should not inherit a long gap from a previously slower display.
            NextCapture = Math.Min(NextCapture, Time + spacing);
            if (Time + Tolerance < NextCapture || Time + Tolerance < earliest) return null;

            var next = Captures[selected];
            double period = 1 / UpdateRate(selected);
            next.Due += period;
            if (next.Due <= Time) next.Due = Time + period;
            NextCapture += spacing;
            if (NextCapture <= Time) NextCapture = Time + spacing;
            return selected;
        }
    }
}
