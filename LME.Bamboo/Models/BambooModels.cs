using System;
using System.Collections.Generic;

namespace LME.Bamboo.Models
{
    /// <summary>
    /// Картка результату пошуку BambooUA (article.swiper-slide).
    /// </summary>
    public class SearchResult
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Poster { get; set; }

        /// <summary>Тип контенту: "series" / "movie" / "" (невідомо).</summary>
        public string Type { get; set; }

        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>Кількість епізодів з блоку date-anounce ("Суб. 16"), 0 — невідомо.</summary>
        public int EpisodesHint { get; set; }

        /// <summary>true — це точно серіал (лічильник епізодів у картці).</summary>
        public bool IsSeriesHint => EpisodesHint > 0;

        public bool IsMovieHint => !IsSeriesHint && string.Equals(Type, "movie", StringComparison.Ordinal);

        public bool IsKnownType => IsSeriesHint || IsMovieHint;
    }

    /// <summary>
    /// Один епізод із Playerjs-плейлиста (нативний плеєр або ashdi).
    /// </summary>
    public class EpisodeInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public int Season { get; set; } = 1;
        public int? Episode { get; set; }

        /// <summary>Назва озвучки (title верхнього рівня плейлиста).</summary>
        public string Voice { get; set; }

        /// <summary>Сирий рядок subtitle — розбирається через ApnHelper.ParseSubtitles.</summary>
        public string Subtitle { get; set; }
    }

    /// <summary>
    /// Голос + його епізоди (одна вкладка плеєра).
    /// </summary>
    public class PlaylistVoice
    {
        /// <summary>Слаг голосу для параметра t (стійкий між запитами).</summary>
        public string Key { get; set; }
        public string Name { get; set; }
        public List<EpisodeInfo> Episodes { get; set; } = new List<EpisodeInfo>();
    }

    /// <summary>
    /// Епізоди серіалу, згруповані за озвучками.
    /// </summary>
    public class SeriesEpisodes
    {
        public List<PlaylistVoice> Voices { get; set; } = new List<PlaylistVoice>();
    }

    /// <summary>
    /// Потік фільму.
    /// </summary>
    public class StreamInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Voice { get; set; }
        public string Subtitle { get; set; }
    }

    /// <summary>
    /// Метадані сторінки (ld+json + og:image) — для SimilarTpl.
    /// </summary>
    public class BambooDetails
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Poster { get; set; }
        public int? Year { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }
}
