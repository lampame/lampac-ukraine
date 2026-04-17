using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LME.UafilmME
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 1.1;

        public static OnlinesSettings UafilmME;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => UafilmME;
            set => UafilmME = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            UafilmME = new OnlinesSettings("LME.UafilmME", "https://uafilm.me", streamproxy: false, useproxy: false)
            {
                displayname = "UAFilmME",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(UafilmME);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.UafilmME", defaults);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            UafilmME = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, UafilmME);

            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                UafilmME.streamproxy = false;
            }
            else if (UafilmME.streamproxy)
            {
                UafilmME.apnstream = false;
                UafilmME.apn = null;
            }

            OnlineRegistry.RegisterWithSearch("lme_uafilmme");
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
