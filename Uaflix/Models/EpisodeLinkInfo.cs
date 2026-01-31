using System;

namespace Uaflix.Models
{
    public class EpisodeLinkInfo
    {
        public string url { get; set; }
        public string title { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
        
        // Нові поля для підтримки змішаних плеєрів
        public string playerType { get; set; } // "ashdi-serial", "zetvideo-serial", "zetvideo-vod", "ashdi-vod"
        public string iframeUrl { get; set; }  // URL iframe для цього епізоду
    }
}