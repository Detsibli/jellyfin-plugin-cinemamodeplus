using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.CinemaMode.Configuration;

#nullable enable

namespace Jellyfin.Plugin.CinemaMode
{

    enum PreRollType
    {
        TrailerPreRoll,
        FeaturePreRoll,
    }

    class PreRollSelector
    {
        private PreRollType Category { get; } 
        private Random RNG { get; }
        private BaseItem Feature { get; }
        private User User { get; }
        private string PreRollLibrary { get; }
        private List<PreRollSelectionConfig> PreRollsSelections { get; }
        private bool IgnoreOutOfSeason { get; }
        private bool EnforceRatingLimit { get; }
        private List<SeasonalTagDefinition> SeasonalTagDefinitions { get; }
        private readonly ILogger Logger;


        public PreRollSelector(PreRollType Category, BaseItem Feature, User User, PluginConfiguration Config, ILogger logger)
        {
            this.Category = Category;
            this.RNG = new Random();
            this.Feature = Feature;
            this.User = User;
            this.Logger = logger;

            if (Category == PreRollType.TrailerPreRoll)
            {
                this.PreRollLibrary = Config.TrailerPreRollsLibrary;
                this.PreRollsSelections = Config.TrailerPreRollsSelections;
                this.EnforceRatingLimit = Config.TrailerPreRollsRatingLimit;
                this.SeasonalTagDefinitions = Config.SeasonalTagDefinitions;
                this.IgnoreOutOfSeason = Config.TrailerPreRollsIgnoreOutOfSeason;
            }
            else
            {
                this.PreRollLibrary = Config.FeaturePreRollsLibrary;
                this.PreRollsSelections = Config.FeaturePreRollsSelections;
                this.EnforceRatingLimit = Config.FeaturePreRollsRatingLimit;
                this.SeasonalTagDefinitions = Config.SeasonalTagDefinitions;
                this.IgnoreOutOfSeason = Config.FeaturePreRollsIgnoreOutOfSeason;
            }
        }

        public List<String> PreRollYearTags() 
        {
            if (!this.Feature.PremiereDate.HasValue)
            {
                return new List<string>();
            }
            
            String Year = this.Feature.PremiereDate.Value.Year.ToString();
            String Decade = $"{Year.Substring(0, 3)}0s";
            return new List<string>() { Year, Decade };
        }
        
        public bool InSeason(SeasonalTagDefinition seasonalTag, DateTime today)
        {
            DateTime startDate = DateTime.Parse(seasonalTag.Start);
            DateTime endDate = DateTime.Parse(seasonalTag.End);

            int yearDiff = endDate.Year - startDate.Year;
            startDate = new DateTime(today.Year, startDate.Month, startDate.Day);
            endDate = new DateTime(today.Year + yearDiff, endDate.Month, endDate.Day);

            if (today >= startDate && today <= endDate)
            {
                return true;
            }   
            return false;
        }

        public List<String> PreRollSeasonTags()
        {
            DateTime today = DateTime.Now;
            List<string> Tags = new List<string>();

            foreach (SeasonalTagDefinition seasonalTag in this.SeasonalTagDefinitions)
            {
                if (InSeason(seasonalTag, today))
                {
                    Tags.Add(seasonalTag.Tag);
                }   
            }

            return Tags;
        }

        private string[] OutOfSeasonTags()
        {
            DateTime today = DateTime.Now;
            List<string> Tags = new List<string>();

            foreach (SeasonalTagDefinition seasonalTag in this.SeasonalTagDefinitions)
            {
                if (!InSeason(seasonalTag, today))
                {
                    Tags.Add(seasonalTag.Tag);
                }   
            }
            return Tags.ToArray();
        }

        public Guid[] GetStudioIds()
        {
            List<Guid> ids = new List<Guid>();
            foreach (string studio in Feature.Studios)
            {
                InternalItemsQuery query = new InternalItemsQuery();
                query.IncludeItemTypes = new BaseItemKind[] { BaseItemKind.Studio };
                query.Name = studio;
                IReadOnlyList<BaseItem> items = Plugin.LibraryManager.GetItemList(query);
                foreach (BaseItem item in items)
                {
                    ids.Add(item.Id);
                } 
            }
            return ids.ToArray();
        }


