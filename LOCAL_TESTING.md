# Local Testing

## 1. 啟動網站

```powershell
cd "C:\Users\User\Documents\New project\TradingViewWebhookDashboard"
dotnet run --launch-profile http
```

啟動後打開：

`http://localhost:5241`

## 2. 用 DU 範例打 webhook

另開一個 PowerShell：

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:5241/api/webhooks/tradingview" `
  -Method Post `
  -ContentType "application/json" `
  -InFile "C:\Users\User\Documents\New project\TradingViewWebhookDashboard\sample-webhook.json"
```

成功後首頁會看到：

- `WIFUSDT.P` 被建立出來
- `SDU 卡夾` 亮起 `12H`
- 該卡夾下面會出現一張最近通知卡片

## 3. 用 DD 範例打 webhook

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:5241/api/webhooks/tradingview" `
  -Method Post `
  -ContentType "application/json" `
  -InFile "C:\Users\User\Documents\New project\TradingViewWebhookDashboard\sample-webhook-dd.json"
```

成功後首頁會看到：

- `WIFUSDT.P` 的 `SDD 卡夾` 亮起對應時間軸

## 4. 檢查 JSON API

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:5241/api/dashboard" `
  -UseBasicParsing
```

這個 API 會回傳目前所有幣種的卡夾狀態。

## 5. 測 Discord 轉發

編輯 [appsettings.json](C:\Users\User\Documents\New project\TradingViewWebhookDashboard\appsettings.json)：

```json
"Dashboard": {
  "ForwardDiscordDuWebhookUrl": "你的 DU Discord webhook URL",
  "ForwardDiscordDdWebhookUrl": "你的 DD Discord webhook URL"
}
```

重新啟動網站後，再打一次範例 webhook，就會依方向同步轉發到 Discord：

- DU -> DU webhook
- DD -> DD webhook

## 6. 測密碼保護

如果你設定：

```json
"Dashboard": {
  "WebhookSecret": "abc123"
}
```

呼叫時要加上：

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:5241/api/webhooks/tradingview?key=abc123" `
  -Method Post `
  -ContentType "application/json" `
  -InFile "C:\Users\User\Documents\New project\TradingViewWebhookDashboard\sample-webhook.json"
```

也可以改用 header：

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:5241/api/webhooks/tradingview" `
  -Method Post `
  -Headers @{ "X-Webhook-Secret" = "abc123" } `
  -ContentType "application/json" `
  -InFile "C:\Users\User\Documents\New project\TradingViewWebhookDashboard\sample-webhook.json"
```

## 7. 清空目前卡夾資料

刪掉這個檔案後重新啟動：

`C:\Users\User\Documents\New project\TradingViewWebhookDashboard\App_Data\dashboard-state.json`

注意：

資料現在會保存在專案根目錄的 `App_Data`，所以一般重新整理頁面或重新啟動網站，不應該再把資料清空。

## 8. 真實 TradingView 測試

TradingView 不能直接打你本機的 `localhost`。

如果你要用真實警報測：

1. 先把網站在本機跑起來
2. 用 ngrok 或 Cloudflare Tunnel 暴露一個公開網址
3. 把 TradingView webhook URL 指到該公開網址
4. 例如：

```text
https://你的-tunnel-網址/api/webhooks/tradingview
```
