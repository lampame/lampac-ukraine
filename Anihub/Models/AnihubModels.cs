using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace Anihub.Models
{
    public class AnihubSearchResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("next")]
        public object? Next { get; set; }

        [JsonPropertyName("previous")]
        public object? Previous { get; set; }

        [JsonPropertyName("results")]
        public List<AnihubResult> Results { get; set; } = new();

        public bool IsEmpty => Results == null || Results.Count == 0;
        public List<AnihubResult> content => Results ?? new List<AnihubResult>();
    }

    public class AnihubResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("title_ukrainian")]
        public string TitleUkrainian { get; set; } = string.Empty;

        [JsonPropertyName("title_english")]
        public string TitleEnglish { get; set; } = string.Empty;

        [JsonPropertyName("title_original")]
        public string TitleOriginal { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("poster_url")]
        public string PosterUrl { get; set; } = string.Empty;

        [JsonPropertyName("banner_url")]
        public string BannerUrl { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("episodes_count")]
        public int EpisodesCount { get; set; }

        [JsonPropertyName("rating")]
        public double Rating { get; set; }

        [JsonPropertyName("genres")]
        public List<AnihubGenre> Genres { get; set; } = new();

        [JsonPropertyName("is_recommended_by_community")]
        public bool IsRecommendedByCommunity { get; set; }

        [JsonPropertyName("is_nsfw")]
        public bool IsNsfw { get; set; }

        [JsonPropertyName("has_ukrainian_dub")]
        public bool HasUkrainianDub { get; set; }
    }

    public class AnihubGenre
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("name_ukrainian")]
        public string NameUkrainian { get; set; } = string.Empty;
    }

    public class AnihubEpisodeSourcesResponse
    {
        [JsonPropertyName("moonanime")]
        public List<MoonanimeSource> Moonanime { get; set; } = new();

        [JsonPropertyName("ashdi")]
        public List<AshdiSource> Ashdi { get; set; } = new();

        [JsonPropertyName("statistics")]
        public AnihubStatistics Statistics { get; set; } = new();
        
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    public class MoonanimeSource
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("studio_name")]
        public string StudioName { get; set; } = string.Empty;

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("episodes")]
        public List<MoonanimeEpisode> Episodes { get; set; } = new();

        [JsonPropertyName("episodes_count")]
        public int EpisodesCount { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public class MoonanimeEpisode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("title_en")]
        public string TitleEn { get; set; } = string.Empty;

        [JsonPropertyName("title_jp")]
        public string TitleJp { get; set; } = string.Empty;

        [JsonPropertyName("iframe_link")]
        public string IframeLink { get; set; } = string.Empty;

        [JsonPropertyName("vod_link")]
        public string VodLink { get; set; } = string.Empty;

        [JsonPropertyName("poster_url")]
        public string PosterUrl { get; set; } = string.Empty;

        [JsonPropertyName("episode_type")]
        public string EpisodeType { get; set; } = string.Empty;

        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; } = string.Empty;

        [JsonPropertyName("embed_url")]
        public EmbedUrl EmbedUrl { get; set; } = new();

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public class EmbedUrl
    {
        [JsonPropertyName("iframe_url")]
        public string IframeUrl { get; set; } = string.Empty;

        [JsonPropertyName("iframe_code")]
        public string IframeCode { get; set; } = string.Empty;

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    public class AshdiSource
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("studio_name")]
        public string StudioName { get; set; } = string.Empty;

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("episodes_data")]
        public List<AshdiEpisodeData> EpisodesData { get; set; } = new();

        [JsonPropertyName("episode_urls")]
        public List<AshdiEpisodeUrl> EpisodeUrls { get; set; } = new();

        [JsonPropertyName("episodes_count")]
        public int EpisodesCount { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public class AshdiEpisodeData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("vod_url")]
        public string VodUrl { get; set; } = string.Empty;

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }
    }

    public class AshdiEpisodeUrl
    {
        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("ashdi_episode_id")]
        public string AshdiEpisodeId { get; set; } = string.Empty;
    }

    public class AnihubStatistics
    {
        [JsonPropertyName("anime_title")]
        public string AnimeTitle { get; set; } = string.Empty;

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("moonanime")]
        public MoonanimeStats Moonanime { get; set; } = new();

        [JsonPropertyName("ashdi")]
        public AshdiStats Ashdi { get; set; } = new();

        [JsonPropertyName("total_unique_studios")]
        public int TotalUniqueStudios { get; set; }
    }

    public class MoonanimeStats
    {
        [JsonPropertyName("total_records")]
        public int TotalRecords { get; set; }

        [JsonPropertyName("unique_count")]
        public int UniqueCount { get; set; }

        [JsonPropertyName("unique_names")]
        public List<string> UniqueNames { get; set; } = new();

        [JsonPropertyName("details")]
        public List<MoonanimeDetail> Details { get; set; } = new();
    }

    public class MoonanimeDetail
    {
        [JsonPropertyName("original_name")]
        public string OriginalName { get; set; } = string.Empty;

        [JsonPropertyName("normalized_name")]
        public string NormalizedName { get; set; } = string.Empty;

        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("moon_id")]
        public int MoonId { get; set; }

        [JsonPropertyName("episodes_count")]
        public int EpisodesCount { get; set; }
    }

    public class AshdiStats
    {
        [JsonPropertyName("total_records")]
        public int TotalRecords { get; set; }

        [JsonPropertyName("unique_count")]
        public int UniqueCount { get; set; }

        [JsonPropertyName("unique_names")]
        public List<string> UniqueNames { get; set; } = new();

        [JsonPropertyName("details")]
        public List<AshdiDetail> Details { get; set; } = new();
    }

    public class AshdiDetail
    {
        [JsonPropertyName("original_name")]
        public string OriginalName { get; set; } = string.Empty;

        [JsonPropertyName("normalized_name")]
        public string NormalizedName { get; set; } = string.Empty;

        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("episodes_count")]
        public int EpisodesCount { get; set; }
    }
}
