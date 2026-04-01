using Shared.Models.Online.Settings;

namespace KlonFUN
{
    public class MagicApnSettings
    {
        public string ashdi { get; set; }
    }

    public class ModuleConfig : OnlinesSettings
    {
        public ModuleConfig(string plugin, string host, string apihost = null, bool useproxy = false, string token = null, bool enable = true, bool streamproxy = false, bool rip = false, bool forceEncryptToken = false, string rch_access = null, string stream_access = null) : base(plugin, host, apihost, useproxy, token, enable, streamproxy, rip, forceEncryptToken, rch_access, stream_access)
        {
        }

        public MagicApnSettings magic_apn { get; set; }
    }
}
