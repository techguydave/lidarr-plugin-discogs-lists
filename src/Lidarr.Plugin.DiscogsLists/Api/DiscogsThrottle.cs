using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.DiscogsLists.Api
{
    public interface IDiscogsThrottle
    {
        void WaitForSlot();
        void ReportRemaining(int remaining);
        void Pause(TimeSpan duration);
    }

    // Registered as a singleton by Lidarr, so every Discogs list shares one budget.
    // Discogs allows 60 authenticated requests per minute over a moving window.
    public class DiscogsThrottle : IDiscogsThrottle
    {
        internal const int MaxRequestsPerWindow = 55;
        internal static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
        internal static readonly TimeSpan LowBudgetPause = TimeSpan.FromSeconds(10);
        internal const int LowBudgetThreshold = 2;

        private readonly IDiscogsClock _clock;
        private readonly Queue<DateTime> _requests = new ();
        private readonly object _gate = new ();
        private DateTime _pausedUntil = DateTime.MinValue;

        public DiscogsThrottle(IDiscogsClock clock)
        {
            _clock = clock;
        }

        public void WaitForSlot()
        {
            lock (_gate)
            {
                while (true)
                {
                    var now = _clock.UtcNow;

                    while (_requests.Count > 0 && now - _requests.Peek() >= Window)
                    {
                        _requests.Dequeue();
                    }

                    if (_pausedUntil > now)
                    {
                        _clock.Sleep(_pausedUntil - now);
                    }
                    else if (_requests.Count >= MaxRequestsPerWindow)
                    {
                        _clock.Sleep(_requests.Peek() + Window - now);
                    }
                    else
                    {
                        _requests.Enqueue(now);
                        return;
                    }
                }
            }
        }

        // Other tools using the same account or address also consume the budget Discogs reports.
        public void ReportRemaining(int remaining)
        {
            if (remaining <= LowBudgetThreshold)
            {
                Pause(LowBudgetPause);
            }
        }

        public void Pause(TimeSpan duration)
        {
            // Discogs resets the window after 60 idle seconds, so there is no reason to hold a thread longer.
            var capped = duration > Window ? Window : duration;

            lock (_gate)
            {
                var until = _clock.UtcNow + capped;

                if (until > _pausedUntil)
                {
                    _pausedUntil = until;
                }
            }
        }
    }
}
