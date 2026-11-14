using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// SDK configuration. Open it with the menu <b>PhaiBay > Settings</b> (creates
/// Assets/Resources/PhaiBaySettings.asset). When it lives in a Resources folder the SDK
/// initializes itself before the first scene loads.
/// </summary>
[CreateAssetMenu(menuName = "PhaiBay/Settings", fileName = "PhaiBaySettings")]
public class PhaiBaySettings : ScriptableObject
{
    public const string ResourceName = "PhaiBaySettings";

    /// <summary>
    /// Server used when serverUrl is left empty. Keep this domain stable: shipped game builds keep
    /// calling it, so moving to another VPS = point this domain's DNS at the new machine.
    /// </summary>
    public const string DefaultServerUrl = "https://phaibay.dynv6.net";

    [Tooltip("App key chosen when the app was created in the dashboard (e.g. com.company.game). Identifies which app the events belong to.")]
    [FormerlySerializedAs("apiKey")]
    public string appKey = "";

    [Tooltip("Server base URL. Leave empty to use the default PhaiBay server (PhaiBaySettings.DefaultServerUrl).")]
    public string serverUrl = "";

    /// <summary>serverUrl, or DefaultServerUrl when it is empty.</summary>
    public string ServerUrlOrDefault => string.IsNullOrWhiteSpace(serverUrl) ? DefaultServerUrl : serverUrl.Trim();

    [Tooltip("Initialize automatically on startup (RuntimeInitializeOnLoadMethod)")]
    public bool autoInitialize = true;

    [Tooltip("Also send automatic events: first_open, session_start. Off = only the events you log yourself.")]
    public bool autoTrackEvents = false;

    [Tooltip("Seconds between uploads")]
    [Range(2, 300)] public float flushIntervalSeconds = 15f;

    [Tooltip("Upload immediately when this many events are queued")]
    [Range(1, 500)] public int flushAtCount = 50;

    [Tooltip("Max events per HTTP request")]
    [Range(10, 1000)] public int maxBatchSize = 200;

    [Tooltip("Max events kept on device while offline (oldest dropped)")]
    [Range(100, 50000)] public int maxQueueSize = 5000;

    [Tooltip("A new session id is created after the app was in background this long")]
    [Range(10, 7200)] public int sessionTimeoutSeconds = 1800;

    [Tooltip("Send events while running in the Unity Editor")]
    public bool sendInEditor = true;

    [Tooltip("Only Android builds record real data. In the Editor and other platforms (Windows, Mac, Linux...) " +
             "events are sent as a dry run: the server validates the app key, connection and payload but stores nothing.")]
    public bool realDataOnlyOnAndroid = true;

    [Tooltip("Log every event / upload to the Unity console")]
    public bool debugLog = false;
}
