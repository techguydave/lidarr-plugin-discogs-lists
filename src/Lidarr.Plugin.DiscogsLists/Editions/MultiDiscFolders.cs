using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Music;

namespace Lidarr.Plugin.DiscogsLists.Editions
{
    // Lidarr writes each disc of a multi-disc release into its own folder, named by the Multi Disc Track Format
    // setting, which defaults to "{Medium Format} {medium:00}". When it scans the library again, it only joins
    // such folders back into one album if their names end in "CD", "Disc" or "Disk" and a number
    // (TrackGroupingService in Lidarr). Folders like "12 Vinyl 01" and "12 Vinyl 02" are read as two albums:
    // Lidarr then sees the album as incomplete, downloads it again, and deletes the files it has.
    //
    // This tells whether a release would be written in folders that Lidarr can read back.
    public static class MultiDiscFolders
    {
        private static readonly Regex Readable = new (@"^(?<root>.*(?:dis[ck]|cd)[\W_]*)\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MediumNumber = new (@"\{medium(?:\:0+)?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MediumFormat = new (@"\{medium[ ._-]?format\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MediumName = new (@"\{medium[ ._-]?name\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OtherToken = new (@"\{[^{}]+\}", RegexOptions.Compiled);

        public static bool CanBeReadBack(AlbumRelease release, string multiDiscTrackFormat)
        {
            var media = release?.Media ?? new List<Medium>();

            if (media.Count <= 1 || string.IsNullOrWhiteSpace(multiDiscTrackFormat))
            {
                return true;
            }

            var parts = multiDiscTrackFormat.Split('/', '\\');

            if (parts.Length < 2)
            {
                return true;
            }

            // Lidarr only looks at the folder that holds the files.
            var folder = parts[^2];

            if (!MediumNumber.IsMatch(folder) && !MediumFormat.IsMatch(folder) && !MediumName.IsMatch(folder))
            {
                // Every disc goes into the same folder.
                return true;
            }

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var medium in media)
            {
                var name = MediumFormat.Replace(folder, _ => medium.Format ?? string.Empty);
                name = MediumName.Replace(name, _ => medium.Name ?? string.Empty);
                name = MediumNumber.Replace(name, _ => medium.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                name = OtherToken.Replace(name, "x").Trim();

                var match = Readable.Match(name);

                if (!match.Success)
                {
                    return false;
                }

                roots.Add(match.Groups["root"].Value);
            }

            // The folders must differ only in their number.
            return roots.Count == 1;
        }
    }
}
