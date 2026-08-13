using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;
using Shared.Models.Events;
using LME.Common.Online;
using LME.Common.Update;
using LME.Franko.Models;
using Shared.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LME.Franko
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 1.0;

        public static FrankoConfig Franko;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => Franko;
            set => Franko = value as FrankoConfig;
        }

        /// <summary>
        /// Дефолтний пул мірорів для consilium search (тільки сайти-донори).
        /// kinokrad виключено: його прямий /show/imdb/ ендпоінт ненадійний (чужий контент).
        /// </summary>
        private static readonly string[] DefaultMirrors = new string[]
        {
            "https://uakino.watch",
            "https://uakinohd.my",
            "https://uaserials.live",
            "https://uaserials.digital",
            "https://uaserialshd.my",
            "https://uakino.productions"
        };

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            UpdateConfig();
            EventListener.UpdateInitFile += UpdateConfig;

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_franko");
        }

        private void UpdateConfig()
        {
            Franko = new FrankoConfig("LME.Franko", "https://uakino.watch", streamproxy: false, useproxy: false)
            {
                displayname = "Franko",
                displayindex = 826,
                proxy = new global::Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                },
                mirrors = DefaultMirrors,
                api_host = "https://franko.uacdn.online",
                fhost = "https://franko.uacdn.online"
            };

            var defaults = JObject.FromObject(Franko);
            defaults["enabled"] = true;

            var conf = ModuleInvoke.Init("LME.Franko", defaults) ?? defaults;
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            Franko = conf.ToObject<FrankoConfig>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Franko, useDefaultHostWhenEmpty: true);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);

            if (hasApn && apnEnabled)
            {
                Franko.streamproxy = false;
            }
            else if (Franko.streamproxy)
            {
                Franko.apnstream = false;
                Franko.apn = null;
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
