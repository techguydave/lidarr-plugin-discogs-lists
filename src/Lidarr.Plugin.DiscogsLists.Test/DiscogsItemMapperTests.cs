using System.Collections.Generic;
using Lidarr.Plugin.DiscogsLists.Api.Models;
using Lidarr.Plugin.DiscogsLists.Mapping;
using NUnit.Framework;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class DiscogsItemMapperTests
    {
        [TestCase("Nirvana (2)", "Nirvana")]
        [TestCase("Prince*", "Prince")]
        [TestCase("Prince (3)*", "Prince")]
        [TestCase("Foo (2) & Bar (5)", "Foo & Bar")]
        [TestCase("Sunn O)))", "Sunn O)))")]
        [TestCase("M*A*S*H", "M*A*S*H")]
        [TestCase("Level 42", "Level 42")]
        [TestCase("Haiku (1999) Band", "Haiku (1999) Band")]
        public void cleans_discogs_artist_markers(string name, string expected)
        {
            Assert.That(DiscogsItemMapper.CleanArtist(name), Is.EqualTo(expected));
        }

        [Test]
        public void maps_a_release_to_its_first_artist_and_title()
        {
            var item = DiscogsItemMapper.Map(Release("Information Chase", "Bit Shifter (2)", "Someone Else"));

            Assert.That(item.Artist, Is.EqualTo("Bit Shifter"));
            Assert.That(item.Album, Is.EqualTo("Information Chase"));
        }

        [TestCase("Various")]
        [TestCase("Unknown Artist")]
        [TestCase(null)]
        public void skips_releases_without_a_real_artist(string artist)
        {
            Assert.That(DiscogsItemMapper.Map(Release("Title", artist)), Is.Null);
        }

        [Test]
        public void skips_releases_without_details()
        {
            Assert.That(DiscogsItemMapper.Map(new DiscogsRelease { Id = 1 }), Is.Null);
        }

        [TestCase("release")]
        [TestCase("master")]
        public void splits_list_release_titles_at_the_first_separator(string type)
        {
            var item = DiscogsItemMapper.Map(new DiscogsListItem { Type = type, DisplayTitle = "Silent Phase (2) - The Rewired Mixes - Part 1" });

            Assert.That(item.Artist, Is.EqualTo("Silent Phase"));
            Assert.That(item.Album, Is.EqualTo("The Rewired Mixes - Part 1"));
        }

        [Test]
        public void maps_list_artists_to_artist_only_items()
        {
            var item = DiscogsItemMapper.Map(new DiscogsListItem { Type = "artist", DisplayTitle = "Silent Phase" });

            Assert.That(item.Artist, Is.EqualTo("Silent Phase"));
            Assert.That(item.Album, Is.Null);
        }

        [TestCase("label", "Warp Records")]
        [TestCase("release", "Various - Artificial Intelligence II")]
        [TestCase("release", "No separator here")]
        [TestCase("artist", "Various")]
        [TestCase("release", null)]
        public void skips_unusable_list_items(string type, string title)
        {
            Assert.That(DiscogsItemMapper.Map(new DiscogsListItem { Type = type, DisplayTitle = title }), Is.Null);
        }

        private static DiscogsRelease Release(string title, params string[] artists)
        {
            var list = new List<DiscogsArtist>();

            foreach (var artist in artists ?? new string[] { null })
            {
                list.Add(new DiscogsArtist { Name = artist });
            }

            return new DiscogsRelease { BasicInformation = new DiscogsBasicInformation { Title = title, Artists = list } };
        }
    }
}
