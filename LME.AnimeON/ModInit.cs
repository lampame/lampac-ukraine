using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
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


namespace LME.AnimeON
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 4.1;

        public static OnlinesSettings AnimeON;
        public static bool ApnHostProvided;
        public static string MagicApnAshdiHost;

        public static OnlinesSettings Settings
        {
            get => AnimeON;
            set => AnimeON = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            

            AnimeON = new OnlinesSettings("LME.AnimeON", "https://animeon.club", streamproxy: false, useproxy: false)
            {
                displayname = "AnimeON",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var defaults = JObject.FromObject(AnimeON);
            defaults["enabled"] = true;
            defaults["magic_apn"] = new JObject()
            {
                ["ashdi"] = ApnHelper.DefaultHost
            };

            var conf = ModuleInvoke.Init("LME.AnimeON", defaults) ?? defaults;
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            MagicApnAshdiHost = ApnHelper.TryGetMagicAshdiHost(conf);
            conf.Remove("magic_apn");
            conf.Remove("apn");
            conf.Remove("apn_host");
            AnimeON = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, AnimeON);
            ApnHostProvided = ApnHelper.IsEnabled(AnimeON);
            if (ApnHostProvided)
            {
                AnimeON.streamproxy = false;
            }
            else if (AnimeON.streamproxy)
            {
                AnimeON.apnstream = false;
                AnimeON.apn = null;
            }

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_animeon");
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