        public InternalItemsQuery QueryBuilder(PreRollSelectionConfig? SelectionConfig)
        {
            InternalItemsQuery query = new InternalItemsQuery(this.User);
            query.ParentId = Guid.Parse(this.PreRollLibrary);
            query.Recursive = true;
            query.IncludeItemTypes = new BaseItemKind[] { BaseItemKind.Movie };

            if (this.EnforceRatingLimit)
            {
                query.MaxParentalRating = this.Feature.GetParentalRatingScore();
            }

            if (SelectionConfig == null)
            {
                return query;
            }

            if (SelectionConfig.Genre)
            {
                query.Genres = Feature.Genres;
            }

            if (SelectionConfig.Studios)
            {
                Guid[] ids = GetStudioIds();
                query.StudioIds = ids;
            }

            List<String> tags = new List<String>();
            if (SelectionConfig.Name)
            {
                tags.Add(Feature.Name);
            }

            if (SelectionConfig.Year && this.Feature.PremiereDate.HasValue)
            {
                tags.Add(this.Feature.PremiereDate.Value.Year.ToString());
            }

            if (SelectionConfig.Decade && this.Feature.PremiereDate.HasValue)
            {
                tags.Add($"{this.Feature.PremiereDate.Value.Year.ToString().Substring(0, 3)}0s");
            }

            if (SelectionConfig.Seasonal)
            {
                tags.AddRange(PreRollSeasonTags());
            }

            if (tags.Count > 0)
            {
                query.Tags = tags.ToArray();
            }

            return query;
        }

        public List<Movie> QueryPreRolls(PreRollSelectionConfig? SelectionConfig)
        {
            InternalItemsQuery query = QueryBuilder(SelectionConfig);
            IReadOnlyList<BaseItem> items = Plugin.LibraryManager.GetItemList(query);
            if (SelectionConfig != null && SelectionConfig.AllTags)
            {
                items = items.Where(pR => query.Tags.All(t => pR.Tags.Contains(t))).ToList();
            }

            if (this.IgnoreOutOfSeason)
            {
                string[] excludeTags = OutOfSeasonTags(); 
                if (excludeTags.Length > 0)
                {
                    items = items.Where(pR => !excludeTags.Any(t => pR.Tags.Contains(t))).ToList();
                }
            }
            
            return items.OfType<Movie>().ToList();
        }

        public IntroInfo? GetPreRoll()
        {
            List<Movie> preRolls;
            foreach (PreRollSelectionConfig SelectConfig in this.PreRollsSelections)
            {
                preRolls = QueryPreRolls(SelectConfig);
                if (preRolls.Count > 0) {
                    int idx = this.RNG.Next(preRolls.Count);
                    Video preRoll = preRolls.ElementAt(idx);
                    return new IntroInfo { ItemId = preRoll.Id, Path = preRoll.Path };
                }
            }

            preRolls = QueryPreRolls(null);
            if (preRolls.Count > 0) {
                int idx = this.RNG.Next(preRolls.Count);
                Video preRoll = preRolls.ElementAt(idx);
                return new IntroInfo { ItemId = preRoll.Id, Path = preRoll.Path };
            }

            this.Logger.LogInformation($"|jellyfin-cinema-mode| No Pre-Rolls Type: {this.Category} User: {this.User}");
            return null;

        }
    }

    class TrailerSelector
    {
        // Movies that have local trailers (trailers in the movie's folder / Trailers subfolder).
        private List<Movie> LocalCandidates { get; set; } = new List<Movie>() { };
        // Videos from the trailer libraries; the item itself is the trailer.
        private List<BaseItem> LibraryCandidates { get; set; } = new List<BaseItem>() { };
        private int Returned { get; set; } = 0;
        private List<Guid> ShownTrailers = new List<Guid>() { };
        private Random RNG { get; }
        private PluginConfiguration Config { get; }
        private BaseItem Feature { get; }
        private User User { get; }
        private List<TrailerSelectionConfig> selectionRules { get; set; } = new List<TrailerSelectionConfig>() { };
        private readonly ILogger Logger;
        private bool UseLocal { get; }
        private List<TrailerLibrary> Libraries { get; }
        private bool NextFromLibrary = true;

        private int CandidateCount => this.LocalCandidates.Count + this.LibraryCandidates.Count;

        public TrailerSelector(BaseItem Feature, User User, PluginConfiguration Config, List<TrailerLibrary> libraries, ILogger logger)
        {
            this.ShownTrailers.Add(Feature.Id);
            this.RNG = new Random();
            this.Config = Config;
            this.Feature = Feature;
            this.User = User;
            this.Logger = logger;
            this.UseLocal = TrailerLibraryResolver.UsesLocalTrailers(Config);
            this.Libraries = TrailerLibraryResolver.UsesLibraryTrailers(Config) ? libraries : new List<TrailerLibrary>();
            foreach (TrailerSelectionConfig selectionConfig in Config.TrailerSelectionRules)
            {
               this.selectionRules.Add(selectionConfig);
            }
        }

        private InternalItemsQuery BaseQuery(bool IsPlayed, bool forLibrary)
        {
            InternalItemsQuery baseQuery = new InternalItemsQuery(this.User);
            baseQuery.ExcludeItemIds = this.ShownTrailers.ToArray();

            if (forLibrary)
            {
                baseQuery.Recursive = true;
                baseQuery.IncludeItemTypes = new BaseItemKind[] { BaseItemKind.Movie, BaseItemKind.Video };
            }
            else
            {
                baseQuery.HasTrailer = true;
                baseQuery.IncludeItemTypes = new BaseItemKind[] { BaseItemKind.Movie };
            }

            // Unrated library trailers are rating-filtered in memory (QueryLibraryTrailers) so the
            // server query cannot drop items that have no rating.
            if (this.Config.EnforceRatingLimitTrailers && !(forLibrary && this.Config.LibraryTrailersAllowUnrated))
            {
                baseQuery.HasOfficialRating = true;
                baseQuery.HasParentalRating = true;
                baseQuery.MinParentalRating = new ParentalRatingScore(0, 0);
                baseQuery.MaxParentalRating = this.Feature.GetParentalRatingScore();
            }

            baseQuery.IsPlayed = IsPlayed;

            return baseQuery;
        }

        private InternalItemsQuery BuildQuery(TrailerSelectionConfig config, bool forLibrary)
        {
            InternalItemsQuery configQuery = BaseQuery(!config.Unplayed, forLibrary);

            if (config.Year && Feature.PremiereDate.HasValue)
            {
                int year = Feature.PremiereDate.Value.Year;
                configQuery.MinPremiereDate = new DateTime(year, 1, 1);
                configQuery.MaxPremiereDate = new DateTime(year, 12, 31);
            }

            if (config.Decade && Feature.PremiereDate.HasValue)
            {
                int year_start = (Feature.PremiereDate.Value.Year / 10) * 10;
                int year_end = year_start + 9;
                configQuery.MinPremiereDate = new DateTime(year_start, 1, 1);
                configQuery.MaxPremiereDate = new DateTime(year_end, 12, 31);
            }

            if (config.Genre)
            {
                configQuery.Genres = Feature.Genres;
            }

            if (config.RecentlyAdded)
            {
                configQuery.MinDateCreated = DateTime.Today.AddMonths(-1);
            }

            if (config.MoreLikeThis)
            {
                configQuery.Genres = this.Feature.Genres;
                configQuery.Tags = this.Feature.Tags;
                configQuery.EnableGroupByMetadataKey = true;
            }

            return configQuery;
        }

        private void QueryLocalTrailers(Func<bool, InternalItemsQuery> queryFactory)
        {
            this.LocalCandidates = new List<Movie>();
            if (!this.UseLocal)
            {
                return;
            }

            IReadOnlyList<BaseItem> baseItems = Plugin.LibraryManager.GetItemList(queryFactory(false));
            IEnumerable<Movie> moviesIter = baseItems.OfType<Movie>().Where(x => x.LocalTrailers.Count > 0);

            // Videos inside a trailer library are trailers themselves, not features with trailers.
            if (this.Libraries.Count > 0)
            {
                moviesIter = moviesIter.Where(x => !TrailerLibraryResolver.IsInTrailerLibrary(x, this.Libraries));
            }

            // TODO: Remove when jf team fixes min rating query
            if (this.Config.EnforceRatingLimitTrailers)
            {
                moviesIter = moviesIter.Where(x => x.InheritedParentalRatingValue.HasValue);
            }

            this.LocalCandidates = moviesIter.ToList();
        }

