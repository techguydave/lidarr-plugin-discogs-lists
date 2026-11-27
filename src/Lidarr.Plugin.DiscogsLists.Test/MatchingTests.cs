using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.DiscogsLists.Api;
using Lidarr.Plugin.DiscogsLists.Mapping;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class MatchingTests
    {
        private const string BernsteinFifth = "9348eb22-3473-49af-8b0b-8880f7901821";

        private FakeMetadata _metadata;

        [SetUp]
        public void SetUp()
        {
            _metadata = new FakeMetadata();
        }

        [Test]
        public void a_master_linked_on_musicbrainz_is_imported_as_that_release_group()
        {
            // Discogs release 2218836 (Bernstein, 1963) belongs to master 372576, which MusicBrainz links.
            _metadata.Links[DiscogsLinkResolver.MasterUrl(372576)] = new () { new ("release_group", BernsteinFifth) };
            _metadata.HasReleaseGroup(BernsteinFifth, "Symphony No. 5 in C minor, op. 67", "Ludwig van Beethoven");
            _metadata.SearchReturns("Fifth Symphony", FakeMetadata.Candidate("Fifth Symphony / Seventh Symphony", "Ludwig van Beethoven"));

            var item = Match(Release(2218836, 372576, "Ludwig van Beethoven", "Fifth Symphony")).Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo(BernsteinFifth));
            Assert.That(item.Album, Is.EqualTo("Symphony No. 5 in C minor, op. 67"));
        }

        [Test]
        public void a_release_link_leads_to_its_release_group()
        {
            _metadata.Links[DiscogsLinkResolver.ReleaseUrl(5)] = new () { new ("release", "rel-1") };
            _metadata.MusicBrainz.Setup(m => m.GetReleaseGroupOfRelease("rel-1")).Returns("rg-1");
            _metadata.HasReleaseGroup("rg-1", "Linked", "Artist");

            var item = Match(Release(5, 0, "Artist", "Album")).Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo("rg-1"));
        }

        [Test]
        public void ambiguous_links_are_ignored()
        {
            _metadata.Links[DiscogsLinkResolver.MasterUrl(9)] = new () { new ("release_group", "rg-1"), new ("release_group", "rg-2") };

            var item = Match(Release(1, 9, "Artist", "Album")).Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo("album:Album"));
        }

        [Test]
        public void a_link_to_a_release_group_lidarr_lacks_falls_back_to_the_name()
        {
            _metadata.Links[DiscogsLinkResolver.MasterUrl(9)] = new () { new ("release_group", "rg-missing") };
            _metadata.Albums.Setup(s => s.SearchForNewAlbum("lidarr:rg-missing", It.IsAny<string>())).Returns(new List<NzbDrone.Core.Music.Album>());

            var item = Match(Release(1, 9, "Artist", "Album")).Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo("album:Album"));
        }

        [Test]
        public void musicbrainz_being_down_falls_back_to_names()
        {
            _metadata.CreateMatcher();
            _metadata.MusicBrainz.Setup(m => m.GetLinks(It.IsAny<IReadOnlyCollection<string>>())).Throws(new MusicBrainzException("busy"));

            var item = _metadata.CreateMatcherKeepingSetup().Match(new[] { Release(1, 9, "Artist", "Album") }, "List").Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo("album:Album"));
        }

        [Test]
        public void name_search_prefers_the_closest_title_over_the_first_result()
        {
            _metadata.SearchReturns("Timeless",
                FakeMetadata.Candidate("Timeless Classics", "Kaytranada"),
                FakeMetadata.Candidate("TIMELESS", "Kaytranada"));

            var item = Match(Release(1, 0, "Kaytranada", "Timeless")).Single();

            Assert.That(item.Album, Is.EqualTo("TIMELESS"));
        }

        [Test]
        public void name_search_uses_the_year_to_choose_between_equal_titles()
        {
            _metadata.SearchReturns("Live",
                FakeMetadata.Candidate("Live", "Band", 1980),
                FakeMetadata.Candidate("Live", "Band", 2004));

            var item = Match(Release(1, 0, "Band", "Live", 2005)).Single();

            Assert.That(item.AlbumMusicBrainzId, Is.EqualTo("album:Live:2004"));
        }

        [TestCase("Something Else Entirely", "Artist")]
        [TestCase("Album", "Different Person")]
        public void name_search_rejects_poor_matches(string title, string artist)
        {
            _metadata.SearchReturns("Album", FakeMetadata.Candidate(title, artist));

            Assert.That(Match(Release(1, 0, "Artist", "Album")), Is.Empty);
        }

        [TestCase("The The", "The The", 1.0)]
        [TestCase("Sigur Rós", "Sigur Ros", 1.0)]
        [TestCase("\"Awaken, My Love!\"", "Awaken, My Love!", 1.0)]
        [TestCase("The Beatles", "Beatles", 1.0)]
        [TestCase("Fifth Symphony", "Fifth Symphony / Seventh Symphony", 2.0 / 3)]
        [TestCase("Abbey Road", "Let It Be", 0.0)]
        public void titles_are_compared_by_their_words(string left, string right, double expected)
        {
            Assert.That(TitleSimilarity.Score(left, right), Is.EqualTo(expected).Within(0.001));
        }

        [Test]
        public void linked_artists_are_looked_up_by_id()
        {
            _metadata.Links[DiscogsLinkResolver.ArtistUrl(95544)] = new () { new ("artist", "1f9df192-a621-4f54-8850-2c5373b7eac9") };
            var item = new DiscogsItem(new ImportListItemInfo { Artist = "Beethoven" }) { DiscogsArtistId = 95544 };

            var result = _metadata.CreateMatcher().Match(new[] { item }, "List").Single();

            Assert.That(result.ArtistMusicBrainzId, Is.EqualTo("artist:lidarr:1f9df192-a621-4f54-8850-2c5373b7eac9"));
        }

        [Test]
        public void links_are_looked_up_once_in_a_batch_and_cached()
        {
            var matcher = _metadata.CreateMatcher();
            var items = new[] { Release(1, 10, "A", "One"), Release(2, 20, "B", "Two") };

            matcher.Match(items, "List").ToList();
            matcher.Match(new[] { Release(1, 10, "A", "One"), Release(2, 20, "B", "Two") }, "List").ToList();

            // Masters in one request, then releases in another; the second sync uses the cache.
            _metadata.MusicBrainz.Verify(m => m.GetLinks(It.IsAny<IReadOnlyCollection<string>>()), Times.Exactly(2));
        }

        private static DiscogsItem Release(int id, int master, string artist, string title, int year = 0) =>
            new (new ImportListItemInfo { Artist = artist, Album = title }) { ReleaseId = id, MasterId = master, Year = year };

        private List<ImportListItemInfo> Match(params DiscogsItem[] items) => _metadata.CreateMatcher().Match(items, "List").ToList();
    }
}
