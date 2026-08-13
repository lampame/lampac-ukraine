using Shared.Models.Online.Settings;
using System.Collections.Generic;

namespace LME.Franko.Models
{
    /// <summary>
    /// Конфіг модуля: стандартні OnlinesSettings + специфічні поля Franko (mirrors, api_host, fhost).
    /// </summary>
    public class FrankoConfig : OnlinesSettings
    {
        public FrankoConfig(string plugin, string host, string apihost = null, bool useproxy = false, string token = null, bool enable = true, bool streamproxy = false, bool rip = false, bool forceEncryptToken = false, string rch_access = null, string stream_access = null) : base(plugin, host, apihost, useproxy, token, enable, streamproxy, rip, forceEncryptToken, rch_access, stream_access)
        {
        }

        /// <summary>Список мірорів для consilium search.</summary>
        public string[] mirrors { get; set; }

        /// <summary>Хост player files API (POST /api/player/files).</summary>
        public string api_host { get; set; }

        /// <summary>Хост franko player payload (GET /show/...).</summary>
        public string fhost { get; set; }
    }

    /// <summary>Player payload з window.__PLAYER_PAYLOAD__.</summary>
    public class FrankoPayload
    {
        public int id { get; set; }

        public bool is_serial { get; set; }

        public List<FrankoTranslation> translations { get; set; } = new List<FrankoTranslation>();

        public Dictionary<string, List<int>> seasons_episodes { get; set; } = new Dictionary<string, List<int>>();
    }

    /// <summary>Одна озвучка/переклад з player payload.</summary>
    public class FrankoTranslation
    {
        public int id { get; set; }

        public string title { get; set; }
    }

    /// <summary>Відповідь POST /api/player/files.</summary>
    public class FrankoStreamResponse
    {
        /// <summary>Готовий stream URL (основне джерело істини для player).</summary>
        public string file { get; set; }

        public List<FrankoSource> sources { get; set; }

        public List<object> subtitles { get; set; }
    }

    public class FrankoSource
    {
        public string src { get; set; }

        public string type { get; set; }

        public string label { get; set; }

        /// <summary>base64-закодований реальний stream URL (для episode token validation).</summary>
        public string fallback { get; set; }
    }

    /// <summary>Результат resolve stream.</summary>
    public class FrankoStream
    {
        public string Url { get; set; }

        public string Quality { get; set; }
    }

    /// <summary>Результат consilium search за imdb_id.</summary>
    public class FrankoSearchResult
    {
        public int Id { get; set; }

        public bool IsSerial { get; set; }

        public FrankoPayload Payload { get; set; }

        public string Title { get; set; }
    }
}
