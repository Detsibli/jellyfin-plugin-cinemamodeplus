using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Plugin.CinemaModePlus.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Jellyfin.Plugin.CinemaModePlus
{
    /// <summary>
    /// A folder whose videos are all treated as trailers, with the physical paths it covers.
    /// </summary>
    public class TrailerLibrary
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string[] Paths { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Resolves the configured trailer libraries (and the optional Trailers4Jellyfin download folder) into
    /// library items that can be queried.
    /// </summary>
    public static class TrailerLibraryResolver
    {
        public const string Trailers4JellyfinConfigFile = "Jellyfin.Plugin.Trailers4Jellyfin.xml";

        public static bool UsesLocalTrailers(PluginConfiguration config)
        {
            return config.TrailerSource != TrailerSources.Library;
        }

        public static bool UsesLibraryTrailers(PluginConfiguration config)
        {
            return config.TrailerSource == TrailerSources.Library || config.TrailerSource == TrailerSources.Both;
        }

        /// <summary>
        /// Reads the download folder from Trailers4Jellyfin's saved configuration, if that plugin is installed.
        /// The file is read directly so there is no assembly dependency on Trailers4Jellyfin.
        /// </summary>
        public static string? GetTrailers4JellyfinFolder(ILogger logger)
        {
            try
            {
                string configPath = Path.Combine(Plugin.ServerApplicationPaths.PluginConfigurationsPath, Trailers4JellyfinConfigFile);
                if (!File.Exists(configPath))
                {
                    return null;
                }

                XDocument doc = XDocument.Load(configPath);
                string? folder = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "DownloadFolder")?.Value;
                return string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "|cinema-mode-plus| Could not read the Trailers4Jellyfin configuration");
                return null;
            }
        }

        public static List<TrailerLibrary> Resolve(PluginConfiguration config, ILogger logger)
        {
            Dictionary<Guid, TrailerLibrary> libraries = new Dictionary<Guid, TrailerLibrary>();

            foreach (string id in config.TrailerLibraries ?? new List<string>())
            {
                if (!Guid.TryParse(id, out Guid libraryId))
                {
                    continue;
                }

                BaseItem? item = Plugin.LibraryManager.GetItemById(libraryId);
                if (item is null)
                {
                    logger.LogWarning($"|cinema-mode-plus| Trailer library {id} no longer exists");
                    continue;
                }

                libraries[item.Id] = new TrailerLibrary { Id = item.Id, Name = item.Name, Paths = item.PhysicalLocations ?? Array.Empty<string>() };
            }

            if (config.UseTrailers4JellyfinFolder)
            {
                TrailerLibrary? t4j = ResolveFolder(GetTrailers4JellyfinFolder(logger), logger);
                if (t4j is not null)
                {
                    libraries.TryAdd(t4j.Id, t4j);
                }
            }

            return libraries.Values.ToList();
        }

        /// <summary>
        /// Maps a filesystem folder onto a Jellyfin item: a library whose location is that folder, or the folder
        /// item itself when it sits inside a larger library.
        /// </summary>
        private static TrailerLibrary? ResolveFolder(string? folder, ILogger logger)
        {
            if (string.IsNullOrEmpty(folder))
            {
                logger.LogInformation("|cinema-mode-plus| Trailers4Jellyfin folder requested but Trailers4Jellyfin is not installed or has no download folder");
                return null;
            }

            string target = Normalize(folder);
            foreach (var virtualFolder in Plugin.LibraryManager.GetVirtualFolders())
            {
                if (virtualFolder.Locations.Any(l => PathEquals(Normalize(l), target)) && Guid.TryParse(virtualFolder.ItemId, out Guid id))
                {
                    return new TrailerLibrary { Id = id, Name = virtualFolder.Name, Paths = new[] { folder } };
                }
            }

            BaseItem? folderItem = Plugin.LibraryManager.FindByPath(folder, true);
            if (folderItem is not null)
            {
                return new TrailerLibrary { Id = folderItem.Id, Name = folderItem.Name, Paths = new[] { folder } };
            }

            logger.LogWarning($"|cinema-mode-plus| Trailers4Jellyfin folder '{folder}' is not part of any Jellyfin library. Add it as a library so its trailers can be played.");
            return null;
        }

        /// <summary>True when the item lives inside one of the trailer libraries (e.g. someone plays a trailer directly).</summary>
        public static bool IsInTrailerLibrary(BaseItem item, IEnumerable<TrailerLibrary> libraries)
        {
            if (string.IsNullOrEmpty(item.Path))
            {
                return false;
            }

            string itemPath = Normalize(item.Path);
            return libraries.SelectMany(l => l.Paths).Any(p => IsUnder(itemPath, Normalize(p)));
        }

        private static string Normalize(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // Keep the raw path if it cannot be normalised.
            }

            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparison PathComparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static bool PathEquals(string a, string b) => string.Equals(a, b, PathComparison);

        private static bool IsUnder(string path, string folder)
        {
            return path.StartsWith(folder + Path.DirectorySeparatorChar, PathComparison)
                || path.StartsWith(folder + Path.AltDirectorySeparatorChar, PathComparison);
        }
    }
}
