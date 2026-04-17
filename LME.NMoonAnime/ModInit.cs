using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using System;
using System.Net.Http;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LME.NMoonAnime
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 2.1;

        public static OnlinesSettings NMoonAnime;

        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => NMoonAnime;
            set => NMoonAnime = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            NMoonAnime = new OnlinesSettings("LME.NMoonAnime", "https://moonanime.art", "https://apx.lme.isroot.in", streamproxy: false, useproxy: false)
            {
                displayname = "New MoonAnime",
                displayindex = 0,
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(NMoonAnime);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.NMoonAnime", defaults) ?? JObject.FromObject(NMoonAnime);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            NMoonAnime = conf.ToObject<OnlinesSettings>();

            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, NMoonAnime, useDefaultHostWhenEmpty: true);

            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                NMoonAnime.streamproxy = false;
            }
            else if (NMoonAnime.streamproxy)
            {
                NMoonAnime.apnstream = false;
                NMoonAnime.apn = null;
            }

            OnlineRegistry.RegisterWithSearch("lme_nmoonanime");
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
