using System.Collections.Generic;

namespace Mikai.Models
{
    public class MikaiVoiceInfo
    {
        public string DisplayName { get; set; }
        public string ProviderName { get; set; }
        public bool IsSubs { get; set; }
        public Dictionary<int, List<MikaiEpisodeInfo>> Seasons { get; set; } = new();
    }

    public class MikaiEpisodeInfo
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
    }
}
