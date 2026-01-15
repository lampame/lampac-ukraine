using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CikavaIdeya
{
    public class TouchService
    {
        private static readonly HashSet<string> EntrySet = new(
            new[]
                {
                    "c3ZpdGFubW92aWU=",
                    "cG9ydGFsLXR2",
                    "Zmxvd25ldA=="
                }
                .Select(base64 => Encoding.UTF8.GetString(Convert.FromBase64String(base64))),
            StringComparer.OrdinalIgnoreCase);

        public static bool Touch(string host)
        {
            return EntrySet.Any(host.Contains);
        }
    }
}
