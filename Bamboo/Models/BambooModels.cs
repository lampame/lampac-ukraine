using System.Collections.Generic;

namespace Bamboo.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Poster { get; set; }
    }

    public class EpisodeInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public int? Episode { get; set; }
    }

    public class StreamInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
    }

    public class SeriesEpisodes
    {
        public List<EpisodeInfo> Sub { get; set; } = new();
        public List<EpisodeInfo> Dub { get; set; } = new();
    }
}
