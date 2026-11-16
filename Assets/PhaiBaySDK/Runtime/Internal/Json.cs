namespace PhaiBay
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static partial class Analytics
{
    /// <summary>Tiny allocation-light JSON writer (JsonUtility can't do dictionaries).</summary>
    internal static class Json
    {
        public static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        /// <summary>Serializes a scalar parameter value. Returns false for unsupported values (NaN, Infinity...).</summary>
        public static bool TryValue(object v, int maxStr, out string json)
        {
            switch (v)
            {
                case string s:
                    var sb = new StringBuilder(s.Length + 2);
                    Str(sb, s.Length > maxStr ? s.Substring(0, maxStr) : s);
                    json = sb.ToString();
                    return true;
                case bool b:
                    json = b ? "1" : "0";
                    return true;
                case long l:
                    json = l.ToString(CultureInfo.InvariantCulture);
                    return true;
                case int i:
                    json = i.ToString(CultureInfo.InvariantCulture);
                    return true;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) { json = null; return false; }
                    json = d.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                case float f:
                    if (float.IsNaN(f) || float.IsInfinity(f)) { json = null; return false; }
                    json = ((double)f).ToString("R", CultureInfo.InvariantCulture);
                    return true;
            }
            json = null;
            return false;
        }

        /// <summary>Parses {"k":"v","n":1} written by this SDK (flat, string / number values only).</summary>
        public static Dictionary<string, object> ParseFlatObject(string s)
        {
            var d = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(s)) return d;
            int i = 0;
            try
            {
                Skip(s, ref i); if (s[i++] != '{') return d;
                while (true)
                {
                    Skip(s, ref i);
                    if (s[i] == '}') break;
                    string key = ReadString(s, ref i);
                    Skip(s, ref i); i++; // ':'
                    Skip(s, ref i);
                    object val;
                    if (s[i] == '"') val = ReadString(s, ref i);
                    else
                    {
                        int st = i;
                        while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0) i++;
                        var num = s.Substring(st, i - st);
                        if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) val = l;
                        else val = double.Parse(num, CultureInfo.InvariantCulture);
                    }
                    d[key] = val;
                    Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    break;
                }
            }
            catch (Exception) { /* corrupted prefs -> keep what we parsed */ }
            return d;
        }

        static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++; // closing quote
            return sb.ToString();
        }
    }
}
}
}
