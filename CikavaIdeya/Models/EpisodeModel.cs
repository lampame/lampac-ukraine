using System.Text.Json.Serialization;

namespace CikavaIdeya.Models
{
    public class EpisodeModel
    {
        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}