using System.Collections.Generic;

namespace LME.UAKino.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public string Url { get; set; }
        public string Poster { get; set; }
        public int? Year { get; set; }
        public string NewsId { get; set; }
    }

    public class VoiceGroup
    {
        public string Name { get; set; }
        public string DataId { get; set; }
        public List<EpisodeItem> Episodes { get; set; } = new();
    }

    public class EpisodeItem
    {
        public string Title { get; set; }
        public string FileUrl { get; set; }
        public int? EpisodeNumber { get; set; }
    }
}
