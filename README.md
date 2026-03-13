# BTCUSDT.P 相關係數網站

這個 ASP.NET Core Razor Pages 專案會把原本的 `find_correlated_coins.py` 邏輯做成網頁版首頁，定時抓取 Bitget USDT 永續合約資料，顯示：

- 基準：`BTCUSDT.P`
- K 線週期：`15m`
- 視窗長度：`20` 根收盤價
- 條件：相關係數 `0.70 ~ 1.00`
- 額外篩選：只保留和 BTC 最新 15 分鐘同方向的標的
- 自動更新：每 `15` 分鐘

## 專案位置

`C:\Users\User\Documents\New project\TradingViewWebhookDashboard`

## 本地執行

```powershell
cd "C:\Users\User\Documents\New project\TradingViewWebhookDashboard"
dotnet restore
dotnet run --launch-profile http
```

預設本地網址：

`http://localhost:5241`

## 主要設定

設定檔在 [appsettings.json](C:\Users\User\Documents\New project\TradingViewWebhookDashboard\appsettings.json)。

`CorrelationDashboard` 區段目前預設如下：

- `PageTitle`: 首頁標題
- `BaseSymbol`: 基準合約，預設 `BTCUSDT`
- `BaseTradingViewSymbol`: 顯示用名稱，預設 `BTCUSDT.P`
- `Granularity`: K 線週期，預設 `15m`
- `CandleLimit`: 相關係數視窗長度，預設 `20`
- `MinCorrelation` / `MaxCorrelation`: 篩選區間
- `RefreshIntervalMinutes`: 排程更新分鐘數，預設 `15`
- `MaxParallelRequests`: 同時抓取 Bitget K 線的最大並行數
- `SnapshotPath`: 快照 JSON 儲存位置

## 測試

```powershell
cd "C:\Users\User\Documents\New project"
dotnet test "TradingViewWebhookDashboard.Tests\TradingViewWebhookDashboard.Tests.csproj"
```

## 上傳到 GitHub

如果你要自己建立 GitHub repo，這組指令可以直接用：

```powershell
cd "C:\Users\User\Documents\New project"
git init
git add .
git commit -m "Add BTC correlation dashboard"
git branch -M main
git remote add origin https://github.com/<你的帳號>/<你的repo>.git
git push -u origin main
```

## 自動建置

GitHub Actions 工作流程已放在 [dotnet-correlation-dashboard.yml](C:\Users\User\Documents\New project\.github\workflows\dotnet-correlation-dashboard.yml)，推到 GitHub 後會自動跑 restore、build、test。
