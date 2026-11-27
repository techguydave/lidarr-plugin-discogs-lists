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

namespace Lidarr.Plugin.DiscogsLists.ImportLists.Collection
{
    public class DiscogsCollection : DiscogsImportListBase<DiscogsCollectionSettings>
    {
        public const string FoldersAction = "getDiscogsFolders";

        // Folder 0 is Discogs' permanent "All" folder.
        private const int AllFolderId = 0;

        public DiscogsCollection(IDiscogsClient client,
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

        public override string Name => "Discogs Collection";

        protected override string OptionsAction => FoldersAction;

        protected override IEnumerable<ImportListItemInfo> FetchItems(DiscogsSession session)
        {
            var selected = Settings.FolderIds?.Distinct().ToList() ?? new List<int>();

            if (!selected.Any() || selected.Contains(AllFolderId))
            {
                selected = new List<int> { AllFolderId };
            }

            return selected
                .SelectMany(id => SkipIfMissing(Client.GetFolderReleases(session.Token, session.Username, id), $"Folder {id}"))
                .Select(DiscogsItemMapper.Map);
        }

        // "All" is left out of the picker because an empty selection already means the whole collection.
        protected override IEnumerable<DiscogsOption> GetOptions(DiscogsSession session)
        {
            return Client.GetFolders(session.Token, session.Username)
                .Where(f => f.Id != AllFolderId)
                .Select(f => new DiscogsOption(f.Id, $"{f.Name} ({f.Count})"));
        }

        protected override void TestAccess(DiscogsSession session, List<ValidationFailure> failures)
        {
            var available = Client.GetFolders(session.Token, session.Username).Select(f => f.Id);

            WarnAboutMissingSelections(Settings.FolderIds ?? Enumerable.Empty<int>(),
                available.Append(AllFolderId),
                nameof(DiscogsCollectionSettings.FolderIds),
                "Folders",
                failures);
        }
    }
}
