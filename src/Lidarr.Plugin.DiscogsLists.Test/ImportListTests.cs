using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Lidarr.Plugin.DiscogsLists.Auth;
using Lidarr.Plugin.DiscogsLists.ImportLists;
using Lidarr.Plugin.DiscogsLists.ImportLists.Collection;
using Lidarr.Plugin.DiscogsLists.ImportLists.UserLists;
using Lidarr.Plugin.DiscogsLists.ImportLists.Wantlist;
using Lidarr.Plugin.DiscogsLists.Mapping;
using Moq;
using Newtonsoft.Json.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Lidarr;
using NzbDrone.Core.Validation;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class ImportListTests
    {
        private FakeDiscogs _discogs;
        private Mock<IImportListStatusService> _status;
        private List<ImportListDefinition> _saved;
        private Mock<IImportListRepository> _repository;
        private FakeMetadata _metadata;

        [SetUp]
        public void SetUp()
        {
            _discogs = new FakeDiscogs().WithIdentity();
            _metadata = new FakeMetadata();
            _status = new Mock<IImportListStatusService>();
            _saved = new List<ImportListDefinition>();
            _repository = new Mock<IImportListRepository>();
            _repository.Setup(r => r.All()).Returns(() => _saved);
        }

        [Test]
        public void collection_without_a_selection_imports_the_all_folder()
        {
            Releases(0, "Album A");

            var items = Collection(new DiscogsCollectionSettings { ApiKey = "token" }).Fetch();

            Assert.That(items.Single().Album, Is.EqualTo("Album A"));
            Assert.That(items.Single().ImportListId, Is.EqualTo(7));
            Assert.That(items.Single().ImportList, Is.EqualTo("My list"));
            _status.Verify(s => s.RecordSuccess(7));
        }

        [Test]
        public void collection_imports_only_selected_folders_and_removes_duplicates()
        {
            Releases(3, "Album A");
            Releases(4, "Album A", "Album B");

            var items = Collection(new DiscogsCollectionSettings { ApiKey = "token", FolderIds = new[] { 3, 4 } }).Fetch();

            Assert.That(items.Select(i => i.Album), Is.EqualTo(new[] { "Album A", "Album B" }));
            Assert.That(_discogs.Requests.Select(r => r.Uri.AbsolutePath), Has.None.EqualTo("/users/rodneyfool/collection/folders/0/releases"));
        }

        [Test]
        public void a_deleted_folder_does_not_stop_the_others()
        {
            Releases(4, "Album B");

            var items = Collection(new DiscogsCollectionSettings { ApiKey = "token", FolderIds = new[] { 99, 4 } }).Fetch();

            Assert.That(items.Single().Album, Is.EqualTo("Album B"));
            _status.Verify(s => s.RecordSuccess(7));
        }

        [Test]
        public void wantlist_imports_wanted_releases()
        {
            _discogs.Returns("/users/rodneyfool/wants?page=1&per_page=100",
                @"{""wants"": [{""id"": 1, ""basic_information"": {""title"": ""Wanted"", ""artists"": [{""name"": ""Artist""}]}}]}");

            var items = Wantlist(new DiscogsWantlistSettings { ApiKey = "token" }).Fetch();

            Assert.That(items.Single().Album, Is.EqualTo("Wanted"));
        }

        [Test]
        public void user_lists_without_a_selection_import_every_list_including_private_ones()
        {
            Lists();
            ListItems(1, "A - One");
            ListItems(2, "B - Two");

            var items = UserLists(new DiscogsUserListsSettings { ApiKey = "token" }).Fetch();

            Assert.That(items.Select(i => i.Album), Is.EqualTo(new[] { "One", "Two" }));
        }

        [Test]
        public void user_lists_import_only_the_selection()
        {
            ListItems(2, "B - Two");

            var items = UserLists(new DiscogsUserListsSettings { ApiKey = "token", ListIds = new[] { 2 } }).Fetch();

            Assert.That(items.Single().Album, Is.EqualTo("Two"));
            Assert.That(_discogs.Requests.Select(r => r.Uri.AbsolutePath), Is.EqualTo(new[] { "/oauth/identity", "/lists/2" }));
        }

        [Test]
        public void a_failing_search_skips_only_that_item()
        {
            Releases(0, "Good", "Sunshine", "Also Good");
            _metadata.Fails("Sunshine");

            var items = Collection(new DiscogsCollectionSettings { ApiKey = "token" }).Fetch();

            Assert.That(items.Select(i => i.Album), Is.EqualTo(new[] { "Good", "Also Good" }));
            _status.Verify(s => s.RecordSuccess(7));
        }

        [Test]
        public void imported_items_carry_ids_so_lidarr_does_not_search_again()
        {
            Releases(0, "Album A");

            var item = Collection(new DiscogsCollectionSettings { ApiKey = "token" }).Fetch().Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo("album:Album A"));
            Assert.That(item.ArtistMusicBrainzId, Is.EqualTo("artist:Artist"));
        }

        [Test]
        public void unmatched_items_are_left_out()
        {
            Releases(0, "Obscure", "Known");
            _metadata.HasNoMatch("Obscure");

            var items = Collection(new DiscogsCollectionSettings { ApiKey = "token" }).Fetch();

            Assert.That(items.Single().Album, Is.EqualTo("Known"));
        }

        [Test]
        public void each_release_is_searched_once()
        {
            Releases(3, "Album A");
            Releases(4, "Album A");

            Collection(new DiscogsCollectionSettings { ApiKey = "token", FolderIds = new[] { 3, 4 } }).Fetch();

            _metadata.Albums.Verify(s => s.SearchForNewAlbum("Album A", "Artist"), Times.Once());
        }

        [Test]
        public void list_artists_are_matched_to_ids()
        {
            _discogs.Returns("/lists/2?page=1&per_page=100", @"{""items"": [{""id"": 1, ""type"": ""artist"", ""display_title"": ""Someone""}]}");

            var item = UserLists(new DiscogsUserListsSettings { ApiKey = "token", ListIds = new[] { 2 } }).Fetch().Single();

            Assert.That(item.ArtistMusicBrainzId, Is.EqualTo("artist:Someone"));
            Assert.That(item.Album, Is.Null);
        }

        [Test]
        public void stops_when_the_metadata_service_keeps_failing()
        {
            var titles = Enumerable.Range(0, MusicBrainzMatcher.MaxConsecutiveFailures + 5).Select(i => $"Album {i}").ToArray();
            Releases(0, titles);
            _metadata.Fails(titles);

            var items = Collection(new DiscogsCollectionSettings { ApiKey = "token" }).Fetch();

            Assert.That(items, Is.Empty);
            _metadata.Albums.Verify(s => s.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(MusicBrainzMatcher.MaxConsecutiveFailures));
            _status.Verify(s => s.RecordFailure(7, default));
            _status.Verify(s => s.RecordSuccess(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void rate_limit_records_a_back_off()
        {
            _discogs.Returns("/users/rodneyfool/wants?page=1&per_page=100", "{}", HttpStatusCode.TooManyRequests);

            var items = Wantlist(new DiscogsWantlistSettings { ApiKey = "token" }).Fetch();

            Assert.That(items, Is.Empty);
            _status.Verify(s => s.RecordFailure(7, TimeSpan.FromSeconds(60)));
            _status.Verify(s => s.RecordSuccess(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void a_blank_token_is_borrowed_from_the_oldest_other_discogs_list()
        {
            _saved.Add(Definition(7, new DiscogsWantlistSettings()));
            _saved.Add(Definition(1, new LidarrSettings { ApiKey = "not-a-discogs-token" }));
            _saved.Add(Definition(9, new DiscogsCollectionSettings { ApiKey = "newer" }));
            _saved.Add(Definition(8, new DiscogsUserListsSettings { ApiKey = "shared" }));
            _discogs.Returns("/users/rodneyfool/wants?page=1&per_page=100", @"{""wants"": []}");

            Wantlist(new DiscogsWantlistSettings()).Fetch();

            Assert.That(_discogs.Requests.Select(r => r.Authorization).Distinct(), Is.EqualTo(new[] { "Discogs token=shared" }));
        }

        [Test]
        public void own_token_wins_over_a_shared_one()
        {
            _saved.Add(Definition(1, new DiscogsCollectionSettings { ApiKey = "shared" }));

            Assert.That(new DiscogsTokenResolver(_repository.Object).Resolve(" own ", 7), Is.EqualTo("own"));
        }

        [Test]
        public void test_asks_for_a_token_when_none_is_available()
        {
            var result = Collection(new DiscogsCollectionSettings()).Test();

            Assert.That(result.Errors.Single().PropertyName, Is.EqualTo("ApiKey"));
            Assert.That(_discogs.Requests, Is.Empty);
        }

        [Test]
        public void test_flags_a_rejected_token()
        {
            _discogs = new FakeDiscogs().Returns("/oauth/identity", "{}", HttpStatusCode.Unauthorized);

            var result = Wantlist(new DiscogsWantlistSettings { ApiKey = "bad" }).Test();

            Assert.That(result.Errors.Single().PropertyName, Is.EqualTo("ApiKey"));
        }

        [Test]
        public void test_warns_about_selections_that_no_longer_exist()
        {
            Lists();

            var result = UserLists(new DiscogsUserListsSettings { ApiKey = "token", ListIds = new[] { 1, 55 } }).Test();

            var warning = (NzbDroneValidationFailure)result.Errors.Single();
            Assert.That(warning.IsWarning, Is.True);
            Assert.That(warning.ErrorMessage, Does.Contain("55"));
        }

        [Test]
        public void folder_picker_lists_folders_without_the_all_folder()
        {
            _discogs.Returns("/users/rodneyfool/collection/folders",
                @"{""folders"": [{""id"": 0, ""name"": ""All"", ""count"": 5}, {""id"": 1, ""name"": ""Uncategorized"", ""count"": 2}]}");

            var result = JObject.FromObject(Collection(new DiscogsCollectionSettings { ApiKey = "token" }).RequestAction("getDiscogsFolders", null));

            Assert.That(result["options"].Single()["Value"].Value<int>(), Is.EqualTo(1));
            Assert.That(result["options"].Single()["Name"].Value<string>(), Is.EqualTo("Uncategorized (2)"));
        }

        [Test]
        public void list_picker_marks_private_lists()
        {
            Lists();

            var result = JObject.FromObject(UserLists(new DiscogsUserListsSettings { ApiKey = "token" }).RequestAction("getDiscogsLists", null));

            Assert.That(result["options"].Select(o => o["Name"].Value<string>()), Is.EqualTo(new[] { "Public one", "Secret one (private)" }));
        }

        [Test]
        public void pickers_are_empty_without_a_token()
        {
            var result = JObject.FromObject(UserLists(new DiscogsUserListsSettings()).RequestAction("getDiscogsLists", null));

            Assert.That(result["options"], Is.Empty);
            Assert.That(_discogs.Requests, Is.Empty);
        }

        [TestCase(typeof(DiscogsCollectionSettings))]
        [TestCase(typeof(DiscogsWantlistSettings))]
        [TestCase(typeof(DiscogsUserListsSettings))]
        public void the_token_field_is_masked(Type settings)
        {
            var field = settings.GetProperty("ApiKey").GetCustomAttribute<FieldDefinitionAttribute>();

            Assert.That(field.Privacy, Is.EqualTo(PrivacyLevel.ApiKey));
            Assert.That(field.Type, Is.EqualTo(FieldType.Password));
        }

        [TestCase("", "https://github.com/techguydave/lidarr-plugin-discogs-lists")]
        [TestCase("develop", "https://github.com/techguydave/lidarr-plugin-discogs-lists/tree/develop")]
        public void plugin_checks_updates_on_its_own_channel(string branch, string expected)
        {
            Assert.That(DiscogsListsPlugin.BuildGithubUrl(branch), Is.EqualTo(expected));
        }

        private static ImportListDefinition Definition(int id, IImportListSettings settings) =>
            new () { Id = id, Name = "My list", Settings = settings };

        private DiscogsCollection Collection(DiscogsCollectionSettings settings) =>
            Create(settings, resolver => new DiscogsCollection(_discogs.CreateClient(), resolver, _metadata.CreateMatcher(), _status.Object, null, null, LogManager.CreateNullLogger()));

        private DiscogsWantlist Wantlist(DiscogsWantlistSettings settings) =>
            Create(settings, resolver => new DiscogsWantlist(_discogs.CreateClient(), resolver, _metadata.CreateMatcher(), _status.Object, null, null, LogManager.CreateNullLogger()));

        private DiscogsUserLists UserLists(DiscogsUserListsSettings settings) =>
            Create(settings, resolver => new DiscogsUserLists(_discogs.CreateClient(), resolver, _metadata.CreateMatcher(), _status.Object, null, null, LogManager.CreateNullLogger()));

        private TList Create<TList>(DiscogsSettingsBase settings, Func<IDiscogsTokenResolver, TList> factory)
            where TList : IImportList
        {
            var list = factory(new DiscogsTokenResolver(_repository.Object));
            list.Definition = Definition(7, settings);

            return list;
        }

        private void Releases(int folderId, params string[] titles)
        {
            var releases = string.Join(",", titles.Select((t, i) => $@"{{""id"": {i}, ""basic_information"": {{""title"": ""{t}"", ""artists"": [{{""name"": ""Artist""}}]}}}}"));

            _discogs.Returns($"/users/rodneyfool/collection/folders/{folderId}/releases?page=1&per_page=100", $@"{{""releases"": [{releases}]}}");
        }

        private void Lists()
        {
            _discogs.Returns("/users/rodneyfool/lists?page=1&per_page=100",
                @"{""lists"": [{""id"": 1, ""name"": ""Public one"", ""public"": true}, {""id"": 2, ""name"": ""Secret one"", ""public"": false}]}");
        }

        private void ListItems(int listId, string title)
        {
            _discogs.Returns($"/lists/{listId}?page=1&per_page=100", $@"{{""items"": [{{""id"": 1, ""type"": ""release"", ""display_title"": ""{title}""}}]}}");
        }
    }
}
