using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using Lidarr.Plugin.DiscogsLists.Api;
using Lidarr.Plugin.DiscogsLists.Auth;
using Lidarr.Plugin.DiscogsLists.Mapping;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.DiscogsLists.ImportLists.Wantlist
{
    public class DiscogsWantlist : DiscogsImportListBase<DiscogsWantlistSettings>
    {
        public DiscogsWantlist(IDiscogsClient client,
                               IDiscogsTokenResolver tokenResolver,
                               IImportListStatusService importListStatusService,
                               IConfigService configService,
                               IParsingService parsingService,
                               Logger logger)
            : base(client, tokenResolver, importListStatusService, configService, parsingService, logger)
        {
        }

        public override string Name => "Discogs Wantlist";

        protected override IEnumerable<ImportListItemInfo> FetchItems(DiscogsSession session)
        {
            return Client.GetWants(session.Token, session.Username).Select(DiscogsItemMapper.Map);
        }

        protected override void TestAccess(DiscogsSession session, List<ValidationFailure> failures)
        {
            // Reading the first entry is enough to prove access.
            Client.GetWants(session.Token, session.Username).FirstOrDefault();
        }
    }
}
