using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.DiscogsLists.Api.Models;
using Lidarr.Plugin.DiscogsLists.Editions;
using Lidarr.Plugin.DiscogsLists.Mapping;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class EditionTests
    {
        private static readonly string[] Vinyl = { "vinyl" };

        [TestCase("12\" Vinyl", "vinyl")]
        [TestCase("Vinyl", "vinyl")]
        [TestCase("CD", "cd")]
        [TestCase("CDr", "cd")]
        [TestCase("Enhanced CD", "cd")]
        [TestCase("Hybrid SACD", "sacd")]
        [TestCase("File", "digital")]
        [TestCase("Digital Media", "digital")]
        [TestCase("Cassette", "cassette")]
        [TestCase("Blu-ray", "blu-ray")]
        public void formats_from_both_sources_reduce_to_one_family(string format, string family)
        {
            Assert.That(OwnedEdition.FormatFamily(format), Is.EqualTo(family));
        }

        [Test]
        public void a_musicbrainz_link_picks_that_exact_release()
        {
            var linked = Release("linked", "CD", 1, 2001, tracks: 10);
            var edition = new OwnedEdition("linked", Vinyl, 2, Array.Empty<string>(), 1963);

            Assert.That(edition.Choose(new[] { Release("deluxe", "CD", 2, 2010, tracks: 30, monitored: true), linked }), Is.SameAs(linked));
        }

        [Test]
        public void the_closest_edition_of_the_same_format_is_chosen()
        {
            var deluxe = Release("deluxe", "CD", 2, 2010, tracks: 30, monitored: true);
            var reissue = Release("reissue", "12\" Vinyl", 1, 2015);
            var original = Release("original", "12\" Vinyl", 1, 1963, "Columbia Masterworks");

            var edition = new OwnedEdition(null, Vinyl, 1, new[] { "columbiamasterworks" }, 1963);

            Assert.That(edition.Choose(new[] { deluxe, reissue, original }), Is.SameAs(original));
        }

        [Test]
        public void no_edition_of_the_same_format_keeps_lidarrs_choice()
        {
            var edition = new OwnedEdition(null, new[] { "cassette" }, 1, Array.Empty<string>(), 1990);

            Assert.That(edition.Choose(new[] { Release("cd", "CD", 1, 1990, monitored: true) }), Is.Null);
        }

        [Test]
        public void lidarrs_choice_stands_between_equally_close_editions()
        {
            var first = Release("a", "CD", 1, 2000, tracks: 12);
            var monitored = Release("b", "CD", 1, 2000, tracks: 10, monitored: true);

            var edition = new OwnedEdition(null, new[] { "cd" }, 1, Array.Empty<string>(), 2000);

            Assert.That(edition.Choose(new[] { first, monitored }), Is.SameAs(monitored));
        }

        [Test]
        public void a_mixed_set_needs_every_format_to_match()
        {
            var cdOnly = Release("cd", "CD", 2, 2005);
            var both = Release("both", "CD", 1, 2005);
            both.Media.Add(new Medium { Number = 2, Format = "DVD" });

            var edition = new OwnedEdition(null, new[] { "cd", "dvd" }, 2, Array.Empty<string>(), 2005);

            Assert.That(edition.Choose(new[] { cdOnly, both }), Is.SameAs(both));
        }

        [Test]
        public void the_mapper_reads_format_discs_and_labels_from_discogs()
        {
            var item = DiscogsItemMapper.Map(new DiscogsRelease
            {
                Id = 2218836,
                BasicInformation = new DiscogsBasicInformation
                {
                    Title = "Fifth Symphony",
                    Year = 1963,
                    Artists = new () { new () { Name = "Beethoven*" } },
                    Formats = new () { new () { Name = "Box Set", Quantity = "1" }, new () { Name = "Vinyl", Quantity = "2" } },
                    Labels = new () { new () { Name = "Columbia Masterworks (2)" } }
                }
            });

            Assert.That(item.Formats, Is.EqualTo(Vinyl));
            Assert.That(item.MediumCount, Is.EqualTo(2));
            Assert.That(item.Labels, Is.EqualTo(new[] { "columbiamasterworks" }));
        }

        [Test]
        public void a_release_link_records_the_edition_even_when_the_master_is_linked()
        {
            var metadata = new FakeMetadata();
            metadata.Links[DiscogsLinkResolver.MasterUrl(372576)] = new () { new ("release_group", "rg") };
            metadata.Links[DiscogsLinkResolver.ReleaseUrl(2218836)] = new () { new ("release", "rel") };
            metadata.HasReleaseGroup("rg", "Symphony No. 5", "Ludwig van Beethoven");

            var item = new DiscogsItem(new ImportListItemInfo { Artist = "Ludwig van Beethoven", Album = "Fifth Symphony" })
            {
                ReleaseId = 2218836,
                MasterId = 372576
            };

            metadata.CreateMatcher().Match(new[] { item }, "List").ToList();

            metadata.Editions.Verify(e => e.Remember("rg", It.Is<OwnedEdition>(o => o.MusicBrainzReleaseId == "rel")));

            // The master already gave the release group, so no extra request is made for the release's group.
            metadata.MusicBrainz.Verify(m => m.GetReleaseGroupOfRelease(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void a_new_album_is_switched_to_the_owned_edition_once()
        {
            var pinner = new PinnerFixture();
            var deluxe = Release("deluxe", "CD", 2, 2010, tracks: 30, monitored: true);
            var vinyl = Release("vinyl", "12\" Vinyl", 1, 1963);
            pinner.Releases.Setup(r => r.GetReleasesByAlbum(7)).Returns(new List<AlbumRelease> { deluxe, vinyl });

            pinner.Subject.Remember("rg", new OwnedEdition(null, Vinyl, 1, Array.Empty<string>(), 1963));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg", Title = "Fifth" }));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg", Title = "Fifth" }));

            pinner.Releases.Verify(r => r.SetMonitored(vinyl), Times.Once());
        }

        [Test]
        public void albums_already_in_the_library_are_left_alone()
        {
            var pinner = new PinnerFixture();
            pinner.Albums.Setup(a => a.FindById("rg")).Returns(new Album { Id = 7 });
            pinner.Releases.Setup(r => r.GetReleasesByAlbum(7)).Returns(new List<AlbumRelease> { Release("vinyl", "Vinyl", 1, 1963) });

            pinner.Subject.Remember("rg", new OwnedEdition(null, Vinyl, 1, Array.Empty<string>(), 1963));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg" }));

            pinner.Releases.Verify(r => r.SetMonitored(It.IsAny<AlbumRelease>()), Times.Never());
        }

        [Test]
        public void albums_with_files_are_left_alone()
        {
            var pinner = new PinnerFixture();
            pinner.Releases.Setup(r => r.GetReleasesByAlbum(7)).Returns(new List<AlbumRelease> { Release("cd", "CD", 1, 2010, monitored: true), Release("vinyl", "Vinyl", 1, 1963) });
            pinner.Files.Setup(f => f.GetFilesByAlbum(7)).Returns(new List<TrackFile> { new () });

            pinner.Subject.Remember("rg", new OwnedEdition(null, Vinyl, 1, Array.Empty<string>(), 1963));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg" }));

            pinner.Releases.Verify(r => r.SetMonitored(It.IsAny<AlbumRelease>()), Times.Never());
        }

        [Test]
        public void the_edition_waits_until_lidarr_has_saved_the_releases()
        {
            var pinner = new PinnerFixture();
            var vinyl = Release("vinyl", "Vinyl", 1, 1963);
            pinner.Releases.SetupSequence(r => r.GetReleasesByAlbum(7))
                .Returns(new List<AlbumRelease>())
                .Returns(new List<AlbumRelease> { Release("cd", "CD", 1, 2010, monitored: true), vinyl });

            pinner.Subject.Remember("rg", new OwnedEdition(null, Vinyl, 1, Array.Empty<string>(), 1963));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg" }));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg" }));

            pinner.Releases.Verify(r => r.SetMonitored(vinyl), Times.Once());
        }

        [TestCase("{Album Title}/{Medium Format} {medium:00}/{track:00} - {Track Title}", "CD", true)]
        [TestCase("{Album Title}/{Medium Format} {medium:00}/{track:00} - {Track Title}", "Hybrid SACD", true)]
        [TestCase("{Album Title}/{Medium Format} {medium:00}/{track:00} - {Track Title}", "12\" Vinyl", false)]
        [TestCase("{Album Title}/{Medium Format} {medium:00}/{track:00} - {Track Title}", "Digital Media", false)]
        [TestCase("{Album Title}/Disc {medium:00}/{track:00} - {Track Title}", "12\" Vinyl", true)]
        [TestCase("{Album Title}/{medium:00} {Medium Format}/{track:00} - {Track Title}", "CD", false)]
        [TestCase("{Album Title}/{medium:0}{track:00} - {Track Title}", "12\" Vinyl", true)]
        public void lidarr_reads_back_only_cd_and_disc_folders(string format, string medium, bool readable)
        {
            Assert.That(MultiDiscFolders.CanBeReadBack(Release("r", medium, 2, 2000), format), Is.EqualTo(readable));
        }

        [Test]
        public void a_single_disc_is_always_readable()
        {
            Assert.That(MultiDiscFolders.CanBeReadBack(Release("r", "12\" Vinyl", 1, 2000), NamingConfig.Default.MultiDiscTrackFormat), Is.True);
        }

        [Test]
        public void discs_of_different_formats_are_not_readable_when_named_by_format()
        {
            var release = Release("r", "CD", 1, 2000);
            release.Media.Add(new Medium { Number = 2, Format = "Enhanced CD" });

            Assert.That(MultiDiscFolders.CanBeReadBack(release, NamingConfig.Default.MultiDiscTrackFormat), Is.False);
        }

        [Test]
        public void a_double_vinyl_is_replaced_by_a_readable_edition_with_the_same_tracks()
        {
            // Venice: Lidarr read "12 Vinyl 01" and "12 Vinyl 02" as two albums and downloaded the album again.
            var pinner = new PinnerFixture();
            var deluxe = Release("deluxe", "CD", 2, 2015, tracks: 20, monitored: true);
            var vinyl = Release("vinyl", "12\" Vinyl", 2, 2015, tracks: 16);
            var digital = Release("digital", "Digital Media", 1, 2014, tracks: 16);
            pinner.Releases.Setup(r => r.GetReleasesByAlbum(7)).Returns(new List<AlbumRelease> { deluxe, vinyl, digital });

            pinner.Subject.Remember("rg", new OwnedEdition("vinyl", Vinyl, 2, Array.Empty<string>(), 2015));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg", Title = "Venice" }));

            pinner.Releases.Verify(r => r.SetMonitored(digital), Times.Once());
            pinner.Releases.Verify(r => r.SetMonitored(vinyl), Times.Never());
        }

        [Test]
        public void a_double_vinyl_with_no_readable_equivalent_keeps_lidarrs_choice()
        {
            var pinner = new PinnerFixture();
            pinner.Releases.Setup(r => r.GetReleasesByAlbum(7)).Returns(new List<AlbumRelease>
            {
                Release("cd", "CD", 1, 2015, tracks: 12, monitored: true),
                Release("vinyl", "12\" Vinyl", 2, 2015, tracks: 16)
            });

            pinner.Subject.Remember("rg", new OwnedEdition("vinyl", Vinyl, 2, Array.Empty<string>(), 2015));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg" }));

            pinner.Releases.Verify(r => r.SetMonitored(It.IsAny<AlbumRelease>()), Times.Never());
        }

        [Test]
        public void a_double_vinyl_is_used_when_disc_folders_are_named_disc()
        {
            var pinner = new PinnerFixture { Format = "{Album Title}/Disc {medium:00}/{track:00} - {Track Title}" };
            var vinyl = Release("vinyl", "12\" Vinyl", 2, 2015, tracks: 16);
            pinner.Releases.Setup(r => r.GetReleasesByAlbum(7)).Returns(new List<AlbumRelease> { Release("cd", "CD", 1, 2015, monitored: true), vinyl });

            pinner.Subject.Remember("rg", new OwnedEdition("vinyl", Vinyl, 2, Array.Empty<string>(), 2015));
            pinner.Subject.Handle(new AlbumUpdatedEvent(new Album { Id = 7, ForeignAlbumId = "rg" }));

            pinner.Releases.Verify(r => r.SetMonitored(vinyl), Times.Once());
        }

        private static AlbumRelease Release(string id, string format, int discs, int year, string label = null, int tracks = 10, bool monitored = false) => new ()
        {
            ForeignReleaseId = id,
            Title = id,
            Media = Enumerable.Range(1, discs).Select(n => new Medium { Number = n, Format = format }).ToList(),
            Label = label == null ? new List<string>() : new List<string> { label },
            ReleaseDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TrackCount = tracks,
            Monitored = monitored
        };

        private class PinnerFixture
        {
            public PinnerFixture()
            {
                Files.Setup(f => f.GetFilesByAlbum(It.IsAny<int>())).Returns(new List<TrackFile>());
                Naming.Setup(n => n.GetConfig()).Returns(() => new NamingConfig { MultiDiscTrackFormat = Format });
                Subject = new OwnedEditionPinner(new CacheManager(),
                    new Lazy<IAlbumService>(() => Albums.Object),
                    new Lazy<IReleaseService>(() => Releases.Object),
                    new Lazy<IMediaFileService>(() => Files.Object),
                    new Lazy<INamingConfigService>(() => Naming.Object),
                    LogManager.CreateNullLogger());
            }

            public Mock<IAlbumService> Albums { get; } = new ();

            public Mock<IReleaseService> Releases { get; } = new ();

            public Mock<IMediaFileService> Files { get; } = new ();

            public Mock<INamingConfigService> Naming { get; } = new ();

            public string Format { get; set; } = NamingConfig.Default.MultiDiscTrackFormat;

            public OwnedEditionPinner Subject { get; }
        }
    }
}
