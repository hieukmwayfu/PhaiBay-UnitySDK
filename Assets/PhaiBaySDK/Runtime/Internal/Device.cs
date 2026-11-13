using System;
using System.Globalization;
using UnityEngine;

public static partial class PhaiBay
{
    /// <summary>Device context. Values are computed once on the main thread (Unity APIs are main-thread only).</summary>
    internal static class Device
    {
        public static string Platform = "";
        public static string Language = "";
        public static string Country = ""; // fallback only; the server prefers GeoIP / CDN country

        public static void Init()
        {
            Platform = PlatformName();
            string lang = "", country = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (var def = locale.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    lang = def.Call<string>("getLanguage");
                    country = def.Call<string>("getCountry");
                }
            }
            catch (Exception) { }
#endif
            if (string.IsNullOrEmpty(lang) || string.IsNullOrEmpty(country))
            {
                try
                {
                    var name = CultureInfo.CurrentCulture.Name; // e.g. "vi-VN"
                    var parts = name.Split('-');
                    if (string.IsNullOrEmpty(lang) && parts.Length > 0) lang = parts[0];
                    if (string.IsNullOrEmpty(country) && parts.Length > 1) country = parts[parts.Length - 1];
                }
                catch (Exception) { }
            }
            if (string.IsNullOrEmpty(lang)) lang = LangFromSystem(Application.systemLanguage);
            Language = (lang ?? "").ToLowerInvariant();
            Country = country != null && country.Length == 2 ? country.ToUpperInvariant() : "";
        }

        static string PlatformName()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                case RuntimePlatform.WindowsPlayer: return "Windows";
                case RuntimePlatform.OSXPlayer: return "macOS";
                case RuntimePlatform.LinuxPlayer: return "Linux";
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.LinuxEditor: return "Editor";
                default: return Application.platform.ToString();
            }
        }

        static string LangFromSystem(SystemLanguage l)
        {
            switch (l)
            {
                case SystemLanguage.Vietnamese: return "vi";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Arabic: return "ar";
                default: return "";
            }
        }
    }
}
