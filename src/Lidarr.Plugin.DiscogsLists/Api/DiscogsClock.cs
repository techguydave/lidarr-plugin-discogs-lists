using System;
using System.Threading;

namespace Lidarr.Plugin.DiscogsLists.Api
{
    public interface IDiscogsClock
    {
        DateTime UtcNow { get; }
        void Sleep(TimeSpan duration);
    }

    public class DiscogsClock : IDiscogsClock
    {
        public DateTime UtcNow => DateTime.UtcNow;

        public void Sleep(TimeSpan duration) => Thread.Sleep(duration);
    }
}
