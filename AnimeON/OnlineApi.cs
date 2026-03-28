using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared.Models;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AnimeON
{
    public class OnlineApi : IModuleOnline
    {
        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, IMemoryCache memoryCache, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            long.TryParse(args.id, out long tmdbid);
            return Events(host, tmdbid, args.imdb_id, args.kinopoisk_id, args.title, args.original_title, args.original_language, args.year, args.source, args.serial, args.account_email);
        }

        public Task<List<ModuleOnlineItem>> InvokeAsync(HttpContext httpContext, IMemoryCache memoryCache, RequestModel requestInfo, string host, OnlineEventsModel args)
            => Task.FromResult(default(List<ModuleOnlineItem>));

        public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, IMemoryCache memoryCache, RequestModel requestInfo, string host, OnlineSpiderModel args)
            => null;

        public Task<List<ModuleOnlineSpiderItem>> SpiderAsync(HttpContext httpContext, IMemoryCache memoryCache, RequestModel requestInfo, string host, OnlineSpiderModel args)
            => Task.FromResult(default(List<ModuleOnlineSpiderItem>));

        private static List<ModuleOnlineItem> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            var online = new List<ModuleOnlineItem>();

            var init = ModInit.AnimeON;

            bool hasLang = !string.IsNullOrEmpty(original_language);
            bool isanime = hasLang && (original_language == "ja" || original_language == "zh");

            if (init.enable && !init.rip && (serial == -1 || isanime || !hasLang))
            {
                string url = init.overridehost;
                if (string.IsNullOrEmpty(url) || UpdateService.IsDisconnected())
                    url = $"{host}/lite/animeon";

                online.Add(new ModuleOnlineItem(init.displayname, url, "animeon", init.displayindex));
            }

            return online;
        }
    }
}
