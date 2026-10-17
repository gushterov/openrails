// COPYRIGHT 2026 by the Open Rails project. GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Linq;
using Orts.Viewer3D;
using Xunit;

namespace Tests.RunActivity
{
    public class CabCameraCaptureSchedulerTests
    {
        sealed class Feed
        {
            public readonly double Rate;
            public Feed(double rate) => Rate = rate;
        }

        static CabCameraCaptureScheduler<Feed> Scheduler() => new CabCameraCaptureScheduler<Feed>(feed => feed.Rate);

        [Theory]
        [InlineData(144)]
        [InlineData(200)]
        [InlineData(240)]
        public void FourEightFpsFeedsAreSpreadAcrossFramesAndKeepTheirRefreshRate(int gameFps)
        {
            var feeds = Enumerable.Range(0, 4).Select(_ => new Feed(8)).ToArray();
            var counts = feeds.ToDictionary(feed => feed, _ => 0);
            var scheduler = Scheduler();
            var captureTimes = new List<double>();
            for (int frame = 0; frame < gameFps * 10; frame++)
            {
                var selected = scheduler.Select(feeds, 1.0 / gameFps);
                if (selected == null) continue;
                counts[selected]++;
                captureTimes.Add((frame + 1.0) / gameFps);
            }
            foreach (var count in counts.Values) Assert.InRange(count, 79, 81);
            // Camera work is distributed across the 125 ms cycle, rather than one four-feed burst.
            for (int i = 1; i < captureTimes.Count; i++)
                Assert.InRange(captureTimes[i] - captureTimes[i - 1], .02, .045);
        }

        [Fact]
        public void DifferentUpdateRatesDoNotStarveSlowerFeeds()
        {
            var feeds = new[] { new Feed(8), new Feed(4) };
            var scheduler = Scheduler();
            var counts = new int[2];
            for (int frame = 0; frame < 2000; frame++)
            {
                var selected = scheduler.Select(feeds, .005);
                if (selected != null) counts[Array.IndexOf(feeds, selected)]++;
            }
            Assert.InRange(counts[0], 79, 81);
            Assert.InRange(counts[1], 39, 41);
        }

        [Fact]
        public void LongStallDoesNotCauseCatchUpBurst()
        {
            var feeds = Enumerable.Range(0, 4).Select(_ => new Feed(8)).ToArray();
            var scheduler = Scheduler();
            for (int frame = 0; frame < 200; frame++) scheduler.Select(feeds, .005);
            Assert.NotNull(scheduler.Select(feeds, 5));
            for (int frame = 0; frame < 25; frame++) Assert.Null(scheduler.Select(feeds, .001));
            Assert.NotNull(scheduler.Select(feeds, .01));
        }

        [Fact]
        public void HiddenFeedsAreForgottenAndCanBeReopenedImmediately()
        {
            var feed = new Feed(8);
            var scheduler = Scheduler();
            Assert.Same(feed, scheduler.Select(new[] { feed }, 0));
            Assert.Null(scheduler.Select(Array.Empty<Feed>(), .01));
            Assert.Same(feed, scheduler.Select(new[] { feed }, 0));
            var other = new Feed(8);
            Assert.Null(scheduler.Select(Array.Empty<Feed>(), .01));
            Assert.Same(other, scheduler.Select(new[] { other }, 0));
        }

        [Fact]
        public void InsufficientGameFpsReducesCameraRateFairly()
        {
            var feeds = Enumerable.Range(0, 4).Select(_ => new Feed(8)).ToArray();
            var counts = feeds.ToDictionary(feed => feed, _ => 0);
            var scheduler = Scheduler();
            for (int frame = 0; frame < 100; frame++)
            {
                var selected = scheduler.Select(feeds, .1);
                Assert.NotNull(selected);
                counts[selected]++;
            }
            foreach (var count in counts.Values) Assert.Equal(25, count);
        }

        [Fact]
        public void DuplicateRequestsDoNotIncreaseCaptureFrequency()
        {
            var feed = new Feed(8);
            var scheduler = Scheduler();
            int captures = 0;
            for (int frame = 0; frame < 200; frame++)
                if (scheduler.Select(new[] { feed, feed }, .005) != null) captures++;
            Assert.Equal(8, captures);
        }
    }
}
