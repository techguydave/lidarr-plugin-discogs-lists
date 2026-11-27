using System;
using Lidarr.Plugin.DiscogsLists.Api;
using NUnit.Framework;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class DiscogsThrottleTests
    {
        private FakeClock _clock;
        private DiscogsThrottle _throttle;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeClock();
            _throttle = new DiscogsThrottle(_clock);
        }

        [Test]
        public void never_allows_more_than_the_discogs_limit_in_any_minute()
        {
            var sent = new System.Collections.Generic.List<DateTime>();

            for (var i = 0; i < 200; i++)
            {
                _throttle.WaitForSlot();
                sent.Add(_clock.UtcNow);
                _clock.Advance(TimeSpan.FromMilliseconds(100));
            }

            for (var i = 60; i < sent.Count; i++)
            {
                Assert.That(sent[i] - sent[i - 60], Is.GreaterThan(TimeSpan.FromSeconds(60)));
            }
        }

        [Test]
        public void does_not_wait_while_under_the_limit()
        {
            for (var i = 0; i < DiscogsThrottle.MaxRequestsPerWindow; i++)
            {
                _throttle.WaitForSlot();
            }

            Assert.That(_clock.Sleeps, Is.Empty);
        }

        [Test]
        public void ignores_a_healthy_remaining_budget()
        {
            _throttle.ReportRemaining(DiscogsThrottle.LowBudgetThreshold + 1);
            _throttle.WaitForSlot();

            Assert.That(_clock.Sleeps, Is.Empty);
        }

        [Test]
        public void caps_long_pauses_at_one_window()
        {
            _throttle.Pause(TimeSpan.FromHours(1));
            _throttle.WaitForSlot();

            Assert.That(_clock.Sleeps, Is.EqualTo(new[] { DiscogsThrottle.Window }));
        }
    }
}
