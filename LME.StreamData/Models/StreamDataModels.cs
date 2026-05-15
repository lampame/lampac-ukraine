using System.Collections.Generic;

namespace LME.StreamData.Models
{
    public class SubtitleItem
    {
        public string lang { get; set; }
        public string code { get; set; }
        public string url { get; set; }
    }

    public class StreamDataResponse
    {
        public string status_code { get; set; }
        public StreamDataInfo data { get; set; }
        public List<SubtitleItem> default_subs { get; set; }
    }

    public class StreamDataInfo
    {
        public string title { get; set; }
        public string imdb_id { get; set; }
        public int season { get; set; }
        public string episode { get; set; }
        public Dictionary<string, List<string>> eps { get; set; }
        public string file_name { get; set; }
        public string backdrop { get; set; }
        public List<string> stream_urls { get; set; }
        public string thumbnails_url { get; set; }
    }
}
