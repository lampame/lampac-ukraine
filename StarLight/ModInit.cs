using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Online.Settings;

namespace StarLight
{
    public class ModInit
    {
        public static double Version => 3.1;

        public static OnlinesSettings StarLight;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => StarLight;
            set => StarLight = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            

            StarLight = new OnlinesSettings("StarLight", "https://tp-back.starlight.digital", streamproxy: false, useproxy: false)
            {
                displayname = "StarLight",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var conf = ModuleInvoke.Conf("StarLight", StarLight);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            StarLight = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, StarLight);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                StarLight.streamproxy = false;
            }
            else if (StarLight.streamproxy)
            {
                StarLight.apnstream = false;
                StarLight.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("starlight");
        }
    }
}
