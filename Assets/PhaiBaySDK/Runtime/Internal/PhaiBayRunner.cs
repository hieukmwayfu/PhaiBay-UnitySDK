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
/// </summary>
[AddComponentMenu("")]
internal sealed class PhaiBayRunner : MonoBehaviour
{
    static PhaiBayRunner _instance;
    static volatile bool _flushRequested;

    const float MinBackoff = 5f, MaxBackoff = 300f;
    const string QueueFile = "phaibay_queue.jsonl";

    float _sinceFlush;
    float _nextAllowedSend;
    float _backoff = MinBackoff;
    bool _sending;
    List<string> _pendingBatch; // failed batch kept with the same batch_id so the server can dedupe the retry
    string _pendingBatchId;

    DateTime _backgroundAt = DateTime.MinValue;

    static string QueuePath => Path.Combine(Application.persistentDataPath, QueueFile);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() // supports "Enter Play Mode" without domain reload
    {
        _instance = null;
        _flushRequested = false;
        PhaiBay.ResetForPlayMode();
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
    internal static void NotifyEnqueued() { }

    internal static void DeletePersistedQueue()
    {
        try { if (File.Exists(QueuePath)) File.Delete(QueuePath); } catch (Exception) { }
    }

    void Awake()
    {
        PhaiBay.Device.Init();
        RestoreQueue();
    }

    void Update()
    {
        _sinceFlush += Time.unscaledDeltaTime;
        if (_sending || Time.realtimeSinceStartup < _nextAllowedSend) return;
        var s = PhaiBay.Settings;
        int count = PhaiBay.QueueCount;
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
        if (!PhaiBay.Initialized) return;
        if (paused)
        {
            _backgroundAt = DateTime.UtcNow;
            PersistQueue();
            if (!_sending) StartCoroutine(Send()); // best effort before the OS suspends us
        }
        else
        {
            if (_backgroundAt != DateTime.MinValue &&
                (DateTime.UtcNow - _backgroundAt).TotalSeconds >= PhaiBay.Settings.sessionTimeoutSeconds)
                PhaiBay.StartSession();
            _backgroundAt = DateTime.MinValue;
            _nextAllowedSend = 0; // network may be back
        }
    }

    void OnApplicationQuit()
    {
        if (!PhaiBay.Initialized) return;
        PersistQueue();
    }

    IEnumerator Send()
    {
        if (_sending) yield break;
#if UNITY_EDITOR
        if (!PhaiBay.Settings.sendInEditor) yield break;
#endif
        _sending = true;
        try
        {
            List<string> batch;
            string batchId;
            var head = PhaiBay.PeekBatch(1);
            if (_pendingBatch != null && head.Count > 0 && ReferenceEquals(head[0], _pendingBatch[0]))
            {
                batch = _pendingBatch;
                batchId = _pendingBatchId;
            }
            else
            {
                batch = PhaiBay.PeekBatch(PhaiBay.Settings.maxBatchSize);
                batchId = Guid.NewGuid().ToString("N");
            }
            if (batch.Count == 0) yield break;

            var sb = new StringBuilder(batch.Count * 160 + 512);
            sb.Append("{\"batch_id\":\"").Append(batchId).Append("\",\"sent_at\":")
              .Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
              .Append(",\"ctx\":").Append(PhaiBay.BuildContextJson())
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
            using (var req = new UnityWebRequest(PhaiBay.Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" };
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-App-Key", PhaiBay.AppKey);
                if (gz) req.SetRequestHeader("Content-Encoding", "gzip");
                req.timeout = 20;
                yield return req.SendWebRequest();

                long code = req.responseCode;
                bool netError = req.result == UnityWebRequest.Result.ConnectionError || code == 0;

                if (!netError && code >= 200 && code < 300)
                {
                    PhaiBay.RemoveSent(batch);
                    _pendingBatch = null;
                    _backoff = MinBackoff;
                    PhaiBay.SetUploadStatus("OK " + code + ": sent " + batch.Count + " events");
                    PhaiBay.Log("sent " + batch.Count + " events (" + body.Length + " bytes): " + req.downloadHandler.text);
                    if (PhaiBay.QueueCount >= PhaiBay.Settings.flushAtCount) _flushRequested = true;
                }
                else if (!netError && (code == 400 || code == 413))
                {
                    // malformed / too large: retrying won't help, drop it so the queue keeps moving
                    PhaiBay.SetUploadStatus("Rejected " + code + ": " + req.downloadHandler.text);
                    PhaiBay.Warn("server rejected batch (" + code + "): " + req.downloadHandler.text + " - dropped " + batch.Count + " events");
                    PhaiBay.RemoveSent(batch);
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
                    PhaiBay.SetUploadStatus((netError ? "Network error: " + req.error : "HTTP " + code + ": " + req.downloadHandler.text)
                        + " (retry in " + wait.ToString("0") + "s)");
                    _nextAllowedSend = Time.realtimeSinceStartup + wait;
                    _backoff = Mathf.Min(_backoff * 2f, MaxBackoff);
                    PhaiBay.Log("upload failed (" + code + " " + req.error + "), retry in " + wait.ToString("0") + "s");
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

    static void PersistQueue()
    {
        try
        {
            var snap = PhaiBay.Snapshot();
            if (snap.Count == 0) { DeletePersistedQueue(); return; }
            File.WriteAllLines(QueuePath, snap.ToArray(), new UTF8Encoding(false));
        }
        catch (Exception e) { PhaiBay.Warn("persist queue failed: " + e.Message); }
    }

    static void RestoreQueue()
    {
        try
        {
            if (!File.Exists(QueuePath)) return;
            var lines = File.ReadAllLines(QueuePath, Encoding.UTF8);
            var list = new List<string>(lines.Length);
            foreach (var l in lines)
                if (l.Length > 2 && l[0] == '{' && l[l.Length - 1] == '}') list.Add(l);
            PhaiBay.PrependRestored(list);
            File.Delete(QueuePath); // re-written on next pause/quit with whatever is still unsent
            PhaiBay.Log("restored " + list.Count + " queued events");
        }
        catch (Exception e) { PhaiBay.Warn("restore queue failed: " + e.Message); }
    }
}
