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
        [JsonPropertyName("translations")]
        public List<TranslationModel> Translations { get; set; }
    }

    public class TranslationModel
    {
        [JsonPropertyName("translation")]
        public Fundub Translation { get; set; }

        [JsonPropertyName("player")]
        public List<Player> Player { get; set; }
    }

    public class Fundub
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("isSub")]
        public bool IsSub { get; set; }

        [JsonPropertyName("synonyms")]
        public List<string> Synonyms { get; set; }

        [JsonPropertyName("studios")]
        public List<Studio> Studios { get; set; }
    }

    public class Studio
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("team")]
        public object Team { get; set; }

        [JsonPropertyName("telegram")]
        public string Telegram { get; set; }

        [JsonPropertyName("youtube")]
        public string Youtube { get; set; }

        [JsonPropertyName("patreon")]
        public string Patreon { get; set; }

        [JsonPropertyName("buymeacoffee")]
        public string BuyMeACoffee { get; set; }

        [JsonPropertyName("avatar")]
        public Avatar Avatar { get; set; }
    }

    public class Avatar
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("original")]
        public string Original { get; set; }

        [JsonPropertyName("preview")]
        public string Preview { get; set; }
    }

    public class Player
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("episodesCount")]
        public int EpisodesCount { get; set; }
    }

    public class FundubModel
    {
        public Fundub Fundub { get; set; }
        public List<Player> Player { get; set; }
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