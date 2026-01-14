using Shared.Models.Base;
using System;
using System.Collections.Generic;

namespace StarLight
{
    public class OnlineApi
    {
        public static List<(string name, string url, string plugin, int index)> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            var online = new List<(string name, string url, string plugin, int index)>();

            if (!string.Equals(original_language, "uk", StringComparison.OrdinalIgnoreCase))
                return online;

            var init = ModInit.StarLight;
            if (init.enable && !init.rip)
            {
                string url = init.overridehost;
                if (string.IsNullOrEmpty(url))
                    url = $"{host}/starlight";

                online.Add((init.displayname, url, "starlight", init.displayindex));
            }

            return online;
        }
    }
}
