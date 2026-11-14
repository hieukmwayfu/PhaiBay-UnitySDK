using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Test UI for the PhaiBay SDK. Open Assets/PhaiBayDemo/PhaiBayDemo.unity and press Play.
/// If Resources/PhaiBaySettings has an App Key the SDK is already initialized; otherwise fill
/// Server URL + App Key (Dashboard > select app > SDK setup) and press Initialize.
/// The log panel shows every Unity log (SDK warnings / upload errors included), also on device builds.
/// </summary>
public class PhaiBayDemo : MonoBehaviour
{
    public string serverUrl = PhaiBaySettings.DefaultServerUrl;
    public string appKey = "";
    public bool debugLog = true;
    [Tooltip("Lines kept in the on-screen log")]
    public int maxLogLines = 300;

    int _level = 1;
    string _userId = "player_001";
    string _status = "";
    Vector2 _scroll, _logScroll;
    bool _onlySdk;
    bool _autoScroll = true;

    struct Line { public LogType Type; public string Text; public string Stack; }
    static readonly object LogLock = new object();
    readonly List<Line> _lines = new List<Line>();
    int _errorCount;
    GUIStyle _logStyle;

    void Awake()
    {
        serverUrl = PlayerPrefs.GetString("demo_server_url", serverUrl);
        appKey = PlayerPrefs.GetString("demo_key", appKey);
    }

    void OnEnable() => Application.logMessageReceivedThreaded += OnLog;
    void OnDisable() => Application.logMessageReceivedThreaded -= OnLog;

    void Start()
    {
        if (PhaiBay.IsInitialized)
        {
            _status = "Auto-initialized from Resources/PhaiBaySettings";
            PhaiBay.DebugLog = debugLog;
        }
        Debug.Log($"[Demo] {Application.productName} {Application.version} on {SystemInfo.operatingSystem} / {SystemInfo.deviceModel}");
    }

    // called from any thread
    void OnLog(string msg, string stack, LogType type)
    {
        lock (LogLock)
        {
            _lines.Add(new Line
            {
                Type = type,
                Text = DateTime.Now.ToString("HH:mm:ss") + "  " + msg,
                Stack = type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? stack : null,
            });
            if (type != LogType.Log && type != LogType.Warning) _errorCount++;
            if (_lines.Count > maxLogLines) _lines.RemoveRange(0, _lines.Count - maxLogLines);
        }
    }

    void OnGUI()
    {
        float scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        var w = Screen.width / scale;
        var h = Screen.height / scale;
        if (_logStyle == null) _logStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 12 };

        // landscape: controls left, log right. portrait: controls top, log bottom.
        Rect controls, log;
        if (w >= h)
        {
            float cw = Mathf.Min(520, w * 0.5f);
            controls = new Rect(10, 10, cw - 15, h - 20);
            log = new Rect(cw + 5, 10, w - cw - 15, h - 20);
        }
        else
        {
            controls = new Rect(10, 10, w - 20, h * 0.55f - 15);
            log = new Rect(10, h * 0.55f + 5, w - 20, h * 0.45f - 15);
        }
        DrawControls(controls);
        DrawLog(log);
    }

    void DrawControls(Rect area)
    {
        GUILayout.BeginArea(area, GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label("<b><size=18>PhaiBay SDK Demo</size></b>", new GUIStyle(GUI.skin.label) { richText = true });

        GUI.enabled = !PhaiBay.IsInitialized;
        GUILayout.Label("Server URL");
        serverUrl = GUILayout.TextField(serverUrl);
        GUILayout.Label("App key");
        appKey = GUILayout.TextField(appKey);
        if (GUILayout.Button("Initialize", GUILayout.Height(36)))
        {
            PlayerPrefs.SetString("demo_server_url", serverUrl);
            PlayerPrefs.SetString("demo_key", appKey);
            PhaiBay.Initialize(appKey.Trim(), serverUrl.Trim(), debugLog);
            _status = PhaiBay.IsInitialized ? "Initialized" : "Init failed - see log";
        }
        GUI.enabled = true;

        bool cur = PhaiBay.IsInitialized ? PhaiBay.DebugLog : debugLog;
        bool dbg = GUILayout.Toggle(cur, "SDK debug log (every event / upload)");
        if (dbg != cur) { debugLog = dbg; PhaiBay.DebugLog = dbg; }

        GUILayout.Space(8);
        GUILayout.Label(PhaiBay.IsInitialized
            ? (PhaiBay.IsDryRun ? "<color=#ffd166><b>TEST MODE</b> - validated by server, not stored (Android only = real data)</color>"
                                : "<color=#6bdc8b><b>LIVE</b> - events are recorded</color>")
            : "Not initialized", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
        GUILayout.Label($"Instance id: {PhaiBay.AppInstanceId}\nSession: {PhaiBay.SessionId}\nQueued: {PhaiBay.PendingEventCount}\n" +
                        $"Last upload: {PhaiBay.LastUploadStatus ?? "-"}\n{_status}");

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("level_start", GUILayout.Height(32)))
            PhaiBay.LogEvent(PhaiBay.Events.LevelStart, PhaiBay.Params.Level, _level);
        if (GUILayout.Button("level_end (win)", GUILayout.Height(32)))
        {
            PhaiBay.LogEvent(PhaiBay.Events.LevelEnd,
                new PhaiBay.Parameter(PhaiBay.Params.Level, _level),
                new PhaiBay.Parameter(PhaiBay.Params.Success, true),
                new PhaiBay.Parameter("stars", UnityEngine.Random.Range(1, 4)));
            _level++;
        }
        if (GUILayout.Button("level_end (lose)", GUILayout.Height(32)))
            PhaiBay.LogEvent(PhaiBay.Events.LevelEnd,
                new PhaiBay.Parameter(PhaiBay.Params.Level, _level),
                new PhaiBay.Parameter(PhaiBay.Params.Success, false));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("purchase $0.99", GUILayout.Height(32)))
            PhaiBay.LogEvent(PhaiBay.Events.Purchase,
                new PhaiBay.Parameter(PhaiBay.Params.ItemId, "gem_pack_small"),
                new PhaiBay.Parameter(PhaiBay.Params.Value, 0.99),
                new PhaiBay.Parameter(PhaiBay.Params.Currency, "USD"));
        if (GUILayout.Button("ad_impression", GUILayout.Height(32)))
            PhaiBay.LogEvent(PhaiBay.Events.AdImpression,
                new PhaiBay.Parameter(PhaiBay.Params.AdFormat, UnityEngine.Random.value > 0.5f ? "interstitial" : "rewarded"),
                new PhaiBay.Parameter(PhaiBay.Params.Value, UnityEngine.Random.Range(0.001f, 0.02f)));
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        _userId = GUILayout.TextField(_userId);
        if (GUILayout.Button("SetUserId", GUILayout.Width(110))) PhaiBay.SetUserId(_userId);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("vip = gold")) PhaiBay.SetUserProperty("vip", "gold");
        if (GUILayout.Button("vip = none")) PhaiBay.SetUserProperty("vip", null);
        if (GUILayout.Button("ab_group = B")) PhaiBay.SetUserProperty("ab_group", "B");
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Flush now", GUILayout.Height(32))) { PhaiBay.Flush(); Debug.Log("[Demo] flush requested"); }
        if (GUILayout.Button("Log 500 random", GUILayout.Height(32)))
        {
            for (int i = 0; i < 500; i++)
                PhaiBay.LogEvent("stress_test", new PhaiBay.Parameter("i", i), new PhaiBay.Parameter("r", UnityEngine.Random.value));
            Debug.Log("[Demo] queued 500 stress_test events");
        }
        if (GUILayout.Button("Invalid name", GUILayout.Height(32)))
            PhaiBay.LogEvent("bad name!"); // rejected client-side with a warning
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawLog(Rect area)
    {
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label(_errorCount > 0 ? $"<b>Log</b>  <color=#ff6b6b>{_errorCount} error(s)</color>" : "<b>Log</b>", _logStyle);
        GUILayout.FlexibleSpace();
        _onlySdk = GUILayout.Toggle(_onlySdk, "PhaiBay only");
        _autoScroll = GUILayout.Toggle(_autoScroll, "Auto scroll");
        if (GUILayout.Button("Copy", GUILayout.Width(60))) GUIUtility.systemCopyBuffer = Dump();
        if (GUILayout.Button("Clear", GUILayout.Width(60))) lock (LogLock) { _lines.Clear(); _errorCount = 0; }
        GUILayout.EndHorizontal();

        Line[] snap;
        lock (LogLock) snap = _lines.ToArray();
        if (_autoScroll) _logScroll.y = float.MaxValue;
        _logScroll = GUILayout.BeginScrollView(_logScroll);
        foreach (var l in snap)
        {
            if (_onlySdk && l.Text.IndexOf("[PhaiBay]", StringComparison.Ordinal) < 0) continue;
            var color = l.Type == LogType.Log ? "#d0d0d0" : l.Type == LogType.Warning ? "#ffd166" : "#ff6b6b";
            var text = Escape(l.Text);
            if (l.Stack != null) text += "\n<size=10>" + Escape(l.Stack.Trim()) + "</size>";
            GUILayout.Label($"<color={color}>{text}</color>", _logStyle);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    string Dump()
    {
        var sb = new StringBuilder();
        lock (LogLock)
            foreach (var l in _lines)
            {
                sb.Append('[').Append(l.Type).Append("] ").AppendLine(l.Text);
                if (l.Stack != null) sb.AppendLine(l.Stack.Trim());
            }
        return sb.ToString();
    }

    // rich text: stop log content from being parsed as tags
    static string Escape(string s) => s.Replace("<", "<​");
}
