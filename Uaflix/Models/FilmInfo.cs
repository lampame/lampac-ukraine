using System;
using System.Collections.Generic;

namespace Uaflix.Models
{
    /// <summary>
    /// Модель для зберігання інформації про фільм
    /// </summary>
    public class FilmInfo
    {
        /// <summary>
        /// URL сторінки фільму
        /// </summary>
        public string Url { get; set; }
        
        /// <summary>
        /// Назва фільму
        /// </summary>
        public string Title { get; set; }
        
        /// <summary>
        /// Рік випуску
        /// </summary>
        public int Year { get; set; }
        
        /// <summary>
        /// Опис фільму
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// Постер фільму
        /// </summary>
        public string PosterUrl { get; set; }
        
        /// <summary>
        /// Список акторів
        /// </summary>
        public List<string> Actors { get; set; } = new List<string>();
        
        /// <summary>
        /// Режисер
        /// </summary>
        public string Director { get; set; }
        
        /// <summary>
        /// Тривалість у секундах
        /// </summary>
        public int Duration { get; set; }
    }
}