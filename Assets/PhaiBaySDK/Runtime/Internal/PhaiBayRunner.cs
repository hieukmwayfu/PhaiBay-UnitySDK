namespace PhaiBay
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Hidden DontDestroyOnLoad object: uploads batches, retries with backoff, persists the queue,
/// tracks sessions (session_start).
///
/// Offline / failure handling (like Firebase): events stay queued in memory and are saved to
/// persistentDataPath every few seconds when the queue changed, right after a failed upload and on
/// pause / quit, so a crash or kill loses at most a few seconds of events. A failed batch is retried
/// with the same batch_id (the server de-duplicates it) using exponential backoff (5 s -> 5 min,
/// Retry-After honored). While the device has no network nothing is attempted; uploads resume as
/// soon as it comes back, and on the next launch the saved queue is restored and sent.
/// </summary>
[AddComponentMenu("")]
internal sealed class PhaiBayRunner : MonoBehaviour
{
    static PhaiBayRunner _instance;
    static volatile bool _flushRequested;
    static volatile bool _queueNeedsPersist;

    const float MinBackoff = 5f, MaxBackoff = 300f;
    const float PersistEvery = 10f; // seconds between background saves of a changed queue
    static readonly object FileLock = new object();
    static int _writtenVersion = -1; // queue version last saved to disk (guarded by FileLock)
    const string QueueFile = "phaibay_queue.jsonl";

    float _sinceFlush;
    float _nextAllowedSend;
    float _backoff = MinBackoff;
    bool _sending;
    List<string> _pendingBatch; // failed batch kept with the same batch_id so the server can dedupe the retry
    string _pendingBatchId;

    DateTime _backgroundAt = DateTime.MinValue;
    float _sincePersist;
    int _persistedVersion = -1;
    bool _offline;

    static string QueuePath => Path.Combine(Application.persistentDataPath, QueueFile);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() // supports "Enter Play Mode" without domain reload
    {
        _instance = null;
        _flushRequested = false;
        _queueNeedsPersist = false;
        lock (FileLock) _writtenVersion = -1;
        Analytics.ResetForPlayMode();
    }

