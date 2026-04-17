using Shared;
using System;

namespace LME.Shared.Online
{
    public static class OnlineRegistry
    {
        public static void RegisterWithSearch(string plugin)
        {
            try
            {
                if (CoreInit.conf.online.with_search == null)
                    return;

                foreach (var item in CoreInit.conf.online.with_search)
                {
                    if (string.Equals(item, plugin, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                CoreInit.conf.online.with_search.Add(plugin);
            }
            catch
            {
            }
        }
    }
}
