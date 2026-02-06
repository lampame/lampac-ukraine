using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Module;
using System.Collections.Generic;

namespace UAKino
{
    public class OnlineApi
    {
        public static List<(string name, string url, string plugin, int index)> Invoke(
            HttpContext httpContext,
            IMemoryCache memoryCache,
            RequestModel requestInfo,
            string host,
            OnlineEventsModel args)
        {
            long.TryParse(args.id, out long tmdbid);
            return Events(host, tmdbid, args.imdb_id, args.kinopoisk_id, args.title, args.original_title, args.original_language, args.year, args.source, args.serial, args.account_email);
        }

        public static List<(string name, string url, string plugin, int index)> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            var online = new List<(string name, string url, string plugin, int index)>();

            var init = ModInit.UAKino;
            if (init.enable && !init.rip)
            {
                string url = init.overridehost;
                if (string.IsNullOrEmpty(url) || UpdateService.IsDisconnected())
                    url = $"{host}/uakino";

                online.Add((init.displayname, url, "uakino", init.displayindex));
            }

            return online;
        }
    }
}
