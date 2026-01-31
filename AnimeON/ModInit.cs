using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Online.Settings;

namespace AnimeON
{
    public class ModInit
    {
        public static double Version => 3.1;

        public static OnlinesSettings AnimeON;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => AnimeON;
            set => AnimeON = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            UpdateService.Start(initspace.memoryCache, initspace.nws);

            AnimeON = new OnlinesSettings("AnimeON", "https://animeon.club", streamproxy: false, useproxy: false)
            {
                displayname = "🇯🇵 AnimeON",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var conf = ModuleInvoke.Conf("AnimeON", AnimeON);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            AnimeON = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, AnimeON);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                AnimeON.streamproxy = false;
            }
            else if (AnimeON.streamproxy)
            {
                AnimeON.apnstream = false;
                AnimeON.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("animeon");
        }
    }
}