    internal static void Create()
    {
        if (_instance != null) return;
        // HideInHierarchy only: HideAndDontSave would keep the object alive after exiting Play mode,
        // so with domain reload disabled the next Play would run two runners (double uploads).
        var go = new GameObject("[PhaiBay]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PhaiBayRunner>();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    internal static void RequestFlush() => _flushRequested = true;
    // LogEvent is allowed from worker threads. Defer the snapshot/file work to Update,
    // but start it on the very next frame instead of waiting for the periodic checkpoint.
    internal static void NotifyEnqueued() => _queueNeedsPersist = true;

    internal static void DeletePersistedQueue()
    {
        lock (FileLock)
        {
            try { if (File.Exists(QueuePath)) File.Delete(QueuePath); } catch (Exception) { }
            _writtenVersion = -1;
        }
    }

    void Awake()
    {
        Device.Init();
        RestoreQueue();
    }

    void Update()
    {
        _sinceFlush += Time.unscaledDeltaTime;

        // save a changed queue in the background so a crash / kill does not lose it
        _sincePersist += Time.unscaledDeltaTime;
        if (_queueNeedsPersist)
        {
            _queueNeedsPersist = false;
            PersistQueueAsync();
        }
        if (_sincePersist >= PersistEvery)
        {
            _sincePersist = 0;
            if (Analytics.QueueVersion != _persistedVersion) PersistQueueAsync();
        }

        // no network: keep everything queued, do not burn retries; resume right away when it is back
        bool offline = Application.internetReachability == NetworkReachability.NotReachable;
        if (offline != _offline)
        {
            _offline = offline;
            if (offline)
            {
                Analytics.SetUploadStatus("Offline - " + Analytics.QueueCount + " events kept, will send when online");
                PersistQueueAsync();
            }
            else
            {
                _nextAllowedSend = 0;
                _backoff = MinBackoff;
                _flushRequested = true;
                Analytics.Log("network back, uploading queued events");
            }
        }
        if (offline) return;

        if (_sending || Time.realtimeSinceStartup < _nextAllowedSend) return;
        var s = Analytics.Settings;
        int count = Analytics.QueueCount;
        if (count == 0) { _flushRequested = false; return; }
        if (_flushRequested || count >= s.flushAtCount || _sinceFlush >= s.flushIntervalSeconds)
        {
            _flushRequested = false;
            _sinceFlush = 0;
            StartCoroutine(Send());
        }
    }

    void OnApplicationPause(bool paused)
    {
        if (!Analytics.Initialized) return;
        if (paused)
        {
            _backgroundAt = DateTime.UtcNow;
            PersistQueue();
            if (!_sending) StartCoroutine(Send()); // best effort before the OS suspends us
        }
        else
        {
            if (_backgroundAt != DateTime.MinValue &&
                (DateTime.UtcNow - _backgroundAt).TotalSeconds >= Analytics.Settings.sessionTimeoutSeconds)
                Analytics.StartSession();
            _backgroundAt = DateTime.MinValue;
            _nextAllowedSend = 0; // network may be back
        }
    }

    void OnApplicationQuit()
    {
        if (!Analytics.Initialized) return;
        PersistQueue();
    }

    IEnumerator Send()
    {
        if (_sending) yield break;
#if UNITY_EDITOR
        if (!Analytics.Settings.sendInEditor) yield break;
#endif
        _sending = true;
        try
        {
            List<string> batch;
            string batchId;
            var head = Analytics.PeekBatch(1);
            if (_pendingBatch != null && head.Count > 0 && ReferenceEquals(head[0], _pendingBatch[0]))
            {
                batch = _pendingBatch;
                batchId = _pendingBatchId;
            }
            else
            {
                batch = Analytics.PeekBatch(Analytics.Settings.maxBatchSize);
                batchId = Guid.NewGuid().ToString("N");
            }
            if (batch.Count == 0) yield break;

            var sb = new StringBuilder(batch.Count * 160 + 512);
            sb.Append("{\"batch_id\":\"").Append(batchId).Append("\",\"sent_at\":")
              .Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
              .Append(",\"ctx\":").Append(Analytics.BuildContextJson())
              .Append(",\"events\":[");
            for (int i = 0; i < batch.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(batch[i]);
            }
            sb.Append("]}");

            byte[] body = Encoding.UTF8.GetBytes(sb.ToString());
            bool gz = false;
#if !UNITY_WEBGL || UNITY_EDITOR
            var zipped = Gzip(body);
            if (zipped != null) { body = zipped; gz = true; }
#endif
            using (var req = new UnityWebRequest(Analytics.Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", Analytics.AppKey);
                bool dry = Analytics.IsDryRun;
                if (dry) req.SetRequestHeader("X-PB-Dry-Run", "1"); // server validates only, stores nothing
                if (gz) req.SetRequestHeader("Content-Encoding", "gzip");
                req.timeout = 20;
                yield return req.SendWebRequest();

                long code = req.responseCode;
                bool netError = req.result == UnityWebRequest.Result.ConnectionError || code == 0;

                if (!netError && code >= 200 && code < 300)
                {
                    Analytics.RemoveSent(batch);
                    _pendingBatch = null;
                    _backoff = MinBackoff;
                    Analytics.SetUploadStatus(dry
                        ? "TEST OK " + code + ": " + batch.Count + " events valid (dry run, not stored)"
                        : "OK " + code + ": sent " + batch.Count + " events");
                    Analytics.Log("sent " + batch.Count + " events (" + body.Length + " bytes): " + req.downloadHandler.text);
                    if (Analytics.QueueCount >= Analytics.Settings.flushAtCount) _flushRequested = true;
                }
                else if (!netError && (code == 400 || code == 413))
                {
                    // malformed / too large: retrying won't help, drop it so the queue keeps moving
                    Analytics.SetUploadStatus("Rejected " + code + ": " + req.downloadHandler.text);
                    Analytics.Warn("server rejected batch (" + code + "): " + req.downloadHandler.text + " - dropped " + batch.Count + " events");
                    Analytics.RemoveSent(batch);
                    _pendingBatch = null;
                }
                else
                {
                    _pendingBatch = batch;
                    _pendingBatchId = batchId;
                    float wait = _backoff;
                    var ra = req.GetResponseHeader("Retry-After");
                    if (!string.IsNullOrEmpty(ra) && float.TryParse(ra, NumberStyles.Float, CultureInfo.InvariantCulture, out var raSec))
                        wait = Mathf.Max(wait, raSec);
                    if (code == 401)
                    {
                        Debug.LogError("[PhaiBay] unknown app key - check PhaiBaySettings.appKey (Dashboard > select app > SDK setup)");
                        wait = MaxBackoff;
                    }
                    Analytics.SetUploadStatus((netError ? "Network error: " + req.error : "HTTP " + code + ": " + req.downloadHandler.text)
                        + " (retry in " + wait.ToString("0") + "s)");
                    _nextAllowedSend = Time.realtimeSinceStartup + wait;
                    PersistQueueAsync(); // keep the unsent batch safe while we wait
                    _backoff = Mathf.Min(_backoff * 2f, MaxBackoff);
                    Analytics.Log("upload failed (" + code + " " + req.error + "), retry in " + wait.ToString("0") + "s");
                }
            }
        }
        finally
        {
            _sending = false;
        }
    }

    static byte[] Gzip(byte[] data)
    {
        try
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress))
                    gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }
        catch (Exception) { return null; } // platform without zlib -> send uncompressed
    }

