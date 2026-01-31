using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace Mikai
{
    public class ModInit
    {
        public static double Version => 3.1;

        public static OnlinesSettings Mikai;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => Mikai;
            set => Mikai = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            UpdateService.Start(initspace.memoryCache, initspace.nws);

            Mikai = new OnlinesSettings("Mikai", "https://mikai.me", streamproxy: false, useproxy: false)
            {
                displayname = "Mikai",
                displayindex = 0,
                apihost = "https://api.mikai.me/v1",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var conf = ModuleInvoke.Conf("Mikai", Mikai);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            Mikai = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Mikai);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                Mikai.streamproxy = false;
            }
            else if (Mikai.streamproxy)
            {
                Mikai.apnstream = false;
                Mikai.apn = null;
            }

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("mikai");
        }
    }
}
