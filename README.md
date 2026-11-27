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
5. Choose the usual Lidarr options (root folder, profiles, monitoring), then **Test** and **Save**.

### Sharing one token

You only need to enter the token once. A Discogs list with a blank token uses the one saved on the oldest other Discogs list. If your lists use different accounts, enter a token on each of them. If you delete the list that holds the token, enter it on another one.

The token is stored the way Lidarr stores any API key: in Lidarr's database, hidden in the interface and API. The plugin sends it only to `api.discogs.com`, in a request header, and never logs it.

## How matching works

Discogs does not provide MusicBrainz IDs, so Lidarr searches by artist and album name.

- Releases Lidarr cannot match are skipped. Pressings with unusual titles are the most likely to be missed.
- If Lidarr's metadata search fails for one release, that release is skipped with a warning in the log, and the rest of the list still imports. If 25 searches fail in a row, the plugin stops and tries again later.
- Collaborations use the first credited artist for the collection and wantlist.
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
