using Shared;
using Shared.Models;
using Shared.Models.Online.Settings;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LME.Common.Engine
{
    /// <summary>
    /// Спільний HTTP-helper з fallback HttpHydra → Http.Get.
    /// Винесено з 11 копій у *Invoke.cs кожного модуля.
    /// </summary>
    public static class HttpHelper
    {
        public static Task<string> GetAsync(
            HttpHydra hydra,
            OnlinesSettings init,
            string url,
            List<HeadersModel> headers,
            ProxyManager proxyManager)
        {
            if (hydra != null)
                return hydra.Get(url, newheaders: headers);

            return Http.Get(init.cors(url), headers: headers, proxy: proxyManager.Get());
        }
    }
}
