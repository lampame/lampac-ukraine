using System.Collections.Generic;

namespace AshdiBase.Models
{
    public class VoiceInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string PlayerType { get; set; }
        public Dictionary<int, List<EpisodeInfo>> Seasons { get; set; }

        public VoiceInfo()
        {
            Seasons = new Dictionary<int, List<EpisodeInfo>>();
        }
    }

    public class EpisodeInfo
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public string File { get; set; }
        public string Id { get; set; }
        public string Poster { get; set; }
        public string Subtitle { get; set; }
    }
}
