using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace Bamboo
{
    public class ModInit
    {
        public static OnlinesSettings Bamboo;

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
            Bamboo = ModuleInvoke.Conf("Bamboo", Bamboo).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("bamboo");
        }
    }
}
