using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Online.Settings;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace LME.AniWorld
{
    public class ModInit : IModuleLoaded
    {
        public static double Version => 1.0;

        public static OnlinesSettings AniWorld;
        public static bool ApnHostProvided;

        public static OnlinesSettings Settings
        {
            get => AniWorld;
            set => AniWorld = value;
        }

        /// <summary>
        /// модуль загружен
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            AniWorld = new OnlinesSettings("LME.AniWorld", "https://api.aniworldua.com", streamproxy: false, useproxy: false)
            {
                displayname = "AniWorld",
                displayindex = 0,
                group = 0,
                group_hide = false
            };
            var defaults = JObject.FromObject(AniWorld);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.AniWorld", defaults);
            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            AniWorld = conf.ToObject<OnlinesSettings>();
            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, AniWorld, useDefaultHostWhenEmpty: true);
            ApnHostProvided = hasApn && apnEnabled && !string.IsNullOrWhiteSpace(apnHost);
            if (hasApn && apnEnabled)
            {
                AniWorld.streamproxy = false;
            }
            else if (AniWorld.streamproxy)
            {
                AniWorld.apnstream = false;
                AniWorld.apn = null;
            }

            // Виводити "уточнити пошук"
            OnlineRegistry.RegisterWithSearch("lme_aniworld");
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
