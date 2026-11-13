using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.CinemaModePlus.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.CinemaModePlus
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Cinema Mode Plus";

        public override Guid Id => Guid.Parse("95a28cc1-e7c7-47b2-976b-32aa580be17e");

        public static Plugin Instance { get; private set; }

        public PluginConfiguration PluginConfiguration => Configuration;

        public static IServerApplicationPaths ServerApplicationPaths { get; private set; }

        public static ILibraryManager LibraryManager { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILibraryManager libraryManager, IServerApplicationPaths serverApplicationPaths)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            LibraryManager = libraryManager;
            ServerApplicationPaths = serverApplicationPaths;
            ImportOriginalCinemaModeConfiguration(applicationPaths, xmlSerializer);
        }

        /// <summary>
        /// On first run, copy the settings of the original Cinema Mode plugin (by CherryFloors) if present.
        /// The configuration schema is a superset of the original, so the file deserializes directly.
        /// </summary>
        private void ImportOriginalCinemaModeConfiguration(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        {
            try
            {
                string originalConfig = Path.Combine(applicationPaths.PluginConfigurationsPath, OriginalConfigurationFileName);
                if (File.Exists(ConfigurationFilePath) || !File.Exists(originalConfig))
                {
                    return;
                }

                if (xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration), originalConfig) is PluginConfiguration imported)
                {
                    UpdateConfiguration(imported);
                }
            }
            catch (Exception)
            {
                // Fall back to default settings if the original configuration cannot be read.
            }
        }

        private const string OriginalConfigurationFileName = "Jellyfin.Plugin.CinemaMode.xml";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
            };
        }
    }
}
