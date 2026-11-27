# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com), and the project follows [Semantic Versioning](https://semver.org).

## [Unreleased]

### Added

- **Discogs Collection** import list, with an optional multi-select of collection folders.
- **Discogs Wantlist** import list.
- **Discogs User Lists** import list for public and private lists, with an optional multi-select.
- Personal access token authentication, entered once and shared between the three lists, with a per-list override.
- Read-only Discogs access with rate limiting.
- A release whose metadata search fails is skipped, instead of stopping the whole import.
- Albums and artists are matched using MusicBrainz's links to Discogs where they exist, and otherwise by a name search that rejects poor matches.
- Albums the plugin adds monitor the edition you own on Discogs, matched by MusicBrainz link or by format, discs, label and year, instead of Lidarr's default of the edition with the most tracks.
- A multi-disc edition is only monitored if Lidarr can rescan the folders it writes for it. Otherwise Lidarr reads each disc as a separate album, downloads the album again and deletes its files.
- **Test** warns when **Monitor: Specific Album** is combined with a metadata profile other than **None**, which adds every album by each artist.
