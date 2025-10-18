using System.Collections.Generic;

namespace Uaflix.Models
{
    /// <summary>
    /// Агрегована структура серіалу з озвучками з усіх джерел (ashdi, zetvideo-serial, zetvideo-vod, ashdi-vod)
    /// </summary>
    public class SerialAggregatedStructure
    {
        /// <summary>
        /// URL головної сторінки серіалу
        /// </summary>
        public string SerialUrl { get; set; }
        
        /// <summary>
        /// Словник озвучок: ключ - displayName озвучки (наприклад, "[Ashdi] DniproFilm"), значення - VoiceInfo
        /// </summary>
        public Dictionary<string, VoiceInfo> Voices { get; set; }
        
        /// <summary>
        /// Список всіх епізодів серіалу (використовується для zetvideo-vod)
        /// </summary>
        public List<EpisodeLinkInfo> AllEpisodes { get; set; }
        
        public SerialAggregatedStructure()
        {
            Voices = new Dictionary<string, VoiceInfo>();
            AllEpisodes = new List<EpisodeLinkInfo>();
        }
    }
}