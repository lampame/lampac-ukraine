using System.Collections.Generic;

namespace KlonFUN.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Poster { get; set; }
        public int Year { get; set; }
    }

    public class KlonItem
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Poster { get; set; }
        public string PlayerUrl { get; set; }
        public bool IsSerialPlayer { get; set; }
        public int Year { get; set; }
    }

    public class PlayerVoice
    {
        public string title { get; set; }
        public string file { get; set; }
        public string subtitle { get; set; }
        public string id { get; set; }
        public List<PlayerSeason> folder { get; set; }
    }

    public class PlayerSeason
    {
        public string title { get; set; }
        public List<PlayerEpisode> folder { get; set; }
    }

    public class PlayerEpisode
    {
        public string title { get; set; }
        public string file { get; set; }
        public string subtitle { get; set; }
        public string id { get; set; }
    }

    public class MovieStream
    {
        public string Title { get; set; }
        public string Link { get; set; }
    }

    public class SerialEpisode
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
    }

    public class SerialVoice
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public Dictionary<int, List<SerialEpisode>> Seasons { get; set; } = new();
    }

    public class SerialStructure
    {
        public List<SerialVoice> Voices { get; set; } = new();
    }
}
