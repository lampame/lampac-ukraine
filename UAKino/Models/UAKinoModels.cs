using System.Collections.Generic;

namespace UAKino.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Poster { get; set; }
        public string Season { get; set; }
    }

    public class PlaylistItem
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Voice { get; set; }
    }

    public class SubtitleInfo
    {
        public string Lang { get; set; }
        public string Url { get; set; }
    }

    public class PlayerResult
    {
        public string File { get; set; }
        public List<SubtitleInfo> Subtitles { get; set; } = new();
    }
}
