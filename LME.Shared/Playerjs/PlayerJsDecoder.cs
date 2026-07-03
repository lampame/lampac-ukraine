using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LME.Common.Playerjs
{
    public static partial class PlayerJsDecoder
    {
        [GeneratedRegex(@"atob\(\s*(['""])(?<payload>[^'""]+)\1\s*\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReAtobLiteral();

        [GeneratedRegex(@"JSON\.parse\(\s*(?<fn>[A-Za-z_$][\w$]*)\(\s*(?<quote>['""])(?<payload>.*?)(\k<quote>)\s*\)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ReJsonParseHelper();

        [GeneratedRegex(@"^\s*(?<fn>[A-Za-z_$][\w$]*)\(\s*(?<quote>['""])(?<payload>.*?)(\k<quote>)\s*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ReHelperCall();

        [GeneratedRegex(@",\s*([}\]])")]
        private static partial Regex ReTrailingComma();

        private static readonly UTF8Encoding _utf8Strict = new UTF8Encoding(false, true);
        private static readonly Encoding _latin1 = Encoding.GetEncoding("ISO-8859-1");

        public static PlayerPayload ExtractPlayerPayload(string htmlText)
        {
            string cleanHtml = WebUtility.HtmlDecode(htmlText ?? string.Empty);
            if (string.IsNullOrWhiteSpace(cleanHtml))
                return null;

            var candidates = new List<string> { cleanHtml };
            string decodedScript = DecodeOuterPlayerScript(cleanHtml);
            if (!string.IsNullOrWhiteSpace(decodedScript))
                candidates.Insert(0, decodedScript);

            foreach (string sourceText in candidates)
            {
                string objectText = ExtractObjectByBraces(sourceText, "new Playerjs");
                if (string.IsNullOrWhiteSpace(objectText))
                    objectText = ExtractObjectByBraces(sourceText, "Playerjs({");

                string searchText = string.IsNullOrWhiteSpace(objectText) ? sourceText : objectText;

                string fileValue = ExtractJsValue(searchText, "file");
                if (fileValue == null && !string.IsNullOrWhiteSpace(objectText))
                    fileValue = ExtractJsValue(sourceText, "file");
                if (fileValue == null)
                    continue;

                string titleValue = ExtractJsValue(searchText, "title");
                object parsedFile = ParsePlayerFileValue(fileValue, sourceText);

                return new PlayerPayload
                {
                    Title = Nullish(titleValue),
                    FilePayload = parsedFile
                };
            }

            return null;
        }

        private static object ParsePlayerFileValue(string rawValue, string contextText)
        {
            string text = rawValue?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return rawValue;

            if (text.StartsWith("[") || text.StartsWith("{"))
            {
                JsonNode loaded = LoadJsonLoose(text);
                if (loaded != null)
                    return loaded;
            }

            var parseMatch = ReJsonParseHelper().Match(text);
            if (parseMatch.Success)
            {
                string decoded = DecodeHelperPayload(parseMatch.Groups["fn"].Value, parseMatch.Groups["payload"].Value, contextText);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    JsonNode loaded = LoadJsonLoose(decoded);
                    if (loaded != null)
                        return loaded;
                }
            }

            var helperMatch = ReHelperCall().Match(text);
            if (helperMatch.Success)
            {
                string decoded = DecodeHelperPayload(helperMatch.Groups["fn"].Value, helperMatch.Groups["payload"].Value, contextText);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    JsonNode loaded = LoadJsonLoose(decoded);
                    if (loaded != null)
                        return loaded;

                    return decoded;
                }
            }

            return rawValue;
        }

        private static string DecodeHelperPayload(string helperName, string payload, string contextText)
        {
            if (string.IsNullOrWhiteSpace(helperName))
                return null;

            if (helperName.Equals("atob", StringComparison.OrdinalIgnoreCase))
            {
                byte[] rawBytes = SafeBase64Decode(payload);
                return rawBytes == null ? null : DecodeBytes(rawBytes);
            }

            string helperKey = ExtractHelperKey(contextText, helperName);
            if (string.IsNullOrWhiteSpace(helperKey))
                return null;

            byte[] keyBytes = Encoding.UTF8.GetBytes(helperKey);
            if (keyBytes.Length == 0)
                return null;

            byte[] payloadBytes = SafeBase64Decode(payload);
            if (payloadBytes == null)
                return null;

            var decoded = new byte[payloadBytes.Length];
            for (int index = 0; index < payloadBytes.Length; index++)
                decoded[index] = (byte)(payloadBytes[index] ^ keyBytes[index % keyBytes.Length]);

            return DecodeBytes(decoded);
        }

        private static string ExtractHelperKey(string contextText, string helperName)
        {
            if (string.IsNullOrWhiteSpace(contextText) || string.IsNullOrWhiteSpace(helperName))
                return null;

            string pattern = $@"function\s+{Regex.Escape(helperName)}\s*\([^)]*\)\s*\{{[\s\S]*?var\s+k\s*=\s*(['""])(?<key>.*?)\1";
            var match = Regex.Match(contextText, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return Nullish(match.Groups["key"].Value);
        }

        private static string DecodeOuterPlayerScript(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = ReAtobLiteral().Match(text);
            if (!match.Success)
                return null;

            string rawBase64 = match.Groups["payload"].Value;
            rawBase64 = Regex.Replace(rawBase64, @"\s+", "");

            byte[] rawData = SafeBase64Decode(rawBase64);
            if (rawData == null || rawData.Length <= 32)
                return null;

            byte[] key = rawData.Take(32).ToArray();
            byte[] encryptedData = rawData.Skip(32).ToArray();
            var decoded = new byte[encryptedData.Length];

            for (int index = 0; index < encryptedData.Length; index++)
                decoded[index] = (byte)(encryptedData[index] ^ key[index % key.Length]);

            string decodedStr = DecodeBytes(decoded);
            if (decodedStr != null && (decodedStr.Contains("Playerjs") || decodedStr.Contains("file:")))
                return decodedStr;

            try
            {
                if (rawData.Length > 33)
                {
                    byte state = rawData[0];
                    byte[] moonKey = new byte[32];
                    Array.Copy(rawData, 1, moonKey, 0, 32);

                    int payloadLen = rawData.Length - 33;
                    byte[] moonPayload = new byte[payloadLen];
                    Array.Copy(rawData, 33, moonPayload, 0, payloadLen);

                    for (int i = 0; i < moonPayload.Length; i++)
                    {
                        byte encrypted = moonPayload[i];
                        byte keyByte = moonKey[i % 32];

                        moonPayload[i] = (byte)(encrypted ^ keyByte ^ state);
                        state = (byte)((encrypted + keyByte) & 0xFF);
                    }

                    string moonDecoded = DecodeBytes(moonPayload);
                    if (moonDecoded != null && (moonDecoded.Contains("Playerjs") || moonDecoded.Contains("file:")))
                        return moonDecoded;
                }
            }
            catch { }

            return decodedStr;
        }

        private static byte[] SafeBase64Decode(string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            int remainder = text.Length % 4;
            if (remainder != 0)
                text += new string('=', 4 - remainder);

            try
            {
                return Convert.FromBase64String(text);
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            try
            {
                return _utf8Strict.GetString(data);
            }
            catch
            {
                return _latin1.GetString(data);
            }
        }

        private static string ExtractObjectByBraces(string text, string anchor)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(anchor))
                return null;

            int anchorIndex = text.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (anchorIndex < 0)
                return null;

            int braceIndex = text.IndexOf('{', anchorIndex);
            if (braceIndex < 0)
                return null;

            int depth = 0;
            bool escaped = false;
            char? inString = null;

            for (int index = braceIndex; index < text.Length; index++)
            {
                char current = text[index];

                if (inString.HasValue)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == inString.Value)
                        inString = null;

                    continue;
                }

                if (current == '"' || current == '\'')
                {
                    inString = current;
                    continue;
                }

                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(braceIndex + 1, index - braceIndex - 1);
                }
            }

            return null;
        }

        private static string ExtractJsValue(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key))
                return null;

            var match = Regex.Match(text, $@"\b{Regex.Escape(key)}\b\s*:\s*", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            int index = match.Index + match.Length;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            if (index >= text.Length)
                return null;

            char token = text[index];
            if (token == '"' || token == '\'')
            {
                var (value, _) = ReadJsString(text, index);
                return value;
            }

            if (token == '[')
            {
                int endIndex = FindMatchingBracket(text, index, '[', ']');
                return endIndex >= index ? text.Substring(index, endIndex - index + 1) : null;
            }

            if (token == '{')
            {
                int endIndex = FindMatchingBracket(text, index, '{', '}');
                return endIndex >= index ? text.Substring(index, endIndex - index + 1) : null;
            }

            int stopIndex = index;
            while (stopIndex < text.Length && text[stopIndex] != ',' && text[stopIndex] != '}' && text[stopIndex] != '\n' && text[stopIndex] != '\r')
                stopIndex++;

            return text.Substring(index, stopIndex - index).Trim();
        }

        private static (string value, int nextIndex) ReadJsString(string text, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length)
                return (null, -1);

            char quote = text[startIndex];
            if (quote != '"' && quote != '\'')
                return (null, -1);

            var buffer = new StringBuilder();
            bool escaped = false;

            for (int index = startIndex + 1; index < text.Length; index++)
            {
                char current = text[index];

                if (escaped)
                {
                    buffer.Append(current);
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                    return (buffer.ToString(), index + 1);

                buffer.Append(current);
            }

            return (null, -1);
        }

        private static int FindMatchingBracket(string text, int startIndex, char openChar, char closeChar)
        {
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length || text[startIndex] != openChar)
                return -1;

            int depth = 0;
            bool escaped = false;
            char? inString = null;

            for (int index = startIndex; index < text.Length; index++)
            {
                char current = text[index];

                if (inString.HasValue)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == inString.Value)
                        inString = null;

                    continue;
                }

                if (current == '"' || current == '\'')
                {
                    inString = current;
                    continue;
                }

                if (current == openChar)
                {
                    depth++;
                    continue;
                }

                if (current == closeChar)
                {
                    depth--;
                    if (depth == 0)
                        return index;
                }
            }

            return -1;
        }

        public static JsonNode LoadJsonLoose(string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string normalized = WebUtility.HtmlDecode(text).Replace("\\/", "/");
            string unescapedQuotes = normalized.Replace("\\'", "'").Replace("\\\"", "\"");
            var candidates = new[]
            {
                normalized,
                unescapedQuotes,
                RemoveTrailingCommas(normalized),
                RemoveTrailingCommas(unescapedQuotes)
            };

            foreach (string candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                try
                {
                    return JsonNode.Parse(candidate);
                }
                catch
                {
                }
            }

            return null;
        }

        private static string RemoveTrailingCommas(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? value : ReTrailingComma().Replace(value, "$1");
        }

        public static string Nullish(string value)
        {
            string text = value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (text.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("undefined", StringComparison.OrdinalIgnoreCase))
                return null;

            return text;
        }
    }

    public sealed class PlayerPayload
    {
        public string Title { get; set; }

        public object FilePayload { get; set; }
    }
}
