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

Khóa phiên bản theo tag: thêm `#v1.0.0` vào cuối URL.

> Yêu cầu Unity 2020.3 trở lên.

## Khởi tạo

Cách 1 – bằng code (gọi sớm nhất có thể, ví dụ trong scene đầu tiên):

```csharp
PhaiBay.Initialize("pb_xxxxxxxx", "https://analytics.your-domain.com");
```

Cách 2 – tự động: **Assets › Create › PhaiBay › Settings**, đặt tên `PhaiBaySettings`, để trong thư mục `Resources`, điền `apiKey` + `serverUrl`. SDK tự khởi tạo trước khi load scene đầu tiên.

API key lấy trong dashboard (tab **Apps**) hoặc gọi `POST /api/apps`.

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

### Event tự động

| Event | Khi nào |
|---|---|
| `first_open` | Lần đầu mở app |
| `session_start` | Mở app, hoặc quay lại sau > 30 phút ở background |
| `user_engagement` | Khi app vào background / thoát, param `engagement_time_msec` |

### Cơ chế gửi

- Event được xếp hàng trong RAM, gửi theo batch (gzip) mỗi 15s hoặc khi đủ 50 event.
- Mất mạng → giữ lại, retry với backoff (5s → 5 phút). Hàng đợi được ghi xuống `persistentDataPath` khi app pause/quit và gửi lại lần mở sau.
- Mỗi batch có `batch_id` → server bỏ qua bản gửi trùng khi retry.
- `LogEvent` gọi được từ mọi thread.

## Test bằng project này

1. Chạy server (xem PhaiBay-Server), tạo app trong dashboard để lấy API key.
2. Mở project bằng Unity 2022.3, mở `Assets/PhaiBayDemo/PhaiBayDemo.unity`, bấm Play.
3. Điền Server URL + API key › **Initialize**, bấm các nút để bắn event.
4. Xem event realtime trong dashboard tab **DebugView**.

## Phát hành phiên bản

Tăng `version` trong `Assets/PhaiBaySDK/package.json`, commit, rồi `git tag v1.0.1 && git push --tags`.
