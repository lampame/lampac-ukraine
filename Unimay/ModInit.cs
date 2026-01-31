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

namespace Unimay
{
    public class ModInit
    {
        public static double Version => 3.1;

        public static OnlinesSettings Unimay;

        public static OnlinesSettings Settings
        {
            get => Unimay;
            set => Unimay = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            UpdateService.Start(initspace.memoryCache, initspace.nws);

            Unimay = new OnlinesSettings("Unimay", "https://api.unimay.media/v1", streamproxy: false, useproxy: false)
            {
                displayname = "Unimay",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };
            Unimay = ModuleInvoke.Conf("Unimay", Unimay).ToObject<OnlinesSettings>();

            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("unimay");
        }
    }
}
