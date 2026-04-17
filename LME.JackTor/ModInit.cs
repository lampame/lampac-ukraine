using LME.JackTor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
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

namespace LME.JackTor
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 2.1;

        public static JackTorSettings JackTor;

        public static JackTorSettings Settings
        {
            get => JackTor;
            set => JackTor = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            JackTor = new JackTorSettings("LME.JackTor", "http://127.0.0.1:9117", streamproxy: false, useproxy: false)
            {
                displayname = "JackTor",
                displayindex = 0,
                group = 0,
                group_hide = true,

                jackett = "http://127.0.0.1:9117",
                apikey = string.Empty,
                min_sid = 5,
                min_peers = 0,
                max_size = 0,
                max_serial_size = 0,
                emptyVoice = true,
                forceAll = false,
                sort = "sid",
                max_age_days = 0,
                quality_allow = new[] { 2160, 1080, 720 },
                trackers_allow = Array.Empty<string>(),
                trackers_block = Array.Empty<string>(),
                hdr_mode = "any",
                codec_allow = "any",
                audio_pref = new[] { "ukr", "eng", "rus" },
                year_tolerance = 1,
                query_mode = "both",
                proxy = new Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(JackTor);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.JackTor", defaults) ?? defaults;
            JackTor = conf.ToObject<JackTorSettings>();

            if (string.IsNullOrWhiteSpace(JackTor.jackett))
                JackTor.jackett = JackTor.host;

            if (string.IsNullOrWhiteSpace(JackTor.host))
                JackTor.host = JackTor.jackett;

            // Показувати «уточнити пошук».
            OnlineRegistry.RegisterWithSearch("lme_jacktor");
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
