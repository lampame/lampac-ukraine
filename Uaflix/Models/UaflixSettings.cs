using Shared.Models.Online.Settings;

namespace Uaflix.Models
{
    public class UaflixSettings : OnlinesSettings, ICloneable
    {
        public UaflixSettings(string plugin, string host, string apihost = null, bool useproxy = false, string token = null, bool enable = true, bool streamproxy = false, bool rip = false, bool forceEncryptToken = false, string rch_access = null, string stream_access = null)
            : base(plugin, host, apihost, useproxy, token, enable, streamproxy, rip, forceEncryptToken, rch_access, stream_access)
        {
        }

        public string login { get; set; }

        public string passwd { get; set; }

        public new UaflixSettings Clone()
        {
            return (UaflixSettings)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return MemberwiseClone();
        }
    }
}