    /// <summary>Synchronous save (pause / quit: the OS may kill the process right after).</summary>
    void PersistQueue()
    {
        var snap = Analytics.Snapshot(out int version);
        _persistedVersion = version;
        WriteQueueFile(snap, version);
    }

    /// <summary>Snapshot on the main thread, write the file on a worker thread (no frame hitch).</summary>
    void PersistQueueAsync()
    {
        var snap = Analytics.Snapshot(out int version);
        _persistedVersion = version;
        System.Threading.ThreadPool.QueueUserWorkItem(_ => WriteQueueFile(snap, version));
    }

    static void WriteQueueFile(List<string> snap, int version)
    {
        lock (FileLock)
        {
            if (version <= _writtenVersion) return; // a newer snapshot was already written
            try
            {
                if (snap.Count == 0)
                {
                    if (File.Exists(QueuePath)) File.Delete(QueuePath);
                    var backup = QueuePath + ".bak";
                    if (File.Exists(backup)) File.Delete(backup);
                }
                else
                {
                    // write + replace: a crash mid-write never corrupts the saved queue
                    var tmp = QueuePath + ".tmp";
                    var backup = QueuePath + ".bak";
                    File.WriteAllLines(tmp, snap.ToArray(), new UTF8Encoding(false));
                    // Keep a recoverable copy across replacement; deleting the old file first
                    // creates a crash window in which neither snapshot exists.
                    if (File.Exists(backup)) File.Delete(backup);
                    if (File.Exists(QueuePath)) File.Move(QueuePath, backup);
                    File.Move(tmp, QueuePath);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                _writtenVersion = version;
            }
            catch (Exception e) { Analytics.Warn("persist queue failed: " + e.Message); }
        }
    }

    static void RestoreQueue()
    {
        try
        {
            var backup = QueuePath + ".bak";
            var source = File.Exists(QueuePath) ? QueuePath : (File.Exists(backup) ? backup : null);
            if (source == null) return;
            var lines = File.ReadAllLines(source, Encoding.UTF8);
            var list = new List<string>(lines.Length);
            foreach (var l in lines)
                if (l.Length > 2 && l[0] == '{' && l[l.Length - 1] == '}') list.Add(l);
            Analytics.PrependRestored(list);
            // Leave the durable snapshot in place until a newer snapshot has been written.
            // If the app is killed before the first Update, the next launch can still restore it.
            Analytics.Log("restored " + list.Count + " queued events");
        }
        catch (Exception e) { Analytics.Warn("restore queue failed: " + e.Message); }
    }
}
}
}
