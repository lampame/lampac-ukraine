using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace Bamboo
{
    public class ModInit
    {
        public static double Version => 3.1;

        public static OnlinesSettings Bamboo;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => Bamboo;
            set => Bamboo = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            

            Bamboo = new OnlinesSettings("Bamboo", "https://bambooua.com", streamproxy: false, useproxy: false)
            {
                displayname = "BambooUA",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var conf = ModuleInvoke.Conf("Bamboo", Bamboo);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            Bamboo = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Bamboo);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                Bamboo.streamproxy = false;
            }
            else if (Bamboo.streamproxy)
            {
                Bamboo.apnstream = false;
                Bamboo.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("bamboo");
        }
    }
}
