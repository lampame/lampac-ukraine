using System;
using System.Collections.Generic;

namespace LME.Uaflix.Models
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
        
        /// <summary>
        /// Всі zetvideo iframe URL на сторінці епізоду (для створення кількох перекладів)
        /// Перший елемент відповідає iframeUrl, наступні — додаткові плеєри (напр. з субтитрами)
        /// </summary>
        public List<string> zetvideoIframeUrls { get; set; }

        /// <summary>
        /// Епізод позначено як «Прем'єра» (ще не вийшов) на сторінці сезону.
        /// У таких епізодів у vi-desc → vi-title зазначено "Прем'єра. ДД.ММ.РРРР".
        /// ProbeSeasonPlayer не робитиме зайвого HTTP-запиту для таких епізодів.
        /// </summary>
        public bool IsPremiere { get; set; }
    }
}