        private void QueryLibraryTrailers(Func<bool, InternalItemsQuery> queryFactory)
        {
            this.LibraryCandidates = new List<BaseItem>();
            if (this.Libraries.Count == 0)
            {
                return;
            }

            Dictionary<Guid, BaseItem> found = new Dictionary<Guid, BaseItem>();
            foreach (TrailerLibrary library in this.Libraries)
            {
                InternalItemsQuery query = queryFactory(true);
                query.ParentId = library.Id;
                foreach (BaseItem item in Plugin.LibraryManager.GetItemList(query))
                {
                    if (item is Video && !string.IsNullOrEmpty(item.Path))
                    {
                        found.TryAdd(item.Id, item);
                    }
                }
            }

            IEnumerable<BaseItem> items = found.Values.Where(x => !this.IsTrailerForFeature(x));

            if (this.Config.EnforceRatingLimitTrailers)
            {
                int? maxRating = this.Feature.InheritedParentalRatingValue;
                items = this.Config.LibraryTrailersAllowUnrated
                    ? items.Where(x => !x.InheritedParentalRatingValue.HasValue || x.InheritedParentalRatingValue <= maxRating)
                    : items.Where(x => x.InheritedParentalRatingValue.HasValue);
            }

            this.LibraryCandidates = items.ToList();
        }

        /// <summary>Skip a library trailer that advertises the feature we are about to play.</summary>
        private bool IsTrailerForFeature(BaseItem trailer)
        {
            foreach (MetadataProvider provider in new[] { MetadataProvider.Tmdb, MetadataProvider.Imdb })
            {
                string? featureId = this.Feature.GetProviderId(provider);
                if (!string.IsNullOrEmpty(featureId) && featureId == trailer.GetProviderId(provider))
                {
                    return true;
                }
            }

            return string.Equals(trailer.Name, this.Feature.Name, StringComparison.OrdinalIgnoreCase)
                && trailer.ProductionYear == this.Feature.ProductionYear;
        }

        private void QueryTrailers(Func<bool, InternalItemsQuery> queryFactory)
        {
            this.QueryLocalTrailers(queryFactory);
            this.QueryLibraryTrailers(queryFactory);

            if (this.CandidateCount == 0)
            {
                this.Logger.LogDebug($"|jellyfin-cinema-mode| No trailer found: {this.Feature.Name} {this.Feature.InheritedParentalRatingValue})");
            }
        }

        private void SelectionRuleQuery()
        {
            this.LocalCandidates = new List<Movie>();
            this.LibraryCandidates = new List<BaseItem>();
            while (this.selectionRules.Count != 0 && this.CandidateCount == 0)
            {
                TrailerSelectionConfig trailerSelection = this.selectionRules.ElementAt(0);
                this.selectionRules.RemoveAt(0);
                this.QueryTrailers(forLibrary => this.BuildQuery(trailerSelection, forLibrary));
            }
        }

        private bool ChooseLibrary()
        {
            if (this.LibraryCandidates.Count == 0)
            {
                return false;
            }

            if (this.LocalCandidates.Count == 0)
            {
                return true;
            }

            switch (this.Config.TrailerMixMode)
            {
                case TrailerMixModes.LibraryFirst:
                    return true;
                case TrailerMixModes.LocalFirst:
                    return false;
                case TrailerMixModes.Shuffle:
                    return this.RNG.Next(this.CandidateCount) >= this.LocalCandidates.Count;
                default:
                    return this.NextFromLibrary;
            }
        }

        public IntroInfo PickAndPop()
        {
            bool fromLibrary = this.ChooseLibrary();
            this.NextFromLibrary = !fromLibrary;

            if (fromLibrary)
            {
                int libIdx = this.RNG.Next(this.LibraryCandidates.Count);
                BaseItem trailer = this.LibraryCandidates.ElementAt(libIdx);
                this.ShownTrailers.Add(trailer.Id);
                this.LibraryCandidates.RemoveAt(libIdx);
                return new IntroInfo { ItemId = trailer.Id, Path = trailer.Path };
            }

            int idx = this.RNG.Next(this.LocalCandidates.Count);
            Movie movie = this.LocalCandidates.ElementAt(idx);
            this.ShownTrailers.Add(movie.Id);
            this.LocalCandidates.RemoveAt(idx);

            int trailerID = this.RNG.Next(movie.LocalTrailers.Count);
            BaseItem item = movie.LocalTrailers.ElementAt(trailerID);
            return new IntroInfo { ItemId = item.Id, Path = item.Path };
        }

