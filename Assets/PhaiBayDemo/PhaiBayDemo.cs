using UnityEngine;

/// <summary>
/// Test UI for the PhaiBay SDK. Open Assets/PhaiBayDemo/PhaiBayDemo.unity and press Play.
/// Fill the server URL + API key (Dashboard > Apps) then press Initialize.
/// </summary>
public class PhaiBayDemo : MonoBehaviour
{
    public string serverUrl = "http://localhost:8080";
    public string apiKey = "";
    public bool debugLog = true;

    int _level = 1;
    string _userId = "player_001";
    string _status = "";
    Vector2 _scroll;

    void Awake()
    {
        serverUrl = PlayerPrefs.GetString("demo_server", serverUrl);
        apiKey = PlayerPrefs.GetString("demo_key", apiKey);
    }

    void OnGUI()
    {
        float scale = Mathf.Max(1f, Screen.height / 720f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        var w = Screen.width / scale;
        var h = Screen.height / scale;

        GUILayout.BeginArea(new Rect(10, 10, Mathf.Min(520, w - 20), h - 20), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label("<b><size=18>PhaiBay SDK Demo</size></b>", new GUIStyle(GUI.skin.label) { richText = true });

        GUI.enabled = !PhaiBay.IsInitialized;
        GUILayout.Label("Server URL");
        serverUrl = GUILayout.TextField(serverUrl);
        GUILayout.Label("API key");
        apiKey = GUILayout.TextField(apiKey);
        debugLog = GUILayout.Toggle(debugLog, "Debug log");
        if (GUILayout.Button("Initialize", GUILayout.Height(36)))
        {
            PlayerPrefs.SetString("demo_server", serverUrl);
            PlayerPrefs.SetString("demo_key", apiKey);
            PhaiBay.Initialize(apiKey.Trim(), serverUrl.Trim(), debugLog);
            _status = PhaiBay.IsInitialized ? "Initialized" : "Init failed - see console";
        }
        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.Label($"Instance id: {PhaiBay.AppInstanceId}\nSession: {PhaiBay.SessionId}\nQueued: {PhaiBay.PendingEventCount}\n{_status}");

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("level_start", GUILayout.Height(32)))
            PhaiBay.LogEvent(PhaiBay.Events.LevelStart, PhaiBay.Params.Level, _level);
        if (GUILayout.Button("level_end (win)", GUILayout.Height(32)))
        {
            PhaiBay.LogEvent(PhaiBay.Events.LevelEnd,
                new PhaiBay.Parameter(PhaiBay.Params.Level, _level),
                new PhaiBay.Parameter(PhaiBay.Params.Success, true),
                new PhaiBay.Parameter("time_sec", Random.Range(20f, 120f)),
                new PhaiBay.Parameter("stars", Random.Range(1, 4)));
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
                new PhaiBay.Parameter(PhaiBay.Params.AdFormat, Random.value > 0.5f ? "interstitial" : "rewarded"),
                new PhaiBay.Parameter(PhaiBay.Params.Value, Random.Range(0.001f, 0.02f)));
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
        if (GUILayout.Button("Flush now", GUILayout.Height(32))) PhaiBay.Flush();
        if (GUILayout.Button("Log 500 random", GUILayout.Height(32)))
        {
            for (int i = 0; i < 500; i++)
                PhaiBay.LogEvent("stress_test", new PhaiBay.Parameter("i", i), new PhaiBay.Parameter("r", Random.value));
        }
        if (GUILayout.Button("Invalid name", GUILayout.Height(32)))
            PhaiBay.LogEvent("bad name!"); // rejected client-side with a warning
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
