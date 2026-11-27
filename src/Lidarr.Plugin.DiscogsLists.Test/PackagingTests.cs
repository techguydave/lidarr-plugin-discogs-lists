using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class PackagingTests
    {
        // Lidarr cannot load a plugin that references a newer Lidarr assembly than the one running.
        [Test]
        public void plugin_references_the_minimum_supported_lidarr_version()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "lidarr.version")))
            {
                directory = directory.Parent;
            }

            var minimum = Version.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "lidarr.version")).Trim());
            var references = typeof(DiscogsListsPlugin).Assembly.GetReferencedAssemblies()
                .Where(a => a.Name!.StartsWith("Lidarr.", StringComparison.Ordinal))
                .ToList();

            Assert.That(references, Is.Not.Empty);
            Assert.That(references.Select(a => a.Version), Is.All.EqualTo(minimum));
        }
    }
}
