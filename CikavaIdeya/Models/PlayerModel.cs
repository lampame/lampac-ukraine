using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CikavaIdeya.Models
{
    public class CikavaIdeyaPlayerModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("qualities")]
        public List<(string link, string quality)> Qualities { get; set; }
        
        [JsonPropertyName("subtitles")]
        public Shared.Models.Templates.SubtitleTpl? Subtitles { get; set; }
    }
}