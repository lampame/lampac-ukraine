using System.Collections.Generic;
using System.Text.Json;

namespace LME.Petlura.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Poster { get; set; }
    }

    public class StreamInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Quality { get; set; }
        public List<SubtitleInfo> Subtitles { get; set; }
    }

    public class SubtitleInfo
    {
        public string Lang { get; set; }
        public string Url { get; set; }
    }

    public class EpisodeInfo
    {
        public int? Episode { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
    }

    public class VoiceEpisodes
    {
        public string Name { get; set; }
        public List<EpisodeInfo> Episodes { get; set; } = new();
    }

    public class SerialInfo
    {
        public List<VoiceEpisodes> Voices { get; set; } = new();
    }

    public class PlayerFileItem
    {
        public string file { get; set; }
        public string title { get; set; }
        public string subtitle { get; set; }
        public JsonElement? folder { get; set; }
        public string id { get; set; }
        public string poster { get; set; }
    }
}
