using System;
using System.Text;

namespace LME.Common.Engine
{
    /// <summary>
    /// Декодування обфускованих payload від MoonAnime/Tortuga плеєрів.
    /// Винесено з 2 копій у *Invoke.cs (Mikai, AnimeON) — ідентичний код.
    /// </summary>
    public static class TortugaDecoder
    {
        /// <summary>
        /// Декодує base64-payload з заголовком (state + key) та XOR-шифруванням.
        /// Формат: [1 byte state][32 bytes key][payload...] — кожен байт XOR з (key[i%32] ^ state).
        /// </summary>
        public static string MoonDecode(string base64Input)
        {
            try
            {
                byte[] raw = Convert.FromBase64String(base64Input);
                const int KeySize = 32;
                const int HeaderSize = 1 + KeySize;

                if (raw.Length < HeaderSize)
                    return null;

                byte state = raw[0];
                byte[] key = new byte[KeySize];
                Array.Copy(raw, 1, key, 0, KeySize);

                int payloadLen = raw.Length - HeaderSize;
                byte[] payload = new byte[payloadLen];
                Array.Copy(raw, HeaderSize, payload, 0, payloadLen);

                for (int i = 0; i < payload.Length; i++)
                {
                    byte encrypted = payload[i];
                    byte keyByte = key[i % KeySize];

                    payload[i] = (byte)(encrypted ^ keyByte ^ state);
                    state = (byte)((encrypted + keyByte) & 0xFF);
                }

                return Encoding.UTF8.GetString(payload);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Простий XOR-дешифрувач: data XOR key (циклічно).
        /// Використовується для декодування файлів Tortuga/MoonAnime.
        /// </summary>
        public static string MoonXorDecrypt(string file, string key)
        {
            try
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                byte[] data = Convert.FromBase64String(file);

                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
                }

                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }
    }
}
