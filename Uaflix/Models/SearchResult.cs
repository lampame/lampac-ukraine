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
    }
}