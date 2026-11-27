using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lidarr.Plugin.DiscogsLists.Api.Models
{
    public class DiscogsPagination
    {
        public int Page { get; set; }
        public int Pages { get; set; }
    }

    public abstract class DiscogsPage
    {
        public DiscogsPagination Pagination { get; set; }
    }

    public class DiscogsIdentity
    {
        public string Username { get; set; }
    }

    public class DiscogsFolder
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
    }

    public class DiscogsFoldersResponse
    {
        public List<DiscogsFolder> Folders { get; set; }
    }

    public class DiscogsArtist
    {
        public string Name { get; set; }
    }

    public class DiscogsBasicInformation
    {
        public string Title { get; set; }
        public int Year { get; set; }

        [JsonProperty("master_id")]
        public int MasterId { get; set; }

        public List<DiscogsArtist> Artists { get; set; }

        public List<DiscogsFormat> Formats { get; set; }

        public List<DiscogsLabel> Labels { get; set; }
    }

    public class DiscogsFormat
    {
        public string Name { get; set; }

        // Discogs sends the number of discs or tapes as a string.
        [JsonProperty("qty")]
        public string Quantity { get; set; }
    }

    public class DiscogsLabel
    {
        public string Name { get; set; }
    }

    public class DiscogsRelease
    {
        public int Id { get; set; }

        [JsonProperty("basic_information")]
        public DiscogsBasicInformation BasicInformation { get; set; }
    }

    public class DiscogsCollectionPage : DiscogsPage
    {
        public List<DiscogsRelease> Releases { get; set; }
    }

    public class DiscogsWantlistPage : DiscogsPage
    {
        public List<DiscogsRelease> Wants { get; set; }
    }

    public class DiscogsUserList
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Public { get; set; }
    }

    public class DiscogsUserListsPage : DiscogsPage
    {
        public List<DiscogsUserList> Lists { get; set; }
    }

    public class DiscogsListItem
    {
        public int Id { get; set; }
        public string Type { get; set; }

        [JsonProperty("display_title")]
        public string DisplayTitle { get; set; }
    }

    public class DiscogsListPage : DiscogsPage
    {
        public List<DiscogsListItem> Items { get; set; }
    }
}
