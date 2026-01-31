using System.Collections.Generic;
using Shared.Models.Templates;

namespace Uaflix.Models
{
    public class PlayResult
    {
        public string ashdi_url { get; set; }
        public List<(string link, string quality)> streams { get; set; }
        public SubtitleTpl? subtitles { get; set; }
    }
}