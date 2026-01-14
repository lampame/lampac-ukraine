using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;

namespace CikavaIdeya
{
    public class ModInit
    {
        public static OnlinesSettings CikavaIdeya;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            CikavaIdeya = new OnlinesSettings("CikavaIdeya", "https://cikava-ideya.top", streamproxy: false, useproxy: false)
            {
                displayname = "ЦікаваІдея",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };
            var conf = ModuleInvoke.Conf("CikavaIdeya", CikavaIdeya);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            CikavaIdeya = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, CikavaIdeya);

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("cikavaideya");
        }
    }
}
