using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace StarLight
{
    public class ModInit
    {
        public static OnlinesSettings StarLight;

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
            StarLight = ModuleInvoke.Conf("StarLight", StarLight).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("starlight");
        }
    }
}
