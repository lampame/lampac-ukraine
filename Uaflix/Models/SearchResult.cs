using System;
using System.Collections.Generic;

namespace Uaflix.Models
{
    public class SearchResult
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public int Year { get; set; }
        public string PosterUrl { get; set; }
        public string Category { get; set; }
        public bool IsAnime { get; set; }
        public int MatchScore { get; set; }
        public bool TitleMatched { get; set; }
        public bool YearMatched { get; set; }
    }
}
