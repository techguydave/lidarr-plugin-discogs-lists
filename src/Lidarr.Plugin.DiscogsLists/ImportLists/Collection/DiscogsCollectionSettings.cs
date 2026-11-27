using System;
using System.Collections.Generic;
using NzbDrone.Core.Annotations;

namespace Lidarr.Plugin.DiscogsLists.ImportLists.Collection
{
    public class DiscogsCollectionSettings : DiscogsSettingsBase
    {
        public DiscogsCollectionSettings()
        {
            FolderIds = Array.Empty<int>();
        }

        [FieldDefinition(1,
            Label = "Folders",
            Type = FieldType.Select,
            SelectOptionsProviderAction = DiscogsCollection.FoldersAction,
            HelpText = "Collection folders to import. Leave empty to import the whole collection.")]
        public IEnumerable<int> FolderIds { get; set; }
    }
}
