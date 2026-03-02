using Shared.Models.Online.Settings;
using System;
using System.Collections.Generic;

namespace JackTor.Models
{
    public class JackTorSettings : OnlinesSettings, ICloneable
    {
        public JackTorSettings(
            string plugin,
            string host,
            string apihost = null,
            bool useproxy = false,
            string token = null,
            bool enable = true,
            bool streamproxy = false,
            bool rip = false,
            bool forceEncryptToken = false,
            string rch_access = null,
            string stream_access = null)
            : base(plugin, host, apihost, useproxy, token, enable, streamproxy, rip, forceEncryptToken, rch_access, stream_access)
        {
        }

        public string jackett { get; set; }

        public string apikey { get; set; }

        public int min_sid { get; set; }

        public int min_peers { get; set; }

        public long max_size { get; set; }

        public long max_serial_size { get; set; }

        public bool emptyVoice { get; set; }

        public bool forceAll { get; set; }

        public string filter { get; set; }

        public string filter_ignore { get; set; }

        public string sort { get; set; }

        public int max_age_days { get; set; }

        public string[] trackers_allow { get; set; }

        public string[] trackers_block { get; set; }

        public int[] quality_allow { get; set; }

        public string hdr_mode { get; set; }

        public string codec_allow { get; set; }

        public string[] audio_pref { get; set; }

        public int year_tolerance { get; set; }

        public string query_mode { get; set; }

        public PidTorAuthTS base_auth { get; set; }

        public string[] torrs { get; set; }

        public List<PidTorAuthTS> auth_torrs { get; set; }

        public new JackTorSettings Clone()
        {
            return (JackTorSettings)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return MemberwiseClone();
        }
    }

    public class JackettSearchRoot
    {
        public JackettResult[] Results { get; set; }

        public JackettIndexer[] Indexers { get; set; }
    }

    public class JackettResult
    {
        public string Guid { get; set; }

        public string Tracker { get; set; }

        public string TrackerId { get; set; }

        public string TrackerType { get; set; }

        public string CategoryDesc { get; set; }

        public string Title { get; set; }

        public string Link { get; set; }

        public string Details { get; set; }

        public DateTime? PublishDate { get; set; }

        public int[] Category { get; set; }

        public long? Size { get; set; }

        public string Description { get; set; }

        public int? Seeders { get; set; }

        public int? Peers { get; set; }

        public string InfoHash { get; set; }

        public string MagnetUri { get; set; }

        public double? Gain { get; set; }
    }

    public class JackettIndexer
    {
        public string ID { get; set; }

        public string Name { get; set; }

        public int Status { get; set; }

        public int Results { get; set; }

        public string Error { get; set; }

        public int ElapsedTime { get; set; }
    }

    public class JackTorParsedResult
    {
        public string Rid { get; set; }

        public string Title { get; set; }

        public string Tracker { get; set; }

        public string TrackerId { get; set; }

        public string SourceUri { get; set; }

        public string Voice { get; set; }

        public int AudioRank { get; set; }

        public int Quality { get; set; }

        public string QualityLabel { get; set; }

        public string MediaInfo { get; set; }

        public string CategoryDesc { get; set; }

        public string Codec { get; set; }

        public bool IsHdr { get; set; }

        public bool IsDolbyVision { get; set; }

        public int Seeders { get; set; }

        public int Peers { get; set; }

        public long Size { get; set; }

        public DateTime PublishDate { get; set; }

        public int[] Seasons { get; set; }

        public int ExtractedYear { get; set; }

        public double Gain { get; set; }
    }

    public class JackTorSourceCache
    {
        public string Rid { get; set; }

        public string SourceUri { get; set; }

        public string Title { get; set; }

        public string Voice { get; set; }

        public int Quality { get; set; }

        public int[] Seasons { get; set; }
    }
}
