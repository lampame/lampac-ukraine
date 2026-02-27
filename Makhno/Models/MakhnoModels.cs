using System.Collections.Generic;

namespace Makhno.Models
{
    public class PlayerData
    {
        public string File { get; set; }
        public string Poster { get; set; }
        public List<Voice> Voices { get; set; }
        public List<Season> Seasons { get; set; }
        public List<MovieVariant> Movies { get; set; }
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

    public class MovieVariant
    {
        public string Title { get; set; }
        public string File { get; set; }
        public string Quality { get; set; }
    }
}
