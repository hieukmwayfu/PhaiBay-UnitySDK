# PhaiBay Unity SDK

Analytics SDK cho Unity, API giống Firebase Analytics (`LogEvent` + params, user property), gửi về server [PhaiBay-Server](../PhaiBay-Server) tự host.

Repo này là **1 Unity project** (Unity 2022.3) để phát triển và test SDK:

| Thư mục | Nội dung |
|---|---|
| `Assets/PhaiBaySDK/` | SDK (UPM package `com.phaibay.analytics`) |
| `Assets/PhaiBayDemo/` | Scene + UI demo để test |

## Cài vào game

**Window › Package Manager › + › Add package from git URL…**, dán:

```
https://github.com/hieukmwayfu/PhaiBay-UnitySDK.git?path=/Assets/PhaiBaySDK
```

Hoặc thêm vào `Packages/manifest.json`:

```json
"com.phaibay.analytics": "https://github.com/hieukmwayfu/PhaiBay-UnitySDK.git?path=/Assets/PhaiBaySDK"
```

> SDK đang ở giai đoạn thử nghiệm (`0.x`), **chưa có tag phát hành** — URL trên luôn lấy bản mới nhất của nhánh `main`.

> Yêu cầu Unity 2020.3 trở lên.

## Khởi tạo

Cách 1 – bằng code (gọi sớm nhất có thể, ví dụ trong scene đầu tiên):

```csharp
PhaiBay.Initialize("com.company.game");                       // server mặc định
// PhaiBay.Initialize("com.company.game", "https://khac.example.com");  // server khác
```

Cách 2 – tự động (khuyên dùng): menu **PhaiBay › Settings** mở cửa sổ quản lý ScriptableObject `Assets/Resources/PhaiBaySettings.asset` (bấm **Tạo Settings** nếu chưa có). Chỉ cần điền **App Key** (Server URL để trống = server mặc định `PhaiBaySettings.DefaultServerUrl`), bấm **Kiểm tra kết nối & App Key** để thử. SDK tự khởi tạo trước khi load scene đầu tiên.

**App Key** là định danh app đặt lúc tạo app trong dashboard (Trang chủ › **+ Tạo app**), thường là package name / bundle id (vd `com.company.game`), hoặc để server tự sinh. Key **không đổi được** sau khi tạo; xem lại ở tab **Tích hợp SDK** của app.

Hỗ trợ **Enter Play Mode Options** với *Reload Domain* tắt: mọi static được reset ở đầu mỗi lần Play.

### Chế độ test (dry run)

Mặc định (`realDataOnlyOnAndroid = true`) **chỉ bản build Android ghi dữ liệu thật**. Trong Unity Editor và bản build Windows / Mac / Linux, SDK gửi event kèm header `X-PB-Dry-Run: 1`: server vẫn kiểm tra App Key, kết nối, rate limit và định dạng event, trả về số event hợp lệ nhưng **không ghi DB**, nên dashboard không bị lẫn data test.

- `PhaiBay.IsDryRun` cho biết đang ở chế độ test.
- `PhaiBay.LastUploadStatus` hiện kết quả lần gửi gần nhất, ví dụ `TEST OK 200: 12 events valid (dry run, not stored)`.
- Tắt `Real Data Only On Android` trong Settings nếu muốn mọi nền tảng đều ghi thật.

## Sử dụng

```csharp
// event không param
PhaiBay.LogEvent("tutorial_begin");

// 1 param
PhaiBay.LogEvent("level_start", "level", 5);

// nhiều param
PhaiBay.LogEvent("level_end",
    new PhaiBay.Parameter("level", 5),
    new PhaiBay.Parameter("success", true),
    new PhaiBay.Parameter("time_sec", 42.5));

// hoặc Dictionary
PhaiBay.LogEvent("purchase", new Dictionary<string, object> {
    { "item_id", "gem_pack" }, { "value", 0.99 }, { "currency", "USD" },
});

// user
PhaiBay.SetUserId("account_123");          // id tài khoản của bạn (tùy chọn)
PhaiBay.SetUserProperty("vip", "gold");    // gắn vào mọi event sau đó, null để xóa

// khác
PhaiBay.Flush();                            // gửi ngay
PhaiBay.SetAnalyticsCollectionEnabled(false);
PhaiBay.ResetAnalyticsData();
```

Hằng số tên event/param thường dùng: `PhaiBay.Events.LevelStart`, `PhaiBay.Params.Level`, …

### Quy tắc (giống Firebase)

- Tên event / param / user property: 1–40 ký tự, chữ + số + `_`, bắt đầu bằng chữ. Không dùng tiền tố `firebase_`, `google_`, `ga_`.
- Tối đa 25 param / event; string param ≤ 100 ký tự; user property ≤ 25 cái, value ≤ 36 ký tự.
- Giá trị param: `string`, `int/long`, `float/double`, `bool` (lưu thành 1/0).

### Event tự động (tắt mặc định)

SDK mặc định chỉ gửi các event bạn tự `LogEvent`. Bật **Auto Track Events** trong Settings nếu muốn thêm:

| Event | Khi nào |
|---|---|
| `first_open` | Lần đầu mở app |
| `session_start` | Mở app, hoặc quay lại sau > 30 phút ở background |

### Cơ chế gửi

- Event được xếp hàng trong RAM, gửi theo batch (gzip) mỗi 15s hoặc khi đủ 50 event.
- **Lỗi mạng / server lỗi (5xx, 429, 503)** → batch được giữ nguyên, retry với backoff 5s → 5 phút (theo `Retry-After` nếu server gửi). Batch gửi lại giữ **cùng `batch_id`** → server bỏ bản trùng, không đếm 2 lần.
- **Không có mạng** (`Application.internetReachability`) → không thử gửi, không tốn lượt retry; có mạng lại là gửi ngay.
- **Lưu tạm xuống máy**: hàng đợi được ghi vào `persistentDataPath/phaibay_queue.jsonl` mỗi 10s khi có thay đổi (ghi ở thread nền), ngay sau mỗi lần gửi lỗi, và khi app pause/quit. App crash / bị kill chỉ mất tối đa vài giây event; lần mở sau hàng đợi được khôi phục và gửi tiếp. File ghi kiểu tạm-rồi-đổi-tên nên không bao giờ bị hỏng giữa chừng.
- Tối đa `maxQueueSize` (mặc định 5000) event chờ trên máy; vượt thì bỏ event cũ nhất.
- Server trả 400/413 (dữ liệu sai / quá lớn) → bỏ batch đó, vì gửi lại cũng không được.
- `LogEvent` gọi được từ mọi thread.

## Test bằng project này

1. Chạy server (xem PhaiBay-Server), tạo app trong dashboard để lấy App Key.
2. Mở project bằng Unity 2022.3, mở `Assets/PhaiBayDemo/PhaiBayDemo.unity`, bấm Play.
3. Điền App Key (Server URL đã điền sẵn server mặc định) › **Initialize**, bấm các nút để bắn event.
4. Xem event realtime trong dashboard tab **DebugView**.

## Phát hành phiên bản

Khi SDK chạy ổn định: tăng `version` trong `Assets/PhaiBaySDK/package.json`, commit, rồi `git tag vX.Y.Z && git push --tags`.
