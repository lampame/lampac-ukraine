using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace Mikai
{
    public class ModInit
    {
        public static OnlinesSettings Mikai;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
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

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("mikai");
        }
    }
}
