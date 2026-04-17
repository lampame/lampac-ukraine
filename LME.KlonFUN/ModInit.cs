using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shared.Models.Events;

namespace LME.KlonFUN
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 2.1;

        public static ModuleConfig KlonFUN;
        public static bool ApnHostProvided;
        public static string MagicApnAshdiHost;

        public static ModuleConfig Settings
        {
            get => KlonFUN;
            set => KlonFUN = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            UpdateConfig();
            EventListener.UpdateInitFile += UpdateConfig;

            // Додаємо підтримку "уточнити пошук".
            OnlineRegistry.RegisterWithSearch("lme_klonfun");
        }

        private void UpdateConfig()
        {
            KlonFUN = new ModuleConfig("LME.KlonFUN", "https://klon.fun", streamproxy: false, useproxy: false)
            {
                displayname = "KlonFUN",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(KlonFUN);
            defaults["enabled"] = true;
            defaults["magic_apn"] = new JObject()
            {
                ["ashdi"] = ApnHelper.DefaultHost
            };

            var conf = ModuleInvoke.Init("LME.KlonFUN", defaults) ?? defaults;
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            MagicApnAshdiHost = ApnHelper.TryGetMagicAshdiHost(conf);
            conf.Remove("magic_apn");
            conf.Remove("apn");
            conf.Remove("apn_host");
            KlonFUN = conf.ToObject<ModuleConfig>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, KlonFUN);
            ApnHostProvided = ApnHelper.IsEnabled(KlonFUN);

            if (ApnHostProvided)
            {
                KlonFUN.streamproxy = false;
            }
            else if (KlonFUN.streamproxy)
            {
                KlonFUN.apnstream = false;
                KlonFUN.apn = null;
            }
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= UpdateConfig;
        }
    }

    public static class UpdateService
    {
        private static readonly ModuleUpdateService _service = new(
            () => ModInit.Settings?.plugin,
            () => ModInit.Version);

        public static Task ConnectAsync(string host, CancellationToken cancellationToken = default)
            => _service.ConnectAsync(host, cancellationToken);

        public static bool IsDisconnected()
            => _service.IsDisconnected();

        public static ActionResult Validate(ActionResult result)
            => _service.Validate(result);
    }
}
