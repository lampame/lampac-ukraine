using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnimeON.Models
{
    public class Serial
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("title_ua")]
        public string TitleUa { get; set; }
        
        [JsonPropertyName("title_en")]
        public string TitleEn { get; set; }
        
        [JsonPropertyName("year")]
        public string Year { get; set; }
        
        [JsonPropertyName("imdb_id")]
        public string ImdbId { get; set; }
        
        [JsonPropertyName("season")]
        public int Season { get; set; }
        
        [JsonPropertyName("voices")]
        public List<Voice> Voices { get; set; }
    }
}