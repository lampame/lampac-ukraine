using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NMoonAnime.Models
{
    public class NMoonAnimeSearchResponse
    {
        [JsonPropertyName("seasons")]
        public List<NMoonAnimeSeasonRef> Seasons { get; set; } = new();
    }

    public class NMoonAnimeSeasonRef
    {
        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class NMoonAnimeSeasonContent
    {
        public int SeasonNumber { get; set; }

        public string Url { get; set; }

        public bool IsSeries { get; set; }

        public List<NMoonAnimeVoiceContent> Voices { get; set; } = new();
    }

    public class NMoonAnimeVoiceContent
    {
        public string Name { get; set; }

        public string MovieFile { get; set; }

        public List<NMoonAnimeEpisodeContent> Episodes { get; set; } = new();
    }

    public class NMoonAnimeEpisodeContent
    {
        public string Name { get; set; }

        public int Number { get; set; }

        public string File { get; set; }
    }

    public class NMoonAnimeStreamVariant
    {
        public string Url { get; set; }

        public string Quality { get; set; }
    }
}
