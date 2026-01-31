using System;
using System.Collections.Generic;

namespace CikavaIdeya.Models
{
    public class EpisodeLinkInfo
    {
        public string url { get; set; }
        public string title { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
    }

    public class PlayResult
    {
        public string iframe_url { get; set; }
        public List<(string link, string quality)> streams { get; set; }
    }
}