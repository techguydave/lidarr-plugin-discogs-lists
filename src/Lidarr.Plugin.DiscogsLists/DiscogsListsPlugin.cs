using System.Linq;
using System.Reflection;

namespace Lidarr.Plugin.DiscogsLists
{
    public class DiscogsListsPlugin : NzbDrone.Core.Plugins.Plugin
    {
        public const string RepositoryUrl = "https://github.com/techguydave/lidarr-plugin-discogs-lists";

        private static readonly string Branch = typeof(DiscogsListsPlugin).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "PluginBranch")?.Value;

        public override string Name => "Lidarr Lists for Discogs";

        public override string Owner => "techguydave";

        public override string GithubUrl => BuildGithubUrl(Branch);

        // Lidarr checks this URL for updates, so pre-release builds must point at their own branch.
        internal static string BuildGithubUrl(string branch) =>
            string.IsNullOrWhiteSpace(branch) ? RepositoryUrl : $"{RepositoryUrl}/tree/{branch}";
    }
}
