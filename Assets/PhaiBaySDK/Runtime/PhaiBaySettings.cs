using UnityEngine;


/// <summary>
/// Create via Assets > Create > PhaiBay > Settings, name it "PhaiBaySettings" and put it in a Resources folder
/// to auto-initialize the SDK before the first scene loads.
/// </summary>
[CreateAssetMenu(menuName = "PhaiBay/Settings", fileName = "PhaiBaySettings")]
public class PhaiBaySettings : ScriptableObject
{
    public const string ResourceName = "PhaiBaySettings";

    [Tooltip("API key of the app (Dashboard API: POST /api/apps)")]
    public string apiKey = "";

    [Tooltip("Server base URL, e.g. https://analytics.example.com")]
    public string serverUrl = "http://localhost:8080";

    [Tooltip("Initialize automatically on startup (RuntimeInitializeOnLoadMethod)")]
    public bool autoInitialize = true;

    [Tooltip("Seconds between uploads")]
    [Range(2, 300)] public float flushIntervalSeconds = 15f;

    [Tooltip("Upload immediately when this many events are queued")]
    [Range(1, 500)] public int flushAtCount = 50;

    [Tooltip("Max events per HTTP request")]
    [Range(10, 1000)] public int maxBatchSize = 200;

    [Tooltip("Max events kept on device while offline (oldest dropped)")]
    [Range(100, 50000)] public int maxQueueSize = 5000;

    [Tooltip("A new session starts after the app was in background this long")]
    [Range(10, 7200)] public int sessionTimeoutSeconds = 1800;

    [Tooltip("Send events while running in the Unity Editor")]
    public bool sendInEditor = true;

    [Tooltip("Log every event / upload to the Unity console")]
    public bool debugLog = false;
}
