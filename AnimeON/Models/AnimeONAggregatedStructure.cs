using System.Collections.Generic;

namespace AnimeON.Models
{
    /// <summary> Aggregated structure for AnimeON serial content to match Lampac standard navigation.</summary>
    public class AnimeONAggregatedStructure
    {
        /// <summary>Anime identifier from AnimeON API.</summary>
        public int AnimeId { get; set; }

        /// <summary>Season number.</summary>
        public int Season { get; set; }

        /// <summary>Voices mapped by display key e.g. "[Moon] AniUA".</summary>
        public Dictionary<string, AnimeONVoiceInfo> Voices { get; set; } = new Dictionary<string, AnimeONVoiceInfo>();
    }

    /// <summary>Voice information for a specific player/studio combination within a season.</summary>
    public class AnimeONVoiceInfo
    {
        /// <summary>Studio/voice name (e.g., AniUA).</summary>
        public string Name { get; set; }

        /// <summary>Player type ("moon" or "ashdi").</summary>
        public string PlayerType { get; set; }

        /// <summary>Display name (e.g., "[Moon] AniUA").</summary>
        public string DisplayName { get; set; }

        /// <summary>Player identifier from API.</summary>
        public int PlayerId { get; set; }

        /// <summary>Fundub identifier from API.</summary>
        public int FundubId { get; set; }

        /// <summary>Flat list of episodes for the selected season.</summary>
        public List<AnimeONEpisodeInfo> Episodes { get; set; } = new List<AnimeONEpisodeInfo>();
    }

    /// <summary>Episode information within a voice.</summary>
    public class AnimeONEpisodeInfo
    {
        /// <summary>Episode number.</summary>
        public int Number { get; set; }

        /// <summary>Episode title.</summary>
        public string Title { get; set; }

        /// <summary>Primary HLS link if available.</summary>
        public string Hls { get; set; }

        /// <summary>Fallback video URL (iframe or direct).</summary>
        public string VideoUrl { get; set; }

        /// <summary>Episode identifier from API.</summary>
        public int EpisodeId { get; set; }
    }
}