using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Online.Settings;

namespace AshdiBase
{
    public class ModInit
    {
        public static OnlinesSettings AshdiBase;
        public static bool ApnHostProvided;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            AshdiBase = new OnlinesSettings("AshdiBase", "https://base.ashdi.vip", streamproxy: false, useproxy: false)
            {
                displayname = "Ashdi Base",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };

            var conf = ModuleInvoke.Conf("AshdiBase", AshdiBase);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            AshdiBase = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, AshdiBase);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                AshdiBase.streamproxy = false;
            }
            else if (AshdiBase.streamproxy)
            {
                AshdiBase.apnstream = false;
                AshdiBase.apn = null;
            }
        }
    }
}
