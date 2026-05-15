using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.Online.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LME.StreamData
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 1.5;

        public static OnlinesSettings StreamDataSettings;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => StreamDataSettings;
            set => StreamDataSettings = value;
        }

        /// <summary>
        /// Модуль завантажено
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            StreamDataSettings = new OnlinesSettings("LME.StreamData", "https://streamdata.vaplayer.ru", streamproxy: false, useproxy: false)
            {
                displayname = "StreamData",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(StreamDataSettings);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.StreamData", defaults);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            StreamDataSettings = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, StreamDataSettings, useDefaultHostWhenEmpty: true);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                StreamDataSettings.streamproxy = false;
            }
            else if (StreamDataSettings.streamproxy)
            {
                StreamDataSettings.apnstream = false;
                StreamDataSettings.apn = null;
            }

            // Реєструємо плагін без пошуку — працюємо тільки через TMDB ID
            OnlineRegistry.RegisterWithSearch("lme_streamdata");
        }

        public void Dispose()
        {
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
