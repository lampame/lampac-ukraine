using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;
using LME.Petlura.Models;

namespace LME.Petlura
{
    public class PetluraSettings : OnlinesSettings, ICloneable
    {
        public PetluraSettings(string plugin, string host, string apihost = null, bool useproxy = false, string token = null, bool enable = true, bool streamproxy = false, bool rip = false, bool forceEncryptToken = false, string rch_access = null, string stream_access = null)
            : base(plugin, host, apihost, useproxy, token, enable, streamproxy, rip, forceEncryptToken, rch_access, stream_access)
        {
        }

        public string[] source_list { get; set; }

        public new PetluraSettings Clone()
        {
            return (PetluraSettings)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return MemberwiseClone();
        }
    }

    public class ModInit : IModuleLoaded
    {
        public static double Version => 1.0;

        public static PetluraSettings Petlura;

        public static bool ApnHostProvided;

        public static PetluraSettings Settings
        {
            get => Petlura;
            set => Petlura = value;
        }

        /// <summary>
        /// Модуль завантажено.
        /// </summary>
        public void Loaded(InitspaceModel initspace)
        {
            Petlura = new PetluraSettings("LME.Petlura", "https://uaserials.fm", streamproxy: false, useproxy: false)
            {
                displayname = "Petlura",
                displayindex = 0,
                source_list = new[] { "https://uaserials.fm", "https://uaserials.my" },
                proxy = new global::Shared.Models.Base.ProxySettings()
                {
                    useAuth = true,
                    username = "",
                    password = "",
                    list = new string[] { "socks5://ip:port" }
                }
            };

            var defaults = JObject.FromObject(Petlura);
            defaults["enabled"] = true;
            var conf = ModuleInvoke.Init("LME.Petlura", defaults) ?? defaults;

            bool hasApn = ApnHelper.TryGetInitConf(conf, out bool apnEnabled, out string apnHost);
            conf.Remove("apn");
            conf.Remove("apn_host");
            Petlura = conf.ToObject<PetluraSettings>();

            if (Petlura.source_list == null || Petlura.source_list.Length == 0)
                Petlura.source_list = new[] { "https://uaserials.fm", "https://uaserials.my" };

            if (hasApn)
                ApnHelper.ApplyInitConf(apnEnabled, apnHost, Petlura);

            ApnHostProvided = ApnHelper.IsEnabled(Petlura);
            if (ApnHostProvided)
            {
                Petlura.streamproxy = false;
            }
            else if (Petlura.streamproxy)
            {
                Petlura.apnstream = false;
                Petlura.apn = null;
            }

            // Показувати «уточнити пошук».
            OnlineRegistry.RegisterWithSearch("lme_petlura");
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