        public IEnumerable<IntroInfo> GetTrailers()
        {
            // Early break if we are enforcing trailer limit and the feature is "unrated" or has no rating
            if (this.Config.EnforceRatingLimitTrailers && this.Feature.InheritedParentalRatingValue == null)
            {
                this.Logger.LogInformation("|jellyfin-cinema-mode| Feature has no rating, skipping trailers");
                yield break;
            }

            if (!this.UseLocal && this.Libraries.Count == 0)
            {
                this.Logger.LogInformation("|jellyfin-cinema-mode| Trailer source is set to library but no trailer library could be resolved");
                yield break;
            }

            // Selection Rules
            this.SelectionRuleQuery();
            while (this.CandidateCount != 0 && this.Returned < this.Config.NumberOfTrailers)
            {
                yield return this.PickAndPop();
                this.Returned++;

                if (this.Config.TrailerConsumeMode | this.CandidateCount == 0)
                {
                   SelectionRuleQuery();
                }
            }

            // Break if we have reached count
            if (this.Returned == this.Config.NumberOfTrailers)
            {
                yield break;
            }

            // Unplayed
            this.QueryTrailers(forLibrary => this.BaseQuery(false, forLibrary));
            while (this.CandidateCount != 0 && this.Returned < this.Config.NumberOfTrailers)
            {
                yield return this.PickAndPop();
                this.Returned++;
            }

            // Break if we have reached count
            if (this.Returned == this.Config.NumberOfTrailers)
            {
                yield break;
            }

            // Played
            this.QueryTrailers(forLibrary => this.BaseQuery(true, forLibrary));
            while (this.CandidateCount != 0 && this.Returned < this.Config.NumberOfTrailers)
            {
                yield return this.PickAndPop();
                this.Returned++;
            }

        }
    }

    public class IntroManager
    {
        private readonly Random _random = new Random();

        private readonly ILogger Logger;

        public IntroManager(ILogger logger)
        {
            this.Logger = logger;
        }

        public IEnumerable<IntroInfo> Get(BaseItem item, User user)
        {
            PluginConfiguration config = Plugin.Instance.Configuration;
            List<TrailerLibrary> trailerLibraries = new List<TrailerLibrary>();
            if (config.NumberOfTrailers > 0 && TrailerLibraryResolver.UsesLibraryTrailers(config))
            {
                try
                {
                    trailerLibraries = TrailerLibraryResolver.Resolve(config, this.Logger);
                }
                catch (System.Exception e)
                {
                    this.Logger.LogError(e, "|jellyfin-cinema-mode| Exception encountered resolving trailer libraries");
                }

                // Playing a trailer straight from a trailer library should not get its own cinema intro.
                if (TrailerLibraryResolver.IsInTrailerLibrary(item, trailerLibraries))
                {
                    yield break;
                }
            }

            if (Plugin.Instance.Configuration.TrailerPreRollsLibrary != "-")
            {
                IntroInfo? trailerPreRoll = null;
                try
                {
                    PreRollSelector preRollSelector = new PreRollSelector(PreRollType.TrailerPreRoll, item, user, Plugin.Instance.Configuration, this.Logger);
                    trailerPreRoll = preRollSelector.GetPreRoll();
                }
                catch (System.Exception e)
                {
                    this.Logger.LogError("|jellyfin-cinema-mode| Exception encountered fetching Trailer Pre-Roll");
                    this.Logger.LogError(e.StackTrace);
                }

                if (trailerPreRoll != null)
                {
                    yield return trailerPreRoll;
                }
            }

            if (Plugin.Instance.Configuration.NumberOfTrailers > 0)
            {
                List<IntroInfo> trailers = new List<IntroInfo>();
                try
                {
                    TrailerSelector trailerSelector = new TrailerSelector(item, user, Plugin.Instance.Configuration, trailerLibraries, this.Logger);
                    trailers = trailerSelector.GetTrailers().ToList();
                }
                catch (System.Exception e)
                {
                    this.Logger.LogError("|jellyfin-cinema-mode| Exception encountered fetching Trailers");
                    this.Logger.LogError(e.StackTrace);
                }

                foreach (IntroInfo trailer in trailers)
                {
                    yield return trailer;
                }
            }

            if (Plugin.Instance.Configuration.FeaturePreRollsLibrary != "-")
            {
                IntroInfo? featurePreRoll = null;
                try
                {
                    PreRollSelector preRollSelector = new PreRollSelector(PreRollType.FeaturePreRoll, item, user, Plugin.Instance.Configuration, this.Logger);
                    featurePreRoll = preRollSelector.GetPreRoll();
                }
                catch (System.Exception e)
                {
                    this.Logger.LogError("|jellyfin-cinema-mode| Exception encountered fetching Feature Pre-Roll");
                    this.Logger.LogError(e.StackTrace);
                }

                if (featurePreRoll != null)
                {
                    yield return featurePreRoll;
                }
            }
        }
    }
}
#nullable disable
