using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DryIoc;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.ImportLists;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class CompositionTests
    {
        // A dependency cycle stops Lidarr loading plugins, and it then deletes their import lists. Unit tests
        // that construct the lists by hand cannot see one, so this wires them up the way Lidarr does.
        [Test]
        public void lidarr_can_build_its_import_lists_with_the_plugin_loaded()
        {
            var assemblies = new List<Assembly>
            {
                typeof(NzbDrone.Common.Http.IHttpClient).Assembly,
                typeof(IImportListFactory).Assembly,
                typeof(DiscogsListsPlugin).Assembly
            };

            // Services that Lidarr's host and platform assemblies provide are stubbed, so they do not hide a cycle.
            using var container = new Container(rules => rules.WithNzbDroneRules()
                .WithUnknownServiceResolvers(r => r.ServiceType.IsInterface ? new DelegateFactory(_ => null) : null));
            container.AutoAddServices(assemblies)
                .AddNzbDroneLogger()
                .AddDatabase();

            var errors = container.Validate(r => r.ServiceType == typeof(IImportListFactory))
                .Select(e => e.Value.Message)
                .ToList();

            Assert.That(errors, Is.Empty);
        }
    }
}
