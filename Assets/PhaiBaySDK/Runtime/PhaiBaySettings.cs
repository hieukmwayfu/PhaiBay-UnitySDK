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

    [Tooltip("App key chosen when the app was created in the dashboard (e.g. com.company.game). Identifies which app the events belong to.")]
    [FormerlySerializedAs("apiKey")]
    public string appKey = "";

    [Tooltip("Server base URL, e.g. https://analytics.example.com")]
    public string serverUrl = "";

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

    [Tooltip("Log every event / upload to the Unity console")]
    public bool debugLog = false;
}
