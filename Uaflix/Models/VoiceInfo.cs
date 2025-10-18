using System.Collections.Generic;

namespace Uaflix.Models
{
    /// <summary>
    /// Модель для зберігання інформації про озвучку серіалу
    /// </summary>
    public class VoiceInfo
    {
        /// <summary>
        /// Назва озвучки без префіксу (наприклад, "DniproFilm")
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Тип плеєра: "ashdi-serial", "zetvideo-serial", "zetvideo-vod", "ashdi-vod"
        /// </summary>
        public string PlayerType { get; set; }
        
        /// <summary>
        /// Назва для відображення з префіксом плеєра (наприклад, "[Ashdi] DniproFilm")
        /// </summary>
        public string DisplayName { get; set; }
        
        /// <summary>
        /// Словник сезонів: ключ - номер сезону, значення - список епізодів
        /// </summary>
        public Dictionary<int, List<EpisodeInfo>> Seasons { get; set; }
        
        public VoiceInfo()
        {
            Seasons = new Dictionary<int, List<EpisodeInfo>>();
        }
    }
    
    /// <summary>
    /// Модель для зберігання інформації про окремий епізод
    /// </summary>
    public class EpisodeInfo
    {
        /// <summary>
        /// Номер епізоду
        /// </summary>
        public int Number { get; set; }
        
        /// <summary>
        /// Назва епізоду
        /// </summary>
        public string Title { get; set; }
        
        /// <summary>
        /// Пряме посилання на відео файл (m3u8)
        /// </summary>
        public string File { get; set; }
        
        /// <summary>
        /// ID епізоду у плеєрі
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// URL постера епізоду
        /// </summary>
        public string Poster { get; set; }
        
        /// <summary>
        /// Субтитри у форматі Playerjs
        /// </summary>
        public string Subtitle { get; set; }
    }
}