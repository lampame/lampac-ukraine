using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Unimay.Models
{
    public class ReleaseResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("year")]
        public string Year { get; set; }
        
        [JsonPropertyName("type")]
        public string Type { get; set; } // "Фільм" або "Телесеріал"
        
        [JsonPropertyName("playlist")]
        public List<Episode> Playlist { get; set; }
    }
}