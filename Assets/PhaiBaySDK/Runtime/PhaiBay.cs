namespace PhaiBay
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// PhaiBay Analytics - Firebase-like API.
///
///   Analytics.Initialize("com.company.game");   // default server; or a PhaiBaySettings asset in Resources
///   Analytics.LogEvent("level_start", "level", 5);
///   Analytics.LogEvent("level_end", new Parameter("level", 5), new Parameter("success", true));
///   Analytics.SetUserProperty("vip", "gold");
///
/// LogEvent can be called from any thread; Initialize / SetUserId / SetUserProperty from the main thread.
/// Events logged before Initialize are kept and sent after it.
/// Events are queued, persisted to disk on pause/quit and uploaded in gzip batches with retry.
/// </summary>
public static partial class Analytics
{
    public const string SdkVersion = "unity-0.1.3";

    // limits (same as the server)
    const int MaxParams = 25;
    const int MaxUserProps = 25;
    const int MaxParamStr = 100;
    const int MaxUserPropStr = 36;
    static readonly Regex NameRe = new Regex("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.Compiled);
    static readonly string[] ReservedPrefixes = { "firebase_", "google_", "ga_" };

    static readonly object Lock = new object();
    static readonly List<string> Queue = new List<string>(); // serialized events (JSON objects)
    static readonly Dictionary<string, object> UserProps = new Dictionary<string, object>();
    static string _userPropsJson; // cached snapshot, null = dirty
    static int _queueVersion; // bumped on every queue change, so the runner knows when to re-persist

    internal static PhaiBaySettings Settings;
    internal static string AppKey;
    internal static string Endpoint;
    internal static bool Initialized;
    static bool _enabled = true;
    static string _uid;
    static string _sessionId;
    static string _appVersion;

    /// <summary>Pseudo user id stored on device (like Firebase app instance id).</summary>
    public static string AppInstanceId { get; private set; }

    /// <summary>Current session id.</summary>
    public static string SessionId { get { lock (Lock) return _sessionId; } }

    public static bool IsInitialized => Initialized;

    /// <summary>
    /// True when events are only validated by the server, never stored (Editor / desktop test runs).
    /// See PhaiBaySettings.realDataOnlyOnAndroid.
    /// </summary>
    public static bool IsDryRun => Settings != null && Settings.realDataOnlyOnAndroid && Application.platform != RuntimePlatform.Android;

    /// <summary>Result of the last upload attempt (for debugging), e.g. "OK 200: sent 12 events". Null before the first upload.</summary>
    public static string LastUploadStatus { get { lock (Lock) return _lastUploadStatus; } }
    static string _lastUploadStatus;

    /// <summary>Log every event / upload to the Unity console (overrides PhaiBaySettings.debugLog at runtime).</summary>
    public static bool DebugLog
    {
        get => _debugOverride ?? (Settings != null && Settings.debugLog);
        set => _debugOverride = value; // not written to the asset (would persist in the Editor)
    }
    static bool? _debugOverride;

    /// <summary>Events waiting to be uploaded.</summary>
    public static int PendingEventCount => QueueCount;

    // ------------------------------------------------------------------ init

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoInit()
    {
        var s = Resources.Load<PhaiBaySettings>(PhaiBaySettings.ResourceName);
        if (s != null && s.autoInitialize && !string.IsNullOrEmpty(s.appKey))
            Initialize(s);
    }

    /// <summary>Initialize with the app key (the server is fixed: PhaiBaySettings.DefaultServerUrl).</summary>
    public static void Initialize(string appKey, bool debugLog = false)
    {
        var s = ScriptableObject.CreateInstance<PhaiBaySettings>();
        s.appKey = appKey;
        s.debugLog = debugLog;
        Initialize(s);
    }

    public static void Initialize(PhaiBaySettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (Initialized) { Log("already initialized"); return; }
        if (string.IsNullOrEmpty(settings.appKey))
        {
            Debug.LogError("[PhaiBay] appKey is required");
            return;
        }
        Settings = settings;
        AppKey = settings.appKey.Trim();
        Endpoint = settings.ServerUrl.TrimEnd('/') + "/v1/events";
        _appVersion = Application.version;

        AppInstanceId = PlayerPrefs.GetString("phaibay_iid", "");
        bool firstOpen = string.IsNullOrEmpty(AppInstanceId);
        if (firstOpen)
        {
            AppInstanceId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("phaibay_iid", AppInstanceId);
        }
        _uid = PlayerPrefs.GetString("phaibay_uid", "");
        _enabled = PlayerPrefs.GetInt("phaibay_enabled", 1) == 1;
        LoadUserProps();
        PlayerPrefs.Save();

        Initialized = true;
        PhaiBayRunner.Create(); // loads persisted queue, uploads, tracks lifecycle

        if (IsDryRun)
            Debug.Log("[PhaiBay] TEST MODE on " + Application.platform + ": events are validated by the server but NOT stored. " +
                      "Only Android builds record real data (PhaiBaySettings.realDataOnlyOnAndroid).");

        if (firstOpen && settings.autoTrackEvents) LogEventInternal(Events.FirstOpen, null);
        StartSession();
        Log("initialized, instance id = " + AppInstanceId);
    }

    // ------------------------------------------------------------------ public API

    public static void LogEvent(string name) => LogEventInternal(name, null);

    public static void LogEvent(string name, string parameterName, string value) =>
        LogEventInternal(name, new[] { new Parameter(parameterName, value) });

    public static void LogEvent(string name, string parameterName, long value) =>
        LogEventInternal(name, new[] { new Parameter(parameterName, value) });

    public static void LogEvent(string name, string parameterName, int value) =>
        LogEventInternal(name, new[] { new Parameter(parameterName, value) });

    public static void LogEvent(string name, string parameterName, double value) =>
        LogEventInternal(name, new[] { new Parameter(parameterName, value) });

    public static void LogEvent(string name, string parameterName, bool value) =>
        LogEventInternal(name, new[] { new Parameter(parameterName, value) });

    public static void LogEvent(string name, params Parameter[] parameters) =>
        LogEventInternal(name, parameters);

    public static void LogEvent(string name, IDictionary<string, object> parameters)
    {
        Parameter[] ps = null;
        if (parameters != null)
        {
            ps = new Parameter[parameters.Count];
            int i = 0;
            foreach (var kv in parameters) ps[i++] = ToParameter(kv.Key, kv.Value);
        }
        LogEventInternal(name, ps);
    }

    /// <summary>Custom user id (your account id). Pass null to clear.</summary>
    public static void SetUserId(string userId)
    {
        lock (Lock) _uid = string.IsNullOrEmpty(userId) ? "" : Clip(userId, 128);
        PlayerPrefs.SetString("phaibay_uid", _uid ?? "");
    }

    /// <summary>User property attached to every following event. Value null removes it.</summary>
    public static void SetUserProperty(string name, string value)
    {
        if (!ValidName(name, "user property")) return;
        lock (Lock)
        {
            if (value == null) UserProps.Remove(name);
            else if (UserProps.Count < MaxUserProps || UserProps.ContainsKey(name)) UserProps[name] = Clip(value, MaxUserPropStr);
            else { Warn("too many user properties, ignored " + name); return; }
            _userPropsJson = null;
        }
        SaveUserProps();
    }

    public static void SetUserProperty(string name, long value)
    {
        if (!ValidName(name, "user property")) return;
        lock (Lock)
        {
            if (UserProps.Count >= MaxUserProps && !UserProps.ContainsKey(name)) { Warn("too many user properties, ignored " + name); return; }
            UserProps[name] = value;
            _userPropsJson = null;
        }
        SaveUserProps();
    }

    /// <summary>Enable / disable collection (persisted). Disabled = events are dropped, nothing is sent.</summary>
    public static void SetAnalyticsCollectionEnabled(bool enabled)
    {
        _enabled = enabled;
        PlayerPrefs.SetInt("phaibay_enabled", enabled ? 1 : 0);
        if (!enabled) lock (Lock) { Queue.Clear(); _queueVersion++; }
    }

    /// <summary>Upload queued events now.</summary>
    public static void Flush() => PhaiBayRunner.RequestFlush();

    /// <summary>Clears ids and user properties (e.g. GDPR "delete my data" on device).</summary>
    public static void ResetAnalyticsData()
    {
        lock (Lock)
        {
            Queue.Clear();
            _queueVersion++;
            UserProps.Clear();
            _userPropsJson = null;
            _uid = "";
            AppInstanceId = Guid.NewGuid().ToString("N");
        }
        PlayerPrefs.SetString("phaibay_iid", AppInstanceId);
        PlayerPrefs.DeleteKey("phaibay_uid");
        PlayerPrefs.DeleteKey("phaibay_up");
        PlayerPrefs.Save();
        PhaiBayRunner.DeletePersistedQueue();
    }

    // ------------------------------------------------------------------ internals

    internal static void StartSession()
    {
        lock (Lock) _sessionId = Guid.NewGuid().ToString("N").Substring(0, 16);
        if (Settings != null && Settings.autoTrackEvents) LogEventInternal(Events.SessionStart, null);
    }

    static Parameter ToParameter(string k, object v)
    {
        switch (v)
        {
            case null: return new Parameter(k, "");
            case string s: return new Parameter(k, s);
            case bool b: return new Parameter(k, b);
            case int i: return new Parameter(k, i);
            case long l: return new Parameter(k, l);
            case short sh: return new Parameter(k, sh);
            case byte by: return new Parameter(k, by);
            case uint ui: return new Parameter(k, (long)ui);
            case float f: return new Parameter(k, f);
            case double d: return new Parameter(k, d);
            case decimal m: return new Parameter(k, (double)m);
            case Enum e: return new Parameter(k, e.ToString());
            default: return new Parameter(k, Convert.ToString(v, CultureInfo.InvariantCulture));
        }
    }

    static bool ValidName(string name, string what)
    {
        if (name == null || !NameRe.IsMatch(name))
        {
            Warn("invalid " + what + " name '" + name + "' (1-40 chars, letters/digits/_, must start with a letter)");
            return false;
        }
        foreach (var p in ReservedPrefixes)
            if (name.StartsWith(p, StringComparison.Ordinal)) { Warn("reserved prefix in '" + name + "'"); return false; }
        return true;
    }

    internal static void LogEventInternal(string name, Parameter[] parameters)
    {
        if (!_enabled) return;
        if (!ValidName(name, "event")) return;

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sb = new StringBuilder(128);
        sb.Append("{\"name\":"); Json.Str(sb, name);
        sb.Append(",\"ts\":").Append(ts.ToString(CultureInfo.InvariantCulture));

        if (parameters != null && parameters.Length > 0)
        {
            sb.Append(",\"params\":{");
            int n = 0;
            var seen = new HashSet<string>();
            foreach (var p in parameters)
            {
                if (p == null || !ValidName(p.Name, "parameter") || !seen.Add(p.Name)) continue;
                if (n >= MaxParams) { Warn(name + ": more than " + MaxParams + " params, extra dropped"); break; }
                if (!Json.TryValue(p.Value, MaxParamStr, out var js)) continue;
                if (n++ > 0) sb.Append(',');
                Json.Str(sb, p.Name); sb.Append(':').Append(js);
            }
            sb.Append('}');
        }

        lock (Lock)
        {
            if (!string.IsNullOrEmpty(_sessionId)) { sb.Append(",\"sid\":"); Json.Str(sb, _sessionId); }
            if (!string.IsNullOrEmpty(_uid)) { sb.Append(",\"uid\":"); Json.Str(sb, _uid); }
            if (!string.IsNullOrEmpty(_appVersion)) { sb.Append(",\"v\":"); Json.Str(sb, _appVersion); }
            var up = UserPropsJson();
            if (up != null) sb.Append(",\"up\":").Append(up);
            sb.Append('}');

            int max = Settings != null ? Settings.maxQueueSize : 5000;
            if (Queue.Count >= max) Queue.RemoveRange(0, Queue.Count - max + 1); // drop oldest
            Queue.Add(sb.ToString());
            _queueVersion++;
        }
        Log("event " + sb);
        PhaiBayRunner.NotifyEnqueued();
    }

    // must hold Lock
    static string UserPropsJson()
    {
        if (UserProps.Count == 0) return null;
        if (_userPropsJson != null) return _userPropsJson;
        var sb = new StringBuilder(64).Append('{');
        int n = 0;
        foreach (var kv in UserProps)
        {
            if (!Json.TryValue(kv.Value, MaxUserPropStr, out var js)) continue;
            if (n++ > 0) sb.Append(',');
            Json.Str(sb, kv.Key); sb.Append(':').Append(js);
        }
        return _userPropsJson = sb.Append('}').ToString();
    }

    static void SaveUserProps()
    {
        string s;
        lock (Lock) s = UserPropsJson() ?? "";
        PlayerPrefs.SetString("phaibay_up", s);
    }

    static void LoadUserProps()
    {
        // stored as flat JSON object of string/number values written by UserPropsJson()
        var s = PlayerPrefs.GetString("phaibay_up", "");
        lock (Lock)
        {
            UserProps.Clear();
            foreach (var kv in Json.ParseFlatObject(s)) UserProps[kv.Key] = kv.Value;
            _userPropsJson = null;
        }
    }

    // ---- queue access for the runner ----
    internal static int QueueCount { get { lock (Lock) return Queue.Count; } }
    internal static int QueueVersion { get { lock (Lock) return _queueVersion; } }

    /// <summary>Queue copy + its version, taken atomically (for persisting).</summary>
    internal static List<string> Snapshot(out int version) { lock (Lock) { version = _queueVersion; return new List<string>(Queue); } }

    internal static List<string> PeekBatch(int max)
    {
        lock (Lock) return Queue.GetRange(0, Math.Min(max, Queue.Count));
    }

    /// <summary>Remove the first n events (they were sent). Safe even if the queue was trimmed meanwhile.</summary>
    internal static void RemoveSent(List<string> sent)
    {
        lock (Lock)
        {
            int n = 0;
            while (n < sent.Count && n < Queue.Count && ReferenceEquals(Queue[n], sent[n])) n++;
            Queue.RemoveRange(0, n);
            if (n > 0) _queueVersion++;
        }
    }

    internal static List<string> Snapshot() { lock (Lock) return new List<string>(Queue); }

    internal static void PrependRestored(List<string> restored)
    {
        if (restored.Count == 0) return;
        lock (Lock)
        {
            Queue.InsertRange(0, restored);
            _queueVersion++;
            int max = Settings != null ? Settings.maxQueueSize : 5000;
            if (Queue.Count > max) Queue.RemoveRange(0, Queue.Count - max);
        }
    }

    internal static string BuildContextJson()
    {
        var sb = new StringBuilder(256).Append('{');
        sb.Append("\"user_id\":"); Json.Str(sb, AppInstanceId);
        lock (Lock) if (!string.IsNullOrEmpty(_uid)) { sb.Append(",\"uid\":"); Json.Str(sb, _uid); }
        sb.Append(",\"app_version\":"); Json.Str(sb, _appVersion);
        sb.Append(",\"platform\":"); Json.Str(sb, Device.Platform);
        sb.Append(",\"os\":"); Json.Str(sb, Clip(SystemInfo.operatingSystem, 64));
        sb.Append(",\"device\":"); Json.Str(sb, Clip(SystemInfo.deviceModel, 64));
        sb.Append(",\"lang\":"); Json.Str(sb, Device.Language);
        sb.Append(",\"country\":"); Json.Str(sb, Device.Country);
        sb.Append(",\"sdk\":"); Json.Str(sb, SdkVersion);
        return sb.Append('}').ToString();
    }

    internal static void ResetForPlayMode()
    {
        lock (Lock)
        {
            Queue.Clear();
            _queueVersion++;
            UserProps.Clear();
            _userPropsJson = null;
            _sessionId = null;
            _uid = null;
            _lastUploadStatus = null;
        }
        // every static must be reset here: with "Enter Play Mode Options" (domain reload off)
        // static fields survive between play sessions
        Initialized = false;
        Settings = null;
        AppKey = null;
        Endpoint = null;
        _enabled = true;
        _debugOverride = null;
        _appVersion = null;
        AppInstanceId = null;
    }

    internal static void SetUploadStatus(string s)
    {
        s = Clip(s, 300);
        lock (Lock) _lastUploadStatus = s;
    }

    internal static string Clip(string s, int n) => s == null ? "" : (s.Length <= n ? s : s.Substring(0, n));

    internal static void Log(string msg)
    {
        if (DebugLog) Debug.Log("[PhaiBay] " + msg);
    }

    internal static void Warn(string msg) => Debug.LogWarning("[PhaiBay] " + msg);
}
}