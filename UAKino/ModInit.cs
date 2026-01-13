using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace UAKino
{
    public class ModInit
    {
        public static OnlinesSettings UAKino;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            UAKino = new OnlinesSettings("UAKino", "https://uakino.best", streamproxy: false, useproxy: false)
            {
                displayname = "UAKino",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            UAKino = ModuleInvoke.Conf("UAKino", UAKino).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("uakino");
        }
    }
}
