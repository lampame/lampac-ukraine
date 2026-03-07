using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MoonAnime.Models
{
    public class MoonAnimeSearchResponse
    {
        [JsonPropertyName("seasons")]
        public List<MoonAnimeSeasonRef> Seasons { get; set; } = new();
    }

    public class MoonAnimeSeasonRef
    {
        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class MoonAnimeSeasonContent
    {
        public int SeasonNumber { get; set; }

        public string Url { get; set; }

        public bool IsSeries { get; set; }

        public List<MoonAnimeVoiceContent> Voices { get; set; } = new();
    }

    public class MoonAnimeVoiceContent
    {
        public string Name { get; set; }

        public string MovieFile { get; set; }

        public List<MoonAnimeEpisodeContent> Episodes { get; set; } = new();
    }

    public class MoonAnimeEpisodeContent
    {
        public string Name { get; set; }

        public int Number { get; set; }

        public string File { get; set; }
    }

    public class MoonAnimeStreamVariant
    {
        public string Url { get; set; }

        public string Quality { get; set; }
    }
}
