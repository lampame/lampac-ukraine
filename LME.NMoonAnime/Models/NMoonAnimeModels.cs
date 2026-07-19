using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LME.NMoonAnime.Models
{
    // ==================== Моделі для пошуку ====================

    /// <summary>
    /// Відповідь haglund API (/api/v2/imdb?id={imdb_id})
    /// </summary>
    public class HaglundIdMapping
    {
        [JsonPropertyName("myanimelist")]
        public string MyAnimeList { get; set; }

        [JsonPropertyName("themoviedb")]
        public string TheMovieDb { get; set; }

        [JsonPropertyName("imdb")]
        public string Imdb { get; set; }

        [JsonPropertyName("media")]
        public string Media { get; set; }
    }

    /// <summary>
    /// Результат пошуку moonanime API v7.0 (/api/7.0/anime/search)
    /// </summary>
    public class MoonAnimeSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("mal_id")]
        public int MalId { get; set; }

        /// <summary>Оцінка відповідності запиту (обчислюється фільтром)</summary>
        [JsonIgnore]
        public int MatchScore { get; set; }
    }

    /// <summary>
    /// Відповідь moonanime API v6.0 (/api/6.0/title/by/mal_id/{id})
    /// </summary>
    public class MoonAnimeTitleResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("seasons")]
        public List<MoonAnimeSeasonRef> Seasons { get; set; } = new();
    }

    // ==================== Існуючі моделі ====================

    /// <summary>
    /// Відповідь від APX API проксі (залишається для зворотної сумісності)
    /// </summary>
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
