using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CinemaMode.Configuration
{
    public class PreRollSelectionConfig
    {
        public bool Name { get; set; }
        public bool Year { get; set; }
        public bool Decade { get; set; }
        public bool Seasonal { get; set; }
        public bool Genre { get; set; }
        public bool Studios { get; set; }
        public bool AllTags { get; set; }

        public PreRollSelectionConfig()
        {
            Name = false;
            Year = false;
            Decade = false;
            Seasonal = false;
            Genre = false;
            Studios = false;
            AllTags = false;
        }
    }

    public class TrailerSelectionConfig
    {
        public bool Year { get; set; }
        public bool Decade { get; set; }
        public bool Genre { get; set; }
        public bool RecentlyAdded { get; set; }
        public bool MoreLikeThis { get; set; }
        public bool Unplayed { get; set; }

        public TrailerSelectionConfig()
        {
            Year = false;
            Decade = false;
            Genre = false;
            RecentlyAdded = false;
            MoreLikeThis = false;
            Unplayed = false;
        }
    }

    public class SeasonalTagDefinition
    {
        public string Tag { get; set; }
        public string Start { get; set; }
        public string End { get; set; }

        public SeasonalTagDefinition()
        {
            Tag = "";
            Start = "";
            End = "";
        }
    }

    /// <summary>Where trailers are pulled from. Stored as strings so the config page/XML stay readable.</summary>
    public static class TrailerSources
    {
        /// <summary>Trailers stored alongside movies (movie folder / "Trailers" subfolder / -trailer suffix).</summary>
        public const string LocalTrailers = "LocalTrailers";

        /// <summary>Every video in the selected trailer libraries is a trailer.</summary>
        public const string Library = "Library";

        /// <summary>Both of the above.</summary>
        public const string Both = "Both";
    }

    /// <summary>How trailers from the two sources are combined when <see cref="TrailerSources.Both"/> is used.</summary>
    public static class TrailerMixModes
    {
        /// <summary>Alternate between library and local trailers, starting with the library.</summary>
        public const string Alternate = "Alternate";

        /// <summary>Pick randomly from the combined pool.</summary>
        public const string Shuffle = "Shuffle";

        /// <summary>Use library trailers first, fall back to local trailers.</summary>
        public const string LibraryFirst = "LibraryFirst";

        /// <summary>Use local trailers first, fall back to library trailers.</summary>
        public const string LocalFirst = "LocalFirst";
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public string TrailerPreRollsLibrary { get; set; }
        public string FeaturePreRollsLibrary { get; set; }
        public List<PreRollSelectionConfig> FeaturePreRollsSelections { get; set; }
        public List<PreRollSelectionConfig> TrailerPreRollsSelections { get; set; }
        public bool TrailerPreRollsRatingLimit { get; set; }
        public bool FeaturePreRollsRatingLimit { get; set; }
        public bool TrailerPreRollsIgnoreOutOfSeason { get; set; }
        public bool FeaturePreRollsIgnoreOutOfSeason { get; set; }
        public List<SeasonalTagDefinition> SeasonalTagDefinitions { get; set; }
        public List<TrailerSelectionConfig> TrailerSelectionRules { get; set; }
        public bool EnforceRatingLimitTrailers { get; set; }
        public int NumberOfTrailers { get; set; }
        public bool TrailerConsumeMode { get; set; }
        public string TrailerSource { get; set; }
        public List<string> TrailerLibraries { get; set; }
        public bool UseTrailers4JellyfinFolder { get; set; }
        public string TrailerMixMode { get; set; }
        public bool LibraryTrailersAllowUnrated { get; set; }

        public PluginConfiguration()
        {
            TrailerPreRollsLibrary = "-";
            FeaturePreRollsLibrary = "-";
            TrailerPreRollsSelections = new List<PreRollSelectionConfig>();
            FeaturePreRollsSelections = new List<PreRollSelectionConfig>();
            TrailerPreRollsRatingLimit = true;
            FeaturePreRollsRatingLimit = true;
            TrailerPreRollsIgnoreOutOfSeason = true;
            FeaturePreRollsIgnoreOutOfSeason = true;
            SeasonalTagDefinitions = new List<SeasonalTagDefinition>();
            TrailerSelectionRules = new List<TrailerSelectionConfig>();
            NumberOfTrailers = 2;
            EnforceRatingLimitTrailers = true;
            TrailerConsumeMode = false;
            TrailerSource = TrailerSources.LocalTrailers;
            TrailerLibraries = new List<string>();
            UseTrailers4JellyfinFolder = false;
            TrailerMixMode = TrailerMixModes.Alternate;
            LibraryTrailersAllowUnrated = false;
        }
    }
}
