using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Validation;

namespace Lidarr.Plugin.DiscogsLists.ImportLists
{
    public abstract class DiscogsSettingsBase : IImportListSettings
    {
        protected DiscogsSettingsBase()
        {
            BaseUrl = "https://api.discogs.com";
            ApiKey = string.Empty;
        }

        // Required by Lidarr but deliberately not a field: the plugin only ever talks to api.discogs.com.
        public string BaseUrl { get; set; }

        // Named ApiKey because Lidarr's UI reloads the folder and list pickers when a field of this name changes.
        [FieldDefinition(0,
            Label = "Personal Access Token",
            Type = FieldType.Password,
            Privacy = PrivacyLevel.ApiKey,
            HelpText = "Generate one in your Discogs developer settings. Leave blank to use the token saved on another Discogs list.",
            HelpLink = "https://www.discogs.com/settings/developers")]
        public string ApiKey { get; set; }

        // A blank token is valid because it can be shared. The Test button checks that one is available.
        public NzbDroneValidationResult Validate() => new ();
    }
}
