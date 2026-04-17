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


namespace LME.Bamboo
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 4.1;

        public static OnlinesSettings Bamboo;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => Bamboo;
            set => Bamboo = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            

            Bamboo = new OnlinesSettings("LME.Bamboo", "https://bambooua.com", streamproxy: false, useproxy: false)
            {
                displayname = "BambooUA",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var defaults = JObject.FromObject(Bamboo);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.Bamboo", defaults);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            Bamboo = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Bamboo, useDefaultHostWhenEmpty: true);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                Bamboo.streamproxy = false;
            }
            else if (Bamboo.streamproxy)
            {
                Bamboo.apnstream = false;
                Bamboo.apn = null;
            }

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_bamboo");
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
