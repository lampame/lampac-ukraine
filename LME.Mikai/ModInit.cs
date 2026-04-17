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


namespace LME.Mikai
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 4.1;

        public static OnlinesSettings Mikai;
        public static bool ApnHostProvided;
        public static string MagicApnAshdiHost;

        public static OnlinesSettings Settings
        {
            get => Mikai;
            set => Mikai = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {


            Mikai = new OnlinesSettings("LME.Mikai", "https://mikai.me", streamproxy: false, useproxy: false)
            {
                displayname = "Mikai",
                displayindex = 0,
                apihost = "https://api.mikai.me/v1",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(Mikai);
            defaults["enabled"] = true;
            defaults["magic_apn"] = new JObject()
            {
                ["ashdi"] = ApnHelper.DefaultHost
            };

            var conf = ModuleInvoke.Init("LME.Mikai", defaults) ?? defaults;
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            MagicApnAshdiHost = ApnHelper.TryGetMagicAshdiHost(conf);
            conf.Remove("magic_apn");
            conf.Remove("apn");
            conf.Remove("apn_host");
            Mikai = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Mikai);
            ApnHostProvided = ApnHelper.IsEnabled(Mikai);
            if (ApnHostProvided)
            {
                Mikai.streamproxy = false;
            }
            else if (Mikai.streamproxy)
            {
                Mikai.apnstream = false;
                Mikai.apn = null;
            }

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_mikai");
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
