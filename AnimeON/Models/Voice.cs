using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnimeON.Models
{
    public class Voice
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("players")]
        public List<VoicePlayer> Players { get; set; }
    }
    
    public class VoicePlayer
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}