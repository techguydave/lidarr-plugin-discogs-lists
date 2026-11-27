using System;
using System.Collections.Generic;
using NzbDrone.Core.Annotations;

namespace Lidarr.Plugin.DiscogsLists.ImportLists.UserLists
{
    public class DiscogsUserListsSettings : DiscogsSettingsBase
    {
        public DiscogsUserListsSettings()
        {
            ListIds = Array.Empty<int>();
        }

        [FieldDefinition(1,
            Label = "Lists",
            Type = FieldType.Select,
            SelectOptionsProviderAction = DiscogsUserLists.ListsAction,
            HelpText = "Public and private lists to import. Leave empty to import all of your lists.")]
        public IEnumerable<int> ListIds { get; set; }
    }
}
