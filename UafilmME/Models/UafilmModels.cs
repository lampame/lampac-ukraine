using System.Collections.Generic;

namespace UafilmME.Models
{
    public class UafilmSearchItem
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string OriginalTitle { get; set; }
        public bool IsSeries { get; set; }
        public int Year { get; set; }
        public string ImdbId { get; set; }
        public long TmdbId { get; set; }
        public string Poster { get; set; }
        public int MatchScore { get; set; }
    }

    public class UafilmTitleDetails
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string OriginalTitle { get; set; }
        public bool IsSeries { get; set; }
        public int Year { get; set; }
        public string ImdbId { get; set; }
        public long TmdbId { get; set; }
        public int SeasonsCount { get; set; }
        public long PrimaryVideoId { get; set; }
    }

    public class UafilmSeasonItem
    {
        public long Id { get; set; }
        public int Number { get; set; }
        public int EpisodesCount { get; set; }
    }

    public class UafilmEpisodeItem
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public long PrimaryVideoId { get; set; }
        public string PrimaryVideoName { get; set; }
    }

    public class UafilmVideoItem
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Src { get; set; }
        public string Type { get; set; }
        public string Quality { get; set; }
        public string Origin { get; set; }
        public string Language { get; set; }
        public int? SeasonNum { get; set; }
        public int? EpisodeNum { get; set; }
        public long EpisodeId { get; set; }
    }

    public class UafilmWatchInfo
    {
        public UafilmVideoItem Video { get; set; }
        public List<UafilmVideoItem> AlternativeVideos { get; set; } = new();
    }
}
