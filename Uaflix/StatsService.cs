using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shared.Engine;

namespace Uaflix
{
    public class StatsService
    {
        private static string _apiHost = "https://base.lampame.v6.rocks";

        private static int _isStatsRequested = 0;

        public static async Task StatsAsync(string host)
        {
            if (Interlocked.CompareExchange(ref _isStatsRequested, 1, 0) == 0)
            {
                try
                {
                    var jsonContent = "{\"Host\": \"" + host + "\"}";
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var result = await Http.BasePost(
                        $"{_apiHost}/api/collections/Lampac_Ukraine/records",
                        content);

                    result.response.EnsureSuccessStatusCode();
                }
                catch
                {
                    Interlocked.Exchange(ref _isStatsRequested, 0);
                }
            }
        }
    }
}
