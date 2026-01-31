using System.Collections.Generic;

namespace StarLight.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Type { get; set; }
        public string Href { get; set; }
        public string Channel { get; set; }
        public string Project { get; set; }
    }

    public class SeasonInfo
    {
        public string Title { get; set; }
        public string Slug { get; set; }
    }

    public class EpisodeInfo
    {
        public string Title { get; set; }
        public string Hash { get; set; }
        public string VideoSlug { get; set; }
        public string Date { get; set; }
        public string SeasonSlug { get; set; }
        public int? Number { get; set; }
    }

    public class ProjectInfo
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Poster { get; set; }
        public string Hash { get; set; }
        public string Type { get; set; }
        public string Channel { get; set; }
        public List<SeasonInfo> Seasons { get; set; } = new();
        public List<EpisodeInfo> Episodes { get; set; } = new();
    }

    public class StreamResult
    {
        public string Stream { get; set; }
        public string Poster { get; set; }
        public string Name { get; set; }
        public List<(string link, string quality)> Streams { get; set; }
    }
}
