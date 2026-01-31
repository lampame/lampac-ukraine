using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace UaTUT
{
    public class ModInit
    {
        public static double Version => 3.1;

        public static OnlinesSettings UaTUT;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => UaTUT;
            set => UaTUT = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            UpdateService.Start(initspace.memoryCache, initspace.nws);

            UaTUT = new OnlinesSettings("UaTUT", "https://uk.uatut.fun", streamproxy: false, useproxy: false)
            {
                displayname = "🇺🇦 UaTUT",
                displayindex = 0,
                apihost = "https://uk.uatut.fun/watch",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };
            var conf = ModuleInvoke.Conf("UaTUT", UaTUT);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            UaTUT = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, UaTUT);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                UaTUT.streamproxy = false;
            }
            else if (UaTUT.streamproxy)
            {
                UaTUT.apnstream = false;
                UaTUT.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("uatut");
        }
    }
}
