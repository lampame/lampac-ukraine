using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared.Models;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KlonFUN
{
    public class OnlineApi : IModuleOnline
    {
        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            long.TryParse(args.id, out long tmdbid);
            return Events(host, tmdbid, args.imdb_id, args.kinopoisk_id, args.title, args.original_title, args.original_language, args.year, args.source, args.serial, args.account_email);
        }

        private static List<ModuleOnlineItem> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            var online = new List<ModuleOnlineItem>();

            var init = ModInit.KlonFUN;
            if (init.enable && !init.rip)
            {
                if (UpdateService.IsDisconnected())
                    init.overridehost = null;

                online.Add(new ModuleOnlineItem(init, "klonfun"));
            }

            return online;
        }
    }
}
