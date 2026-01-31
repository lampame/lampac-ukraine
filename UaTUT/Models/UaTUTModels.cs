using Newtonsoft.Json;
using System.Collections.Generic;

namespace UaTUT.Models
{
    public class SearchResult
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("imdb_id")]
        public string ImdbId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("title_alt")]
        public string TitleAlt { get; set; }

        [JsonProperty("title_en")]
        public string TitleEn { get; set; }

        [JsonProperty("title_ru")]
        public string TitleRu { get; set; }

        [JsonProperty("year")]
        public string Year { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }
    }

    public class PlayerData
    {
        public string File { get; set; }
        public string Poster { get; set; }
        public List<Voice> Voices { get; set; }
        public List<Season> Seasons { get; set; } // Залишаємо для зворотної сумісності
    }

    public class Voice
    {
        public string Name { get; set; }
        public List<Season> Seasons { get; set; }
    }

    public class Season
    {
        public string Title { get; set; }
        public List<Episode> Episodes { get; set; }
    }

    public class Episode
    {
        public string Title { get; set; }
        public string File { get; set; }
        public string Id { get; set; }
        public string Poster { get; set; }
        public string Subtitle { get; set; }
    }
}
