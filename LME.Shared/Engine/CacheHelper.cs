using Shared.Models.Online.Settings;
using System;

namespace LME.Common.Engine
{
    /// <summary>
    /// Спільний розрахунок часу кешування.
    /// Винесено з 10 копій у *Invoke.cs кожного модуля.
    /// </summary>
    public static class CacheHelper
    {
        public static TimeSpan CacheTime(int multiaccess, int home = 5, int mikrotik = 2, OnlinesSettings init = null, int rhub = -1)
        {
            if (init != null && init.rhub && rhub != -1)
                return TimeSpan.FromMinutes(rhub);

            int ctime = init != null && init.cache_time > 0 ? init.cache_time : multiaccess;
            if (ctime > multiaccess)
                ctime = multiaccess;

            return TimeSpan.FromMinutes(ctime);
        }
    }
}
