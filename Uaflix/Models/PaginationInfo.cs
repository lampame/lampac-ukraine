using System;
using System.Collections.Generic;

namespace Uaflix.Models
{
    public class PaginationInfo
    {
        // Словник сезонів, де ключ - номер сезону, значення - кількість сторінок
        public Dictionary<int, int> Seasons { get; set; } = new Dictionary<int, int>();
        
        // Загальна кількість сторінок (якщо потрібно)
        public int TotalPages { get; set; }
        
        // URL сторінки серіалу (базовий URL для пагінації)
        public string SerialUrl { get; set; }
        
        public List<EpisodeLinkInfo> Episodes { get; set; } = new List<EpisodeLinkInfo>();
    }
}