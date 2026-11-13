using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PhaiBayEditor
{
    /// <summary>Finds / creates the PhaiBaySettings asset used for auto-initialization.</summary>
    internal static class SettingsLocator
    {
        public const string DefaultPath = "Assets/Resources/" + PhaiBaySettings.ResourceName + ".asset";

        /// <summary>The asset the runtime will load: Resources/PhaiBaySettings. Falls back to any PhaiBaySettings asset.</summary>
        public static PhaiBaySettings Find()
        {
            PhaiBaySettings any = null;
            foreach (var guid in AssetDatabase.FindAssets("t:PhaiBaySettings"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<PhaiBaySettings>(path);
                if (s == null) continue;
                if (IsLoadable(path)) return s;
                if (any == null) any = s;
            }
            return any;
        }

        public static bool IsLoadable(string path) =>
            path.Replace('\\', '/').Contains("/Resources/") && Path.GetFileNameWithoutExtension(path) == PhaiBaySettings.ResourceName;

        public static PhaiBaySettings Create()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath));
            var s = ScriptableObject.CreateInstance<PhaiBaySettings>();
            AssetDatabase.CreateAsset(s, DefaultPath);
            AssetDatabase.SaveAssets();
            return s;
        }
    }

    [CustomEditor(typeof(PhaiBaySettings))]
    public class PhaiBaySettingsEditor : Editor
    {
        static readonly Regex AppKeyRe = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{2,99}$");

        UnityWebRequest _test;
        string _testResult;
        MessageType _testType;

        public override void OnInspectorGUI()
        {
            var s = (PhaiBaySettings)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("Kết nối", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("appKey"), new GUIContent("App Key"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("serverUrl"), new GUIContent("Server URL"));

            var key = (s.appKey ?? "").Trim();
            var url = (s.serverUrl ?? "").Trim();
            if (key.Length == 0)
                EditorGUILayout.HelpBox("Nhập App Key của app (Dashboard › chọn app › Tích hợp SDK).", MessageType.Warning);
            else if (!AppKeyRe.IsMatch(key))
                EditorGUILayout.HelpBox("App Key không hợp lệ: 3–100 ký tự, chữ/số/'.'/'_'/'-' (vd: com.company.game).", MessageType.Error);
            if (url.Length == 0)
                EditorGUILayout.HelpBox("Nhập Server URL (vd: https://analytics.your-domain.com).", MessageType.Warning);
            else if (!url.StartsWith("https://") && !url.StartsWith("http://"))
                EditorGUILayout.HelpBox("Server URL phải bắt đầu bằng https:// hoặc http://", MessageType.Error);
            else if (url.StartsWith("http://") && !url.Contains("localhost") && !url.Contains("127.0.0.1"))
                EditorGUILayout.HelpBox("HTTP thường bị chặn trên Android/iOS. Nên dùng HTTPS (hoặc bật 'Allow downloads over HTTP' trong Player Settings).", MessageType.Info);

            using (new EditorGUI.DisabledScope(_test != null || key.Length == 0 || url.Length == 0))
            {
                if (GUILayout.Button(_test != null ? "Đang kiểm tra…" : "Kiểm tra kết nối & App Key"))
                    StartTest(url, key);
            }
            if (!string.IsNullOrEmpty(_testResult)) EditorGUILayout.HelpBox(_testResult, _testType);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hành vi", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoInitialize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoTrackEvents"), new GUIContent("Auto Track Events"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sendInEditor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("debugLog"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gửi dữ liệu", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flushIntervalSeconds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flushAtCount"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxBatchSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxQueueSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sessionTimeoutSeconds"));

            serializedObject.ApplyModifiedProperties();

            var path = AssetDatabase.GetAssetPath(s);
            if (!string.IsNullOrEmpty(path) && !SettingsLocator.IsLoadable(path))
                EditorGUILayout.HelpBox($"SDK chỉ tự khởi tạo với file Resources/{PhaiBaySettings.ResourceName}.asset. File này đang ở: {path}", MessageType.Warning);
        }

        void StartTest(string url, string key)
        {
            // An empty batch is accepted by the server only when the app key exists.
            var body = Encoding.UTF8.GetBytes("{\"ctx\":{\"user_id\":\"editor-test\"},\"events\":[]}");
            _test = new UnityWebRequest(url.TrimEnd('/') + "/v1/events", UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            _test.SetRequestHeader("Content-Type", "application/json");
            _test.SetRequestHeader("X-App-Key", key);
            _test.SendWebRequest();
            _testResult = null;
            EditorApplication.update += PollTest;
        }

        void PollTest()
        {
            if (_test == null) { EditorApplication.update -= PollTest; return; }
            if (!_test.isDone) return;
            EditorApplication.update -= PollTest;
            var code = _test.responseCode;
            if (code == 200) { _testResult = "OK — server nhận app key này."; _testType = MessageType.Info; }
            else if (code == 401) { _testResult = "Server không biết App Key này. Kiểm tra lại key trong dashboard."; _testType = MessageType.Error; }
            else if (code == 0) { _testResult = "Không kết nối được server: " + _test.error; _testType = MessageType.Error; }
            else { _testResult = $"Lỗi {code}: {_test.downloadHandler.text}"; _testType = MessageType.Warning; }
            _test.Dispose();
            _test = null;
            Repaint();
        }

        void OnDisable()
        {
            EditorApplication.update -= PollTest;
            _test?.Dispose();
            _test = null;
        }
    }

    /// <summary>Menu PhaiBay › Settings: one place to edit the SDK settings asset.</summary>
    public class PhaiBaySettingsWindow : EditorWindow
    {
        PhaiBaySettings _settings;
        Editor _editor;
        Vector2 _scroll;

        [MenuItem("PhaiBay/Settings", priority = 0)]
        public static void Open()
        {
            var w = GetWindow<PhaiBaySettingsWindow>("PhaiBay");
            w.minSize = new Vector2(360, 320);
            w.Show();
        }

        [MenuItem("PhaiBay/Select Settings Asset", priority = 1)]
        static void Select()
        {
            var s = SettingsLocator.Find() ?? SettingsLocator.Create();
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
        }

        void OnEnable() => Refresh();
        void OnFocus() => Refresh();

        void Refresh()
        {
            var s = SettingsLocator.Find();
            if (s == _settings && (_editor != null || s == null)) return;
            _settings = s;
            if (_editor != null) DestroyImmediate(_editor);
            _editor = s != null ? Editor.CreateEditor(s) : null;
        }

        void OnDisable()
        {
            if (_editor != null) DestroyImmediate(_editor);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("PhaiBay Analytics", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("SDK " + PhaiBay.SdkVersion, EditorStyles.miniLabel);
            EditorGUILayout.Space();

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("Chưa có file settings. Tạo tại " + SettingsLocator.DefaultPath + " để SDK tự khởi tạo khi chạy game.", MessageType.Info);
                if (GUILayout.Button("Tạo Settings", GUILayout.Height(28))) { SettingsLocator.Create(); Refresh(); }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(_settings, typeof(PhaiBaySettings), false);
                if (GUILayout.Button("Chọn", GUILayout.Width(60))) { Selection.activeObject = _settings; EditorGUIUtility.PingObject(_settings); }
            }
            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _editor?.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }
    }
}
