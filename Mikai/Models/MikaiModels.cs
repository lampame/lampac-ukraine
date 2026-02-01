using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mikai.Models
{
    public class SearchResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("result")]
        public List<MikaiAnime> Result { get; set; }
    }

    public class DetailResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("result")]
        public MikaiAnime Result { get; set; }
    }

    public class MikaiAnime
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("season")]
        public string Season { get; set; }

        [JsonPropertyName("year")]
        public int Year { get; set; }

        [JsonPropertyName("startDate")]
        public string StartDate { get; set; }

        [JsonPropertyName("isAdult")]
        public bool IsAdult { get; set; }

        [JsonPropertyName("isDisabled")]
        public bool IsDisabled { get; set; }

        [JsonPropertyName("media")]
        public MikaiMedia Media { get; set; }

        [JsonPropertyName("details")]
        public MikaiDetails Details { get; set; }

        [JsonPropertyName("players")]
        public List<MikaiPlayer> Players { get; set; }

        [JsonPropertyName("relations")]
        public List<MikaiRelation> Relations { get; set; }
    }

    public class MikaiMedia
    {
        [JsonPropertyName("posterUid")]
        public string PosterUid { get; set; }

        [JsonPropertyName("bannerUid")]
        public string BannerUid { get; set; }

        [JsonPropertyName("youtubeId")]
        public string YoutubeId { get; set; }
    }

    public class MikaiDetails
    {
        [JsonPropertyName("source")]
        public string Source { get; set; }

        [JsonPropertyName("genres")]
        public List<MikaiGenre> Genres { get; set; }

        [JsonPropertyName("episodesInformation")]
        public MikaiEpisodesInfo EpisodesInformation { get; set; }

        [JsonPropertyName("names")]
        public MikaiNames Names { get; set; }

        [JsonPropertyName("rating")]
        public MikaiRating Rating { get; set; }

        [JsonPropertyName("views")]
        public int Views { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    public class MikaiNames
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("nameNative")]
        public string NameNative { get; set; }

        [JsonPropertyName("nameEnglish")]
        public string NameEnglish { get; set; }
    }

    public class MikaiGenre
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("ukrainian")]
        public string Ukrainian { get; set; }
    }

    public class MikaiEpisodesInfo
    {
        [JsonPropertyName("durationMin")]
        public int DurationMin { get; set; }

        [JsonPropertyName("episodes")]
        public int Episodes { get; set; }

        [JsonPropertyName("episodesAired")]
        public int EpisodesAired { get; set; }

        [JsonPropertyName("episodesAdapted")]
        public int EpisodesAdapted { get; set; }
    }

    public class MikaiRating
    {
        [JsonPropertyName("rating")]
        public double Rating { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class MikaiPlayer
    {
        [JsonPropertyName("team")]
        public MikaiTeam Team { get; set; }

        [JsonPropertyName("isSubs")]
        public bool IsSubs { get; set; }

        [JsonPropertyName("providers")]
        public List<MikaiProvider> Providers { get; set; }
    }

    public class MikaiTeam
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("isGroup")]
        public bool IsGroup { get; set; }

        [JsonPropertyName("teams")]
        public List<MikaiTeamInfo> Teams { get; set; }
    }

    public class MikaiTeamInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("avatarUid")]
        public string AvatarUid { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }
    }

    public class MikaiProvider
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("episodes")]
        public List<MikaiProviderEpisode> Episodes { get; set; }
    }

    public class MikaiProviderEpisode
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("playLink")]
        public string PlayLink { get; set; }
    }

    public class MikaiRelation
    {
        [JsonPropertyName("relationType")]
        public string RelationType { get; set; }

        [JsonPropertyName("anime")]
        public MikaiRelationAnime Anime { get; set; }
    }

    public class MikaiRelationAnime
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("media")]
        public MikaiMedia Media { get; set; }

        [JsonPropertyName("details")]
        public MikaiDetails Details { get; set; }
    }
}
