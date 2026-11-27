# Lidarr Lists for Discogs

A [Lidarr](https://lidarr.audio) plugin that imports the music in your Discogs account, using three import lists:

| Import list | What it imports |
| --- | --- |
| **Discogs Collection** | Your collection, optionally limited to the folders you pick |
| **Discogs Wantlist** | Your wantlist |
| **Discogs User Lists** | Your public and private lists, optionally limited to the lists you pick |

The plugin is **read-only**. It only ever sends `GET` requests to the Discogs API, and the build fails if that changes.

## Requirements

- Lidarr 3.1.6.5079 or later on the 3.x (.NET 8) line. Lidarr builds that run on .NET 10 are not supported yet.
- A Discogs account and a [personal access token](https://www.discogs.com/settings/developers).

## Install

1. In Lidarr, open **System > Plugins**.
2. Paste `https://github.com/techguydave/lidarr-plugin-discogs-lists` and select **Install**.
3. Restart Lidarr.

To test pre-releases, which are not intended for production use, install `https://github.com/techguydave/lidarr-plugin-discogs-lists/tree/develop` instead. Pre-releases are published before the first stable release is.

> **Note:** Lidarr deletes the import lists of a plugin that is uninstalled or fails to load.

## Set up

1. Generate a token in your [Discogs developer settings](https://www.discogs.com/settings/developers) with **Generate token**.
2. In Lidarr, open **Settings > Import Lists**, select **+**, and choose a Discogs list (under **Other List**).
3. Paste the token into **Personal Access Token**.
4. For the collection or user lists, choose what to import. Leave the selection empty to import everything.
5. To import only the albums on your Discogs list, set:
   - **Monitor** to **Specific Album**. Lidarr's default, **All Artist Albums**, monitors every album by each artist.
   - **Metadata Profile** to **None**. Any other profile adds every album by each artist to your library, thousands for some classical composers, and monitors them all until Lidarr finishes refreshing them. **Test** warns about this.
   - **Monitor New Albums** to **None**, unless you want future releases by these artists too.
6. Choose a root folder and quality profile, then **Test** and **Save**.

With **Metadata Profile: None**, a user list entry that is an artist rather than a release adds that artist with no albums.

### Sharing one token

You only need to enter the token once. A Discogs list with a blank token uses the one saved on the oldest other Discogs list. If your lists use different accounts, enter a token on each of them. If you delete the list that holds the token, enter it on another one.

The token is stored the way Lidarr stores any API key: in Lidarr's database, hidden in the interface and API. The plugin sends it only to `api.discogs.com`, in a request header, and never logs it.

## How matching works

Lidarr needs a MusicBrainz ID for each album, which Discogs doesn't provide. The plugin finds one in two ways:

1. **MusicBrainz links.** MusicBrainz editors link many albums and artists to their Discogs pages. When your Discogs release, its master release or an artist has such a link, the plugin uses it, which finds the exact recording.
2. **Name search.** Otherwise it searches Lidarr by artist and title. It only accepts a result whose title and artist are close to the Discogs ones, using the year to choose between similar albums.

Releases with no close match are skipped.

### Editions

Lidarr tracks albums, and monitors one edition of each. Left alone, it picks the edition with the most tracks, often a deluxe edition, and then rejects your files as having missing tracks. When the plugin adds an album, it monitors the edition you own instead:

1. If MusicBrainz links your Discogs release to one of its releases, that exact release.
2. Otherwise, for the collection and wantlist, the edition with the same format (vinyl, CD, cassette and so on), then the closest number of discs, label and year.

**Multi-disc editions.** Lidarr puts each disc in its own folder, named `{Medium Format} {medium:00}` by default, such as `12 Vinyl 01`. When it rescans your library, it only joins folders named `CD` or `Disc` back into one album. It reads any others as separate albums, sees the album as incomplete, downloads it again and deletes the files it has. So the plugin doesn't pick a multi-disc vinyl, cassette or digital edition with that naming. It picks an edition with the same number of tracks that Lidarr can rescan, and says so in the log. To get the exact edition, change the disc folder in **Settings > Media Management > Multi Disc Track Format** to `Disc {medium:00}`.

The plugin only changes albums it adds to your library, once, before they have files. It never changes an album that was already in your library, or an edition you pick on the album's page. If no edition matches, Lidarr's choice stands. User lists only use MusicBrainz links, as Discogs doesn't send their formats.

To find links, each sync sends the Discogs release, master and artist IDs of your items to [MusicBrainz](https://musicbrainz.org), read-only and at most about one request per second. No Discogs token or account details are sent. If MusicBrainz is unavailable, the plugin matches by name.

- Releases Lidarr cannot match are skipped. Pressings with unusual titles are the most likely to be missed.
- If Lidarr's metadata search fails for one release, that release is skipped with a warning in the log, and the rest of the list still imports. If 25 searches fail in a row, the plugin stops and tries again later.
- Collaborations use the first credited artist for the collection and wantlist.
- Artists already in your library keep their own monitoring and metadata profile. To trim one, set its **Metadata Profile** to **None** in **Artist Editor** and refresh it. Albums with files are kept.
- "Various" artist compilations are skipped.
- In user lists, releases, masters and artists are imported. Labels are skipped.

## Limits

- Each list refreshes at most every 6 hours.
- The plugin stays under Discogs' limit of 60 requests per minute, so large collections take a while.
- When you change a folder or list selection, Lidarr drops previously saved entries that no longer exist on Discogs.

## Discogs notice

[Data provided by Discogs](https://www.discogs.com).

This application uses Discogs' API but is not affiliated with, sponsored or endorsed by Discogs. 'Discogs' is a trademark of Zink Media, LLC.

Your use of the Discogs API is subject to the [API Terms of Use](https://support.discogs.com/hc/articles/360009334593-API-Terms-of-Use).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Licensed under [GPL-3.0](LICENSE.md).
