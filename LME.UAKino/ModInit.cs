using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Shared.Models;
using Shared.Models.Events;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LME.UAKino
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 1.0;

        public static OnlinesSettings UAKino;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => UAKino;
            set => UAKino = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            UAKino = new OnlinesSettings("LME.UAKino", "https://uakino.top", streamproxy: false, useproxy: false)
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
            var defaults = JObject.FromObject(UAKino);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.UAKino", defaults);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            UAKino = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, UAKino, useDefaultHostWhenEmpty: true);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                UAKino.streamproxy = false;
            }
            else if (UAKino.streamproxy)
            {
                UAKino.apnstream = false;
                UAKino.apn = null;
            }

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_uakino");
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
