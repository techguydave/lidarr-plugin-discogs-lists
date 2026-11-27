using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using Lidarr.Plugin.DiscogsLists.Api;
using Lidarr.Plugin.DiscogsLists.Auth;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace Lidarr.Plugin.DiscogsLists.ImportLists
{
    public record DiscogsSession(string Token, string Username);

    public record DiscogsOption(int Value, string Name);

    public abstract class DiscogsImportListBase<TSettings> : ImportListBase<TSettings>
        where TSettings : DiscogsSettingsBase, new()
    {
        private const string MissingToken = "Enter a Discogs personal access token, or save one on another Discogs list.";

        private readonly IDiscogsTokenResolver _tokenResolver;

        protected DiscogsImportListBase(IDiscogsClient client,
                                        IDiscogsTokenResolver tokenResolver,
                                        IImportListStatusService importListStatusService,
                                        IConfigService configService,
                                        IParsingService parsingService,
                                        Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            Client = client;
            _tokenResolver = tokenResolver;
        }

        public override ImportListType ListType => ImportListType.Other;

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public override ProviderMessage Message => new (
            "Data provided by Discogs (https://www.discogs.com). This application uses Discogs' API but is not affiliated with, " +
            "sponsored or endorsed by Discogs. 'Discogs' is a trademark of Zink Media, LLC.",
            ProviderMessageType.Info);

        protected IDiscogsClient Client { get; }

        // Name of the UI action that fills this list's picker, or null if it has none.
        protected virtual string OptionsAction => null;

        public override IList<ImportListItemInfo> Fetch()
        {
            var items = new List<ImportListItemInfo>();

            try
            {
                var session = CreateSession();

                foreach (var item in FetchItems(session))
                {
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }

                _importListStatusService.RecordSuccess(Definition.Id);
            }
            catch (DiscogsRateLimitException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id, ex.RetryAfter);
                _logger.Warn("{0}: {1}", Definition.Name, ex.Message);
            }
            catch (DiscogsApiException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                _logger.Warn("{0}: {1}", Definition.Name, ex.Message);
            }
            catch (Exception ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                _logger.Error(ex, "{0}: failed to import from Discogs", Definition.Name);
            }

            return CleanupListItems(items);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (OptionsAction == null || action != OptionsAction)
            {
                return new { };
            }

            var options = new List<DiscogsOption>();

            try
            {
                if (ResolveToken() != null)
                {
                    options = GetOptions(CreateSession()).ToList();
                }
            }
            catch (DiscogsApiException ex)
            {
                _logger.Warn("Unable to load options from Discogs: {0}", ex.Message);
            }

            return new { options };
        }

        protected abstract IEnumerable<ImportListItemInfo> FetchItems(DiscogsSession session);

        protected virtual IEnumerable<DiscogsOption> GetOptions(DiscogsSession session) => Enumerable.Empty<DiscogsOption>();

        // Checks that the account can read what this list imports.
        protected abstract void TestAccess(DiscogsSession session, List<ValidationFailure> failures);

        // A folder or list deleted on Discogs should not stop the remaining ones from importing.
        protected IEnumerable<T> SkipIfMissing<T>(IEnumerable<T> source, string description)
        {
            using var enumerator = source.GetEnumerator();

            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }
                }
                catch (DiscogsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.Warn("{0}: {1} no longer exists on Discogs and was skipped", Definition.Name, description);
                    yield break;
                }

                yield return enumerator.Current;
            }
        }

        protected static void WarnAboutMissingSelections(IEnumerable<int> selected,
                                                         IEnumerable<int> available,
                                                         string field,
                                                         string description,
                                                         List<ValidationFailure> failures)
        {
            var missing = selected.Except(available).ToList();

            if (missing.Any())
            {
                failures.Add(new NzbDroneValidationFailure(field, $"{description} no longer found on Discogs: {string.Join(", ", missing)}")
                {
                    IsWarning = true
                });
            }
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            if (ResolveToken() == null)
            {
                failures.Add(new ValidationFailure(nameof(DiscogsSettingsBase.ApiKey), MissingToken));
                return;
            }

            try
            {
                TestAccess(CreateSession(), failures);
            }
            catch (DiscogsApiException ex)
            {
                var field = ex.StatusCode == HttpStatusCode.Unauthorized ? nameof(DiscogsSettingsBase.ApiKey) : string.Empty;
                failures.Add(new ValidationFailure(field, ex.Message));
            }
        }

        private string ResolveToken() => _tokenResolver.Resolve(Settings.ApiKey, Definition.Id);

        private DiscogsSession CreateSession()
        {
            var token = ResolveToken() ?? throw new DiscogsApiException(MissingToken, HttpStatusCode.Unauthorized);

            return new DiscogsSession(token, Client.GetUsername(token));
        }
    }
}
