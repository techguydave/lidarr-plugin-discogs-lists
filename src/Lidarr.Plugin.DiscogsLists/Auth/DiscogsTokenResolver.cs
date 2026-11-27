using System.Linq;
using Lidarr.Plugin.DiscogsLists.ImportLists;
using NzbDrone.Core.ImportLists;

namespace Lidarr.Plugin.DiscogsLists.Auth
{
    public interface IDiscogsTokenResolver
    {
        string Resolve(string ownToken, int definitionId);
    }

    // A list with a blank token borrows the one saved on the oldest other Discogs list.
    public class DiscogsTokenResolver : IDiscogsTokenResolver
    {
        private readonly IImportListRepository _importListRepository;

        public DiscogsTokenResolver(IImportListRepository importListRepository)
        {
            _importListRepository = importListRepository;
        }

        public string Resolve(string ownToken, int definitionId)
        {
            if (!string.IsNullOrWhiteSpace(ownToken))
            {
                return ownToken.Trim();
            }

            return _importListRepository.All()
                .Where(d => d.Id != definitionId)
                .OrderBy(d => d.Id)
                .Select(d => (d.Settings as DiscogsSettingsBase)?.ApiKey)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?
                .Trim();
        }
    }
}
