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

namespace Lidarr.Plugin.DiscogsLists.ImportLists.UserLists
{
    public class DiscogsUserLists : DiscogsImportListBase<DiscogsUserListsSettings>
    {
        public const string ListsAction = "getDiscogsLists";

        public DiscogsUserLists(IDiscogsClient client,
                                IDiscogsTokenResolver tokenResolver,
                                MusicBrainzMatcher matcher,
                                ImportSettingsAdvisor advisor,
                                IImportListStatusService importListStatusService,
                                IConfigService configService,
                                IParsingService parsingService,
                                Logger logger)
            : base(client, tokenResolver, matcher, advisor, importListStatusService, configService, parsingService, logger)
        {
        }

        public override string Name => "Discogs User Lists";

        protected override string OptionsAction => ListsAction;

        protected override IEnumerable<DiscogsItem> FetchItems(DiscogsSession session)
        {
            var selected = Settings.ListIds?.Distinct().ToList() ?? new List<int>();

            if (!selected.Any())
            {
                selected = Client.GetUserLists(session.Token, session.Username).Select(l => l.Id).ToList();
            }

            return selected
                .SelectMany(id => SkipIfMissing(Client.GetListItems(session.Token, id), $"List {id}"))
                .Select(DiscogsItemMapper.Map);
        }

        protected override IEnumerable<DiscogsOption> GetOptions(DiscogsSession session)
        {
            return Client.GetUserLists(session.Token, session.Username)
                .Select(l => new DiscogsOption(l.Id, l.Public ? l.Name : $"{l.Name} (private)"));
        }

        protected override void TestAccess(DiscogsSession session, List<ValidationFailure> failures)
        {
            var available = Client.GetUserLists(session.Token, session.Username).Select(l => l.Id).ToList();

            WarnAboutMissingSelections(Settings.ListIds ?? Enumerable.Empty<int>(),
                available,
                nameof(DiscogsUserListsSettings.ListIds),
                "Lists",
                failures);
        }
    }
}
