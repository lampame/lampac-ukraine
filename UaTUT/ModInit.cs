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
        public static OnlinesSettings UaTUT;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
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
            UaTUT = ModuleInvoke.Conf("UaTUT", UaTUT).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("uatut");
        }
    }
}
