using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CikavaIdeya.Models
{
    public class SeasonModel
    {
        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }
        
        [JsonPropertyName("episodes")]
        public List<EpisodeModel> Episodes { get; set; }
    }
}