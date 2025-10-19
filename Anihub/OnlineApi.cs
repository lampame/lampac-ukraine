using Shared.Models.Base;  
using System.Collections.Generic;  

namespace Anihub
{
    public class OnlineApi  
    {
        public static List<(string name, string url, string plugin, int index)> Events(string host, long id, string imdb_id, long kinopoisk_id, string title, string original_title, string original_language, int year, string source, int serial, string account_email)
        {
            var online = new List<(string name, string url, string plugin, int index)>();

            var init = ModInit.Anihub;

            // Логіка визначення isAnime згідно Deepwiki:
            // isanime = true якщо original_language == "ja" або "zh"
            bool hasLang = !string.IsNullOrEmpty(original_language);
            bool isanime = hasLang && (original_language == "ja" || original_language == "zh");

            // Аніме-модуль додається тільки якщо:
            // - загальний пошук (serial == -1), або
            // - контент є аніме (isanime), або
            // - мова невідома (немає original_language)
            if (init.enable && !init.rip && (serial == -1 || isanime || !hasLang))
            {
                string url = init.overridehost;
                if (string.IsNullOrEmpty(url))
                    url = $"{host}/anihub";

                online.Add((init.displayname, url, "anihub", init.displayindex));
            }

            return online;
        }
    }
}
