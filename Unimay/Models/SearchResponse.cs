using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Unimay.Models
{
    public class SearchResponse
    {
        [JsonPropertyName("content")]
        public List<ReleaseInfo> Content { get; set; }
        
        [JsonPropertyName("totalElements")]
        public int TotalElements { get; set; }
    }
    
    public class ReleaseInfo
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("year")]
        public string Year { get; set; }
        
        [JsonPropertyName("type")]
        public string Type { get; set; } // "Фільм" або "Телесеріал"
        
        [JsonPropertyName("names")]
        public Names Names { get; set; }
    }
    
    public class Names
    {
        [JsonPropertyName("ukr")]
        public string Ukr { get; set; }
        
        [JsonPropertyName("eng")]
        public string Eng { get; set; }
    }
}