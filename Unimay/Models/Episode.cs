using System.Text.Json.Serialization;

namespace Unimay.Models
{
    public class Episode
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("hls")]
        public Hls Hls { get; set; }
    }
    
    public class Hls
    {
        [JsonPropertyName("master")]
        public string Master { get; set; }
    }
}