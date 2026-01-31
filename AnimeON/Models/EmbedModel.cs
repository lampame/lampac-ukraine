using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnimeON.Models
{
    public class EmbedModel
    {
        [JsonPropertyName("translation")]
        public string Translation { get; set; }
        
        [JsonPropertyName("links")]
        public List<(string link, string quality)> Links { get; set; }
        
        [JsonPropertyName("subtitles")]
        public Shared.Models.Templates.SubtitleTpl? Subtitles { get; set; }
        
        [JsonPropertyName("season")]
        public int Season { get; set; }
        
        [JsonPropertyName("episode")]
        public int Episode { get; set; }
    }
}