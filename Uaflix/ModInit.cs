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
        public static double Version => 3.1;

        public static OnlinesSettings UaFlix;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => UaFlix;
            set => UaFlix = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public static void loaded(InitspaceModel initspace)
        {
            

            UaFlix = new OnlinesSettings("Uaflix", "https://uafix.net", streamproxy: false, useproxy: false)
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
            
            var conf = ModuleInvoke.Conf("Uaflix", UaFlix);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            UaFlix = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, UaFlix);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                UaFlix.streamproxy = false;
            }
            else if (UaFlix.streamproxy)
            {
                UaFlix.apnstream = false;
                UaFlix.apn = null;
            }
            
            // Виводити "уточнити пошук"
            AppInit.conf.online.with_search.Add("uaflix");
        }
    }
}
