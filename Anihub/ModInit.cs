using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace Anihub
{
    public class ModInit
    {
        public static OnlinesSettings Anihub;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            Anihub = new OnlinesSettings("Anihub", "https://anihub.in.ua", streamproxy: false, useproxy: false)
            {
                displayname = "🇺🇦 Anihub",
                displayindex = 0,
                apihost = "https://anihub.in.ua/api",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };
            Anihub = ModuleInvoke.Conf("Anihub", Anihub).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("anihub");
        }
    }
}
