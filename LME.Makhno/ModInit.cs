using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Caching.Memory;
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

namespace LME.Makhno
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 3.1;

        public static OnlinesSettings Makhno;
        public static bool ApnHostProvided;
        public static string MagicApnAshdiHost;

        public static OnlinesSettings Settings
        {
            get => Makhno;
            set => Makhno = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            Makhno = new OnlinesSettings("LME.Makhno", "https://wh.lme.isroot.in", streamproxy: false, useproxy: false)
            {
                displayname = "Махно",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };
            var defaults = JObject.FromObject(Makhno);
            defaults["enabled"] = true;
            defaults["magic_apn"] = new JObject()
            {
                ["ashdi"] = ApnHelper.DefaultHost
            };

            var conf = ModuleInvoke.Init("LME.Makhno", defaults) ?? defaults;
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            MagicApnAshdiHost = ApnHelper.TryGetMagicAshdiHost(conf);
            conf.Remove("magic_apn");
            if (hasApn)
            {
                conf.Remove("apn");
                conf.Remove("apn_host");
            }
            Makhno = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Makhno);
            ApnHostProvided = ApnHelper.IsEnabled(Makhno);
            if (ApnHostProvided)
            {
                Makhno.streamproxy = false;
            }
            else if (Makhno.streamproxy)
            {
                Makhno.apnstream = false;
                Makhno.apn = null;
            }

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_makhno");
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
