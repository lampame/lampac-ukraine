using System.Collections.Generic;

namespace LME.Petlura.Models
{
    /// <summary>
    /// Топ-рівень JSON: file:'[...]' — масив сезонів
    /// </summary>
    public class HdvbSeason
    {
        public string title { get; set; }
        public List<HdvbVoice> folder { get; set; }
    }

    /// <summary>
    /// Другий рівень: озвучка всередині сезону
    /// </summary>
    public class HdvbVoice
    {
        public string title { get; set; }
        public List<HdvbEpisode> folder { get; set; }
    }

    /// <summary>
    /// Третій рівень: епізод з посиланням на стрім
    /// </summary>
    public class HdvbEpisode
    {
        public string title { get; set; }
        public string file { get; set; }
        public string id { get; set; }
        public string poster { get; set; }
        public string subtitle { get; set; }
    }
}
