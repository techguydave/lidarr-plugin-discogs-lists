using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Validation;

namespace Lidarr.Plugin.DiscogsLists.ImportLists
{
    // Warns about Lidarr list settings that make a Discogs import do much more than the user expects.
    public class ImportSettingsAdvisor
    {
        internal const string FullCatalogueWarning =
            "Lidarr will add every album by each artist and monitor them all until it finishes refreshing them. " +
            "To add only the albums on your Discogs list, set Metadata Profile to None.";

        private readonly Lazy<IMetadataProfileService> _metadataProfiles;

        // Lazy because Lidarr's metadata profile service depends on the factory that builds import lists.
        public ImportSettingsAdvisor(Lazy<IMetadataProfileService> metadataProfiles)
        {
            _metadataProfiles = metadataProfiles;
        }

        public void Check(ImportListDefinition definition, List<ValidationFailure> failures)
        {
            if (definition?.ShouldMonitor != ImportListMonitorType.SpecificAlbum || IsNoneProfile(definition.MetadataProfileId))
            {
                return;
            }

            failures.Add(new NzbDroneValidationFailure(nameof(ImportListDefinition.MetadataProfileId), FullCatalogueWarning)
            {
                IsWarning = true
            });
        }

        private bool IsNoneProfile(int profileId)
        {
            return _metadataProfiles.Value.Exists(profileId) &&
                _metadataProfiles.Value.Get(profileId).Name == MetadataProfileService.NONE_PROFILE_NAME;
        }
    }
}
