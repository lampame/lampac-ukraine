using System.Collections.Generic;

namespace AshdiBase.Models
{
    public class SerialStructure
    {
        public string SerialUrl { get; set; }
        public Dictionary<string, VoiceInfo> Voices { get; set; }

        public SerialStructure()
        {
            Voices = new Dictionary<string, VoiceInfo>();
        }
    }
}
