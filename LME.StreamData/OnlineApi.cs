using Microsoft.AspNetCore.Http;
using Shared.Models;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;

namespace LME.StreamData
{
    public class OnlineApi : IModuleOnline
    {
        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            var online = new List<ModuleOnlineItem>();

            var init = ModInit.StreamDataSettings;
            if (init.enable && !init.rip)
            {
                if (UpdateService.IsDisconnected())
                    init.overridehost = null;

                // StreamData працює з TMDB ID — показуємо для всього контенту
                online.Add(new ModuleOnlineItem(init, "lme_streamdata"));
            }

            return online;
        }
    }
}
