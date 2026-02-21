using System.Collections.Generic;
using Shared.Models.Templates;

namespace Uaflix.Models
{
    public class PlayResult
    {
        public string ashdi_url { get; set; }
        public List<PlayStream> streams { get; set; }
        public SubtitleTpl? subtitles { get; set; }
    }

    public class PlayStream
    {
        public string link { get; set; }
        public string quality { get; set; }
        public string title { get; set; }
    }
}
