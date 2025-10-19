using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnimeON.Models
{
    public class SearchResponseModel
    {
        [JsonPropertyName("result")]
        public List<SearchModel> Result { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class SearchModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("titleUa")]
        public string TitleUa { get; set; }

        [JsonPropertyName("titleEn")]
        public string TitleEn { get; set; }

        [JsonPropertyName("releaseDate")]
        public string Year { get; set; }
        
        [JsonPropertyName("imdbId")]
        public string ImdbId { get; set; }

        [JsonPropertyName("season")]
        public int Season { get; set; }
    }

    public class FundubsResponseModel
    {
        [JsonPropertyName("funDubs")]
        public List<FundubModel> FunDubs { get; set; }
    }

    public class FundubModel
    {
        [JsonPropertyName("fundub")]
        public Fundub Fundub { get; set; }

        [JsonPropertyName("player")]
        public List<Player> Player { get; set; }
    }

    public class Fundub
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Player
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    public class EpisodeModel
    {
        [JsonPropertyName("episodes")]
        public List<Episode> Episodes { get; set; }

        [JsonPropertyName("anotherPlayer")]
        public System.Text.Json.JsonElement AnotherPlayer { get; set; }
    }

    public class Episode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("episode")]
        public int EpisodeNum { get; set; }

        [JsonPropertyName("fileUrl")]
        public string Hls { get; set; }

        [JsonPropertyName("videoUrl")]
        public string VideoUrl { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class Movie
    {
        public string translation { get; set; }
        public List<(string link, string quality)> links { get; set; }
        public Shared.Models.Templates.SubtitleTpl? subtitles { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
    }

    public class Result
    {
        public List<Movie> movie { get; set; }
    }
}