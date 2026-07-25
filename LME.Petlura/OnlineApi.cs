using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared.Models;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LME.Petlura
{
    public class OnlineApi : IModuleOnline
    {
        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            return Events(host, args.imdb_id, args.title, args.original_title, args.original_language, args.year, args.source, args.serial, args.account_email);
        }

        private static List<ModuleOnlineItem> Events(string host, string imdb_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            var online = new List<ModuleOnlineItem>();

            var init = ModInit.Petlura;
            if (init.enable && !init.rip)
            {
                // Petlura працює тільки з imdb_id
                if (string.IsNullOrWhiteSpace(imdb_id))
                    return online;

                if (UpdateService.IsDisconnected())
                    init.overridehost = null;

                online.Add(new ModuleOnlineItem(init, "lme_petlura"));
            }

            return online;
        }
    }
}
