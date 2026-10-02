# TWSE Market Pulse

台股即時行情 Windows 桌面小工具，可查看加權指數，也能加入個股與 ETF 自選清單，並以小型走勢圖掌握當日行情。

**English:** A Windows desktop widget for following Taiwan stocks and ETFs, with a customizable watchlist and compact intraday charts.

## 下載與安裝

1. 前往 [Releases 最新版本](https://github.com/hs9021401/TWSE-Market-Pulse/releases/latest)。
2. 下載 `TwseWidget-Setup-0.1.1002.2026.exe` 並執行安裝。
3. 從桌面捷徑或開始功能表開啟 **TWSE Market Pulse**。

本程式需要 **.NET 8 Desktop Runtime**。若開啟時 Windows 提示缺少執行環境，請先安裝 [.NET 8 Desktop Runtime for Windows](https://dotnet.microsoft.com/download/dotnet/8.0)，再重新開啟小工具。

## 使用方式

- **查看行情：** 開啟後會顯示大盤指數及當日走勢；盤中約每 20 秒更新一次。
- **切換版面：** 按視窗上的 `▦`，或在視窗空白處按右鍵，即可選擇完整、Mini 或 Ticker 版面。
- **新增自選：** 切換到完整版面，輸入股票／ETF 名稱或代號，從搜尋結果選取後按 `+` 加入。
- **切換自選：** 使用代號下拉選單，或按左右箭頭循環瀏覽。
- **檢視走勢：** 將滑鼠移到圖表上，可查看對應時間與價格。
- **手動更新：** 按 `↻` 重新整理行情；收盤後也可用它重新載入當日走勢。
- **置頂或移動：** 按 `PIN` 切換視窗置頂；拖曳小工具的空白處即可移動。
- **收至系統匣：** 按 `×` 隱藏小工具。雙擊系統匣圖示可再次顯示，右鍵圖示可開啟其他操作。

版面、視窗位置、置頂狀態和自選清單會保存在這台電腦，重新開啟後仍會保留。解除安裝不會刪除自選清單。

## 版面尺寸

| 版面 | 大小 | 適合情境 |
| --- | ---: | --- |
| 完整 | 360 × 240 | 搜尋與管理自選、查看完整走勢 |
| Mini | 260 × 160 | 保留主要報價與當日走勢 |
| Ticker | 280 × 88 | 輕量顯示報價與迷你走勢 |

視窗記住上次使用的版面與位置。若螢幕配置改變，小工具會移回可見範圍。

## 行情資訊

行情主要來自公開市場資訊。實際更新情況會受交易時段、成交狀況與資料來源連線影響；本工具僅供行情查看，不構成投資建議。

---

## English

### Download and install

1. Visit [the latest release](https://github.com/hs9021401/TWSE-Market-Pulse/releases/latest).
2. Download and run `TwseWidget-Setup-0.1.1002.2026.exe`.
3. Open **TWSE Market Pulse** from the desktop shortcut or Start menu.

The app requires the **.NET 8 Desktop Runtime**. If Windows reports that the runtime is missing, install the [.NET 8 Desktop Runtime for Windows](https://dotnet.microsoft.com/download/dotnet/8.0), then reopen the widget.

### How to use

- **View quotes:** The widget shows the Taiwan market index and today's chart. Quotes refresh about every 20 seconds during market hours.
- **Change layout:** Click `▦` or right-click an empty area of the widget to choose Full, Mini, or Ticker.
- **Add a stock or ETF:** Switch to Full, type a name or symbol, select a search result, then click `+`.
- **Browse your watchlist:** Choose a symbol from the drop-down or use the left and right arrows.
- **Inspect the chart:** Hover over the chart to see the time and price.
- **Refresh:** Click `↻` to refresh the quote. After the market closes, it also reloads the day's chart.
- **Pin or move:** Click `PIN` to toggle always-on-top. Drag an empty area to move the widget.
- **Use the system tray:** Click `×` to hide the widget. Double-click its tray icon to show it again; right-click for more actions.

Your layout, window position, pin setting, and watchlist are saved on this computer and restored when you reopen the app. Uninstalling the app does not delete your watchlist.
