using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Module;

namespace Uaflix
{
    public class ModInit
    {
        public static OnlinesSettings UaFlix;

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            UaFlix = new OnlinesSettings("Uaflix", "https://uaflix.net", streamproxy: false, useproxy: false)
            {
                displayname = "UaFlix",
                group = 0,
                group_hide = false,
                globalnameproxy = null,
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                },
                // Note: OnlinesSettings не має властивості additional, використовуємо інший підхід
            };
            
            UaFlix = ModuleInvoke.Conf("Uaflix", UaFlix).ToObject<OnlinesSettings>();
            
            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("uaflix");
        }
    }
}