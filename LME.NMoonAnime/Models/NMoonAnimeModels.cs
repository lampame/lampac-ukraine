using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LME.NMoonAnime.Models
{
    // ==================== Моделі для haglund API ====================

    /// <summary>
    /// Відповідь haglund API (/api/v2/imdb?id={imdb_id})
    /// myanimelist — int, не string
    /// </summary>
    public class HaglundIdMapping
    {
        [JsonPropertyName("myanimelist")]
        public int? MyAnimeList { get; set; }

        [JsonPropertyName("themoviedb")]
        public int? TheMovieDb { get; set; }

        [JsonPropertyName("imdb")]
        public string Imdb { get; set; }

        [JsonPropertyName("media")]
        public string Media { get; set; }
    }

    // ==================== Моделі для moonanime API v7.0 ====================

    /// <summary>
    /// Обгортка відповіді /api/7.0/anime/search: {"data": [...], "count": N}
    /// </summary>
    public class MoonAnimeSearchResponse
    {
        [JsonPropertyName("data")]
        public List<MoonAnimeSearchResult> Data { get; set; } = new();

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    /// <summary>
    /// Результат пошуку moonanime API v7.0
    /// title — об'єкт з мовами: {"ua": "...", "en": "...", "ja": "..."}
    /// </summary>
    public class MoonAnimeSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("display_title")]
        public string DisplayTitle { get; set; }

        [JsonPropertyName("title")]
        public MoonAnimeTitle Titles { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("episodes")]
        public int Episodes { get; set; }

        /// <summary>Оцінка відповідності запиту (обчислюється фільтром)</summary>
        [JsonIgnore]
        public int MatchScore { get; set; }

        /// <summary>Отримати назву для пошуку</summary>
        [JsonIgnore]
        public string BestTitle => Titles?.En ?? Titles?.Ja ?? Titles?.Ua ?? DisplayTitle;
    }

    /// <summary>
    /// Назви тайтлу різними мовами
    /// </summary>
    public class MoonAnimeTitle
    {
        [JsonPropertyName("ua")]
        public string Ua { get; set; }

        [JsonPropertyName("en")]
        public string En { get; set; }

        [JsonPropertyName("ja")]
        public string Ja { get; set; }
    }

    // ==================== Моделі для moonanime API v6.0 ====================

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
        public List<NMoonAnimeSeasonRef> Seasons { get; set; } = new();
    }

    // ==================== Існуючі моделі ====================

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
