using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LME.Uaflix.Models;

namespace LME.Uaflix
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 5.2;

        public static UaflixSettings UaFlix;

        public static bool ApnHostProvided;
        public static string MagicApnAshdiHost;

        public static UaflixSettings Settings
        {
            get => UaFlix;
            set => UaFlix = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            UaFlix = new UaflixSettings("LME.Uaflix", "https://uafix.net", streamproxy: false, useproxy: false)
            {
                displayname = "UaFlix",
                group = 0,
                group_hide = false,
                globalnameproxy = null,
                displayindex = 0,
                login = null,
                passwd = null,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "a",
                    password = "a",
                    list = new string[] { "socks5://IP:PORT" }
                }
            };

            var defaults = JObject.FromObject(UaFlix);
            defaults["enabled"] = true;
            defaults["magic_apn"] = new JObject()
            {
                ["ashdi"] = ApnHelper.DefaultHost
            };

            var conf = ModuleInvoke.Init("LME.Uaflix", defaults) ?? defaults;
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            MagicApnAshdiHost = ApnHelper.TryGetMagicAshdiHost(conf);
            conf.Remove("magic_apn");
            conf.Remove("apn");
            conf.Remove("apn_host");
            UaFlix = conf.ToObject<UaflixSettings>();

            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, UaFlix);

            ApnHostProvided = ApnHelper.IsEnabled(UaFlix);
            if (ApnHostProvided)
            {
                UaFlix.streamproxy = false;
            }
            else if (UaFlix.streamproxy)
            {
                UaFlix.apnstream = false;
                UaFlix.apn = null;
            }

            // Показувати «уточнити пошук».
            OnlineRegistry.RegisterWithSearch("lme_uaflix");
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
