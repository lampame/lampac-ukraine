using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LME.AniWorld.Models
{
    // === API Catalog List Response ===
    
    public class CatalogListResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }
        
        [JsonPropertyName("results")]
        public List<CatalogItem> Results { get; set; }
    }
    
    public class CatalogItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("original_title")]
        public string OriginalTitle { get; set; }
        
        [JsonPropertyName("romanized_title")]
        public string RomanizedTitle { get; set; }
        
        [JsonPropertyName("release_year")]
        public int ReleaseYear { get; set; }
        
        [JsonPropertyName("media_type")]
        public string MediaType { get; set; }
        
        [JsonPropertyName("current_episodes")]
        public int CurrentEpisodes { get; set; }
        
        [JsonPropertyName("total_episodes")]
        public int TotalEpisodes { get; set; }
        
        [JsonPropertyName("rating")]
        public double Rating { get; set; }
        
        [JsonPropertyName("duration_minutes")]
        public int DurationMinutes { get; set; }
        
        [JsonPropertyName("is_archived")]
        public bool IsArchived { get; set; }
    }
    
    // === API Catalog Detail Response ===
    
    public class CatalogDetail
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("original_title")]
        public string OriginalTitle { get; set; }
        
        [JsonPropertyName("release_year")]
        public int ReleaseYear { get; set; }
        
        [JsonPropertyName("media_type")]
        public string MediaType { get; set; }
        
        [JsonPropertyName("episodes")]
        public List<EpisodeDetail> Episodes { get; set; }
    }
    
    public class EpisodeDetail
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }
        
        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        // SourceUrl НЕ отримується тут — отримується при кліку через GetEpisodeSource
    }
    
    // === API Episode Source Response ===
    
    public class EpisodeSourceResponse
    {
        [JsonPropertyName("source_url")]
        public string SourceUrl { get; set; }
        
        [JsonPropertyName("source_type")]
        public string SourceType { get; set; }
    }
    
    // === Internal Models ===
    
    public enum StreamType
    {
        Dailymotion,
        Mediadelivery,
        Unknown
    }
    
    public class EpisodeSource
    {
        public string Url { get; set; }
        public StreamType Type { get; set; }
    }
    
    public class SeasonGroup
    {
        public int SeasonNumber { get; set; }
        public List<EpisodeDetail> Episodes { get; set; } = new();
    }
    
    // === Search Result for SimilarTpl ===
    
    public class AniWorldSearchResult
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public int ReleaseYear { get; set; }
        public string MediaType { get; set; }
        public string PosterUrl { get; set; }
    }
}
