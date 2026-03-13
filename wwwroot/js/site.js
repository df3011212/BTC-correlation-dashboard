(function () {
  const initialDataNode = document.getElementById("initial-dashboard-data");
  const summaryRoot = document.getElementById("summary-root");
  const detailRoot = document.getElementById("detail-root");
  const logRoot = document.getElementById("log-root");
  const symbolSearch = document.getElementById("symbol-search");
  const symbolFilterBar = document.getElementById("symbol-filter-bar");
  const logFilterBar = document.getElementById("log-filter-bar");
  const detailCaption = document.getElementById("detail-caption");
  const discordStatus = document.getElementById("discord-forward-status");
  const dailyResetStatus = document.getElementById("daily-reset-status");
  const discordSettingsForm = document.getElementById("discord-settings-form");
  const duWebhookInput = document.getElementById("du-webhook-url");
  const ddWebhookInput = document.getElementById("dd-webhook-url");
  const dailyResetEnabledInput = document.getElementById("daily-reset-enabled");
  const clearDiscordSettingsButton = document.getElementById("clear-discord-settings");
  const settingsSaveStatus = document.getElementById("settings-save-status");
  const config = window.dashboardConfig || {};

  if (!initialDataNode || !summaryRoot || !detailRoot || !logRoot || !symbolSearch || !symbolFilterBar || !logFilterBar || !detailCaption || !discordStatus || !dailyResetStatus || !discordSettingsForm || !duWebhookInput || !ddWebhookInput || !dailyResetEnabledInput || !clearDiscordSettingsButton || !settingsSaveStatus) {
    return;
  }

  const state = {
    snapshot: { symbols: [], totalAlerts: 0, updatedAtLocalText: "尚未收到" },
    search: "",
    symbolFilter: "all",
    logFilter: "all",
    selectedSymbol: null,
    selectedLane: "DU",
    eventSource: null,
    settings: {
      duWebhookUrl: "",
      ddWebhookUrl: "",
      dailyResetEnabled: true,
      lastAutoResetLocalDate: null
    }
  };

  const symbolFilters = [
    { id: "all", label: "全部" },
    { id: "active", label: "有亮燈" },
    { id: "du", label: "只看 DU" },
    { id: "dd", label: "只看 DD" }
  ];

  const logFilters = [
    { id: "all", label: "全部日誌" },
    { id: "du", label: "DU 日誌" },
    { id: "dd", label: "DD 日誌" }
  ];

  try {
    state.snapshot = JSON.parse(initialDataNode.textContent || "{}");
  } catch (error) {
    console.error("Unable to parse initial dashboard snapshot.", error);
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll("\"", "&quot;")
      .replaceAll("'", "&#39;");
  }

  function normalizeAlertText(value) {
    return String(value ?? "")
      .replaceAll(":green_circle:", "🟢")
      .replaceAll(":red_circle:", "🔴");
  }

  function countTriggered(folder) {
    return (folder?.slots || []).filter((slot) => slot.isTriggered).length;
  }

  function getAllLogs(model) {
    return (model.symbols || [])
      .flatMap((symbol) => {
        const duLogs = (symbol.sduFolder?.recentAlerts || []).map((alert) => ({
          ...alert,
          symbol: symbol.symbol,
          lane: "DU",
          laneLabel: "SDU",
          sortKey: alert.receivedAtUtc || ""
        }));

        const ddLogs = (symbol.sddFolder?.recentAlerts || []).map((alert) => ({
          ...alert,
          symbol: symbol.symbol,
          lane: "DD",
          laneLabel: "SDD",
          sortKey: alert.receivedAtUtc || ""
        }));

        return duLogs.concat(ddLogs);
      })
      .sort((left, right) => String(right.sortKey).localeCompare(String(left.sortKey)));
  }

  function getLatestAlert(folder) {
    return (folder?.recentAlerts || [])[0] || null;
  }

  function getPreferredLane(symbol) {
    const duLatest = getLatestAlert(symbol.sduFolder);
    const ddLatest = getLatestAlert(symbol.sddFolder);

    if (duLatest && !ddLatest) {
      return "DU";
    }

    if (ddLatest && !duLatest) {
      return "DD";
    }

    if (duLatest && ddLatest) {
      return String(duLatest.receivedAtUtc).localeCompare(String(ddLatest.receivedAtUtc)) >= 0 ? "DU" : "DD";
    }

    return countTriggered(symbol.sduFolder) >= countTriggered(symbol.sddFolder) ? "DU" : "DD";
  }

  function getFolderByLane(symbol, lane) {
    return lane === "DD" ? symbol.sddFolder : symbol.sduFolder;
  }

  function getFilteredSymbols(model) {
    const keyword = state.search.trim().toLowerCase();

    return (model.symbols || [])
      .filter((symbol) => {
        if (keyword && !String(symbol.symbol || "").toLowerCase().includes(keyword)) {
          return false;
        }

        const duCount = countTriggered(symbol.sduFolder);
        const ddCount = countTriggered(symbol.sddFolder);
        const totalTriggered = duCount + ddCount;

        if (state.symbolFilter === "active") {
          return totalTriggered > 0;
        }

        if (state.symbolFilter === "du") {
          return duCount > 0;
        }

        if (state.symbolFilter === "dd") {
          return ddCount > 0;
        }

        return true;
      })
      .sort((left, right) => String(right.lastAlertAtUtc || "").localeCompare(String(left.lastAlertAtUtc || "")));
  }

  function ensureSelection(filteredSymbols) {
    const selectedStillExists = filteredSymbols.some((symbol) => symbol.symbol === state.selectedSymbol);
    if (selectedStillExists) {
      return;
    }

    const firstSymbol = filteredSymbols[0];
    state.selectedSymbol = firstSymbol ? firstSymbol.symbol : null;
    state.selectedLane = firstSymbol ? getPreferredLane(firstSymbol) : "DU";
  }

  function renderFilterChips(root, items, currentValue, group) {
    root.innerHTML = items.map((item) => `
      <button class="chip-button ${item.id === currentValue ? "is-active" : ""}" type="button" data-group="${group}" data-value="${escapeHtml(item.id)}">
        ${escapeHtml(item.label)}
      </button>
    `).join("");
  }

  function renderSummary(filteredSymbols) {
    if (filteredSymbols.length === 0) {
      summaryRoot.innerHTML = `
        <section class="empty-state compact">
          <strong>找不到符合條件的幣種。</strong>
          <p class="mb-0">調整搜尋或篩選條件後，這裡會顯示可選擇的幣種摘要卡。</p>
        </section>
      `;
      return;
    }

    summaryRoot.innerHTML = filteredSymbols.map((symbol) => {
      const duCount = countTriggered(symbol.sduFolder);
      const ddCount = countTriggered(symbol.sddFolder);
      const totalCount = duCount + ddCount;
      const isSelected = symbol.symbol === state.selectedSymbol;

      return `
        <button type="button" class="summary-card ${isSelected ? "is-selected" : ""}" data-symbol="${escapeHtml(symbol.symbol)}">
          <div class="summary-card-top">
            <div>
              <h3>${escapeHtml(symbol.symbol)}</h3>
              <div class="summary-meta">最後通知 ${escapeHtml(symbol.lastAlertAtLocalText || "尚未收到")}</div>
            </div>
            <span class="summary-total">${totalCount} 燈號</span>
          </div>
          <div class="summary-badges">
            <span class="summary-badge summary-badge--du">DU ${duCount}</span>
            <span class="summary-badge summary-badge--dd">DD ${ddCount}</span>
          </div>
        </button>
      `;
    }).join("");
  }

  function renderLaneTabs(symbol) {
    const duCount = countTriggered(symbol.sduFolder);
    const ddCount = countTriggered(symbol.sddFolder);

    return `
      <div class="lane-tabs">
        <button type="button" class="lane-tab ${state.selectedLane === "DU" ? "is-active" : ""}" data-lane="DU">
          SDU 卡夾 <span>${duCount}</span>
        </button>
        <button type="button" class="lane-tab ${state.selectedLane === "DD" ? "is-active" : ""}" data-lane="DD">
          SDD 卡夾 <span>${ddCount}</span>
        </button>
      </div>
    `;
  }

  function renderSlot(slot, lane) {
    const activeClass = slot.isTriggered ? `active ${lane.toLowerCase()}` : "";

    return `
      <article class="slot slot--compact ${activeClass}">
        <div class="slot-timeframe">${escapeHtml(slot.timeframe)}</div>
        <div class="slot-status">${slot.isTriggered ? "已響鈴" : "等待中"}</div>
        <div class="slot-meta">${slot.isTriggered ? escapeHtml(slot.triggeredAtText || slot.lastTriggeredAtLocalText || "剛剛") : "灰白待命"}</div>
        ${slot.isTriggered && slot.price ? `<div class="slot-price">${escapeHtml(slot.price)}</div>` : ""}
      </article>
    `;
  }

  function renderAlertCard(alert, lane) {
    return `
      <article class="alert-item alert-item--compact" data-lane="${escapeHtml(lane)}">
        <div class="alert-item-header">
          <h4 class="alert-title">${escapeHtml(normalizeAlertText(alert.title))}</h4>
          <div class="alert-actions">
            <span class="alert-time">${escapeHtml(alert.receivedAtLocalText || alert.triggeredAtText)}</span>
            <button type="button" class="icon-button icon-button--danger" data-delete-alert="${escapeHtml(alert.id)}" title="刪除這筆訊號">刪除</button>
          </div>
        </div>
        <p class="alert-description">${escapeHtml(alert.description)}</p>
        <div class="alert-details">
          <span class="detail-chip">${escapeHtml(alert.timeframe)}</span>
          ${alert.price ? `<span class="detail-chip">收盤價 ${escapeHtml(alert.price)}</span>` : ""}
          ${alert.footerText ? `<span class="detail-chip">${escapeHtml(alert.footerText)}</span>` : ""}
        </div>
      </article>
    `;
  }

  function renderDetail(filteredSymbols) {
    const symbol = filteredSymbols.find((item) => item.symbol === state.selectedSymbol);
    if (!symbol) {
      detailCaption.textContent = "尚未選取幣種";
      detailRoot.innerHTML = `
        <section class="empty-state">
          <strong>請先從上方摘要卡選擇一個幣種。</strong>
          <p class="mb-0">右側日誌也可以直接點擊切換幣種。</p>
        </section>
      `;
      return;
    }

    const folder = getFolderByLane(symbol, state.selectedLane);
    const triggeredCount = countTriggered(folder);
    detailCaption.textContent = `${symbol.symbol} / ${state.selectedLane === "DU" ? "SDU 卡夾" : "SDD 卡夾"}`;

    detailRoot.innerHTML = `
      <section class="detail-symbol-card">
        <header class="detail-symbol-header">
          <div>
            <h3>${escapeHtml(symbol.symbol)}</h3>
            <div class="symbol-subtitle">最後通知 ${escapeHtml(symbol.lastAlertAtLocalText || "尚未收到")}</div>
          </div>
          <span class="detail-highlight">${triggeredCount} 個時間軸已亮燈</span>
        </header>
        ${renderLaneTabs(symbol)}
        <section class="folder-card folder-card--detail" data-lane="${escapeHtml(folder.lane)}">
          <div class="folder-heading">
            <div>
              <h3 class="folder-title">${escapeHtml(folder.displayName)}</h3>
              <div class="symbol-subtitle">${escapeHtml(folder.description)}</div>
            </div>
            <span class="folder-badge">${escapeHtml(folder.triggeredCountLabel)}</span>
          </div>
          <div class="slot-grid slot-grid--detail">
            ${(folder.slots || []).map((slot) => renderSlot(slot, folder.lane)).join("")}
          </div>
          <div class="detail-section-title">最近通知卡片</div>
          <div class="alert-list">
            ${(folder.recentAlerts || []).length > 0
              ? folder.recentAlerts.map((alert) => renderAlertCard(alert, folder.lane)).join("")
              : `<div class="empty-state compact">這個卡夾目前還沒有通知卡片。</div>`}
          </div>
        </section>
      </section>
    `;
  }

  function getFilteredLogs(model) {
    return getAllLogs(model)
      .filter((log) => state.logFilter === "all" || log.lane.toLowerCase() === state.logFilter)
      .slice(0, 60);
  }

  function renderLogs(model) {
    const logs = getFilteredLogs(model);
    const logCountLabel = document.getElementById("log-count-label");
    if (logCountLabel) {
      logCountLabel.textContent = `${logs.length} 筆`;
    }

    if (logs.length === 0) {
      logRoot.innerHTML = `
        <section class="empty-state compact">
          <strong>目前還沒有符合條件的日誌。</strong>
          <p class="mb-0">收到新的 webhook 後，這裡會依時間排序顯示最新紀錄。</p>
        </section>
      `;
      return;
    }

    logRoot.innerHTML = logs.map((log) => `
      <article class="log-item">
        <div class="log-item-top">
          <span class="log-pill ${log.lane === "DU" ? "du" : "dd"}">${escapeHtml(log.lane)}</span>
          <div class="alert-actions">
            <span class="log-time">${escapeHtml(log.receivedAtLocalText || log.triggeredAtText)}</span>
            <button type="button" class="icon-button icon-button--danger" data-delete-alert="${escapeHtml(log.id)}" title="刪除這筆訊號">刪除</button>
          </div>
        </div>
        <button type="button" class="log-open-button" data-log-symbol="${escapeHtml(log.symbol)}" data-log-lane="${escapeHtml(log.lane)}">
          <div class="log-symbol">${escapeHtml(log.symbol)}</div>
          <div class="log-meta">${escapeHtml(log.timeframe)} / ${escapeHtml(normalizeAlertText(log.title))}</div>
          ${log.price ? `<div class="log-price">收盤價 ${escapeHtml(log.price)}</div>` : ""}
        </button>
      </article>
    `).join("");
  }

  function updateCounters(model) {
    const filteredSymbols = getFilteredSymbols(model);
    const lastUpdatedLabel = document.getElementById("last-updated-label");
    const symbolCountLabel = document.getElementById("symbol-count-label");
    const alertCountLabel = document.getElementById("alert-count-label");

    if (lastUpdatedLabel) {
      lastUpdatedLabel.textContent = model.updatedAtLocalText || "尚未收到";
    }

    if (symbolCountLabel) {
      symbolCountLabel.textContent = String(filteredSymbols.length);
    }

    if (alertCountLabel) {
      alertCountLabel.textContent = String(model.totalAlerts || 0);
    }
  }

  function updateDiscordStatus() {
    const isOn = Boolean((state.settings.duWebhookUrl || "").trim() || (state.settings.ddWebhookUrl || "").trim());
    discordStatus.textContent = isOn ? "ON" : "OFF";
  }

  function updateDailyResetStatus() {
    dailyResetStatus.textContent = state.settings.dailyResetEnabled ? "ON" : "OFF";
  }

  function syncSettingsForm() {
    duWebhookInput.value = state.settings.duWebhookUrl || "";
    ddWebhookInput.value = state.settings.ddWebhookUrl || "";
    dailyResetEnabledInput.checked = Boolean(state.settings.dailyResetEnabled);
    updateDiscordStatus();
    updateDailyResetStatus();
  }

  async function loadDiscordSettings() {
    try {
      const response = await fetch("/api/settings/discord", { cache: "no-store" });
      if (!response.ok) {
        settingsSaveStatus.textContent = "讀取設定失敗";
        return;
      }

      const settings = await response.json();
      state.settings = {
        duWebhookUrl: settings.duWebhookUrl || "",
        ddWebhookUrl: settings.ddWebhookUrl || "",
        dailyResetEnabled: settings.dailyResetEnabled !== false,
        lastAutoResetLocalDate: settings.lastAutoResetLocalDate || null
      };
      syncSettingsForm();
      settingsSaveStatus.textContent = "設定已載入";
    } catch (error) {
      console.error("Load settings failed.", error);
      settingsSaveStatus.textContent = "讀取設定失敗";
    }
  }

  async function saveDiscordSettings() {
    settingsSaveStatus.textContent = "儲存中...";

    try {
      const response = await fetch("/api/settings/discord", {
        method: "PUT",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          duWebhookUrl: duWebhookInput.value,
          ddWebhookUrl: ddWebhookInput.value,
          dailyResetEnabled: dailyResetEnabledInput.checked
        })
      });

      if (!response.ok) {
        settingsSaveStatus.textContent = "儲存失敗";
        return;
      }

      const settings = await response.json();
      state.settings = {
        duWebhookUrl: settings.duWebhookUrl || "",
        ddWebhookUrl: settings.ddWebhookUrl || "",
        dailyResetEnabled: settings.dailyResetEnabled !== false,
        lastAutoResetLocalDate: settings.lastAutoResetLocalDate || null
      };
      syncSettingsForm();
      settingsSaveStatus.textContent = "已儲存，立即生效";
    } catch (error) {
      console.error("Save settings failed.", error);
      settingsSaveStatus.textContent = "儲存失敗";
    }
  }

  function render() {
    const filteredSymbols = getFilteredSymbols(state.snapshot);
    ensureSelection(filteredSymbols);
    updateCounters(state.snapshot);
    renderFilterChips(symbolFilterBar, symbolFilters, state.symbolFilter, "symbol-filter");
    renderFilterChips(logFilterBar, logFilters, state.logFilter, "log-filter");
    renderSummary(filteredSymbols);
    renderDetail(filteredSymbols);
    renderLogs(state.snapshot);
  }

  async function refresh() {
    try {
      const response = await fetch(config.apiUrl || "/api/dashboard", { cache: "no-store" });
      if (!response.ok) {
        return;
      }

      state.snapshot = await response.json();
      render();
    } catch (error) {
      console.error("Dashboard refresh failed.", error);
    }
  }

  async function deleteAlert(alertId) {
    const shouldDelete = window.confirm("要刪除這筆訊號嗎？");
    if (!shouldDelete) {
      return;
    }

    try {
      const response = await fetch(`/api/alerts/${encodeURIComponent(alertId)}`, {
        method: "DELETE"
      });

      if (!response.ok) {
        window.alert("刪除失敗，請稍後再試。");
        return;
      }

      await refresh();
    } catch (error) {
      console.error("Delete alert failed.", error);
      window.alert("刪除失敗，請稍後再試。");
    }
  }

  function connectStream() {
    if (!("EventSource" in window)) {
      return;
    }

    if (state.eventSource) {
      state.eventSource.close();
    }

    const streamUrl = config.streamUrl || "/api/dashboard/stream";
    const eventSource = new EventSource(streamUrl);

    eventSource.addEventListener("snapshot", (event) => {
      try {
        state.snapshot = JSON.parse(event.data);
        render();
      } catch (error) {
        console.error("Unable to parse streaming snapshot.", error);
      }
    });

    eventSource.onerror = () => {
      console.warn("Dashboard stream disconnected. Falling back to polling until it reconnects.");
    };

    state.eventSource = eventSource;
  }

  symbolSearch.addEventListener("input", (event) => {
    state.search = event.target.value || "";
    render();
  });

  discordSettingsForm.addEventListener("submit", (event) => {
    event.preventDefault();
    saveDiscordSettings();
  });

  dailyResetEnabledInput.addEventListener("change", () => {
    state.settings.dailyResetEnabled = dailyResetEnabledInput.checked;
    updateDailyResetStatus();
  });

  clearDiscordSettingsButton.addEventListener("click", () => {
    duWebhookInput.value = "";
    ddWebhookInput.value = "";
    settingsSaveStatus.textContent = "欄位已清空，按立即儲存即可套用";
  });

  symbolFilterBar.addEventListener("click", (event) => {
    const button = event.target.closest("[data-group='symbol-filter']");
    if (!button) {
      return;
    }

    state.symbolFilter = button.dataset.value || "all";
    render();
  });

  logFilterBar.addEventListener("click", (event) => {
    const button = event.target.closest("[data-group='log-filter']");
    if (!button) {
      return;
    }

    state.logFilter = button.dataset.value || "all";
    render();
  });

  summaryRoot.addEventListener("click", (event) => {
    const button = event.target.closest("[data-symbol]");
    if (!button) {
      return;
    }

    state.selectedSymbol = button.dataset.symbol || null;
    const selected = (state.snapshot.symbols || []).find((symbol) => symbol.symbol === state.selectedSymbol);
    state.selectedLane = selected ? getPreferredLane(selected) : "DU";
    render();
  });

  detailRoot.addEventListener("click", (event) => {
    const deleteButton = event.target.closest("[data-delete-alert]");
    if (deleteButton) {
      deleteAlert(deleteButton.dataset.deleteAlert || "");
      return;
    }

    const button = event.target.closest(".lane-tab[data-lane]");
    if (!button) {
      return;
    }

    state.selectedLane = button.dataset.lane || "DU";
    render();
  });

  logRoot.addEventListener("click", (event) => {
    const deleteButton = event.target.closest("[data-delete-alert]");
    if (deleteButton) {
      deleteAlert(deleteButton.dataset.deleteAlert || "");
      return;
    }

    const button = event.target.closest("[data-log-symbol]");
    if (!button) {
      return;
    }

    state.selectedSymbol = button.dataset.logSymbol || null;
    state.selectedLane = button.dataset.logLane || "DU";
    render();
    detailRoot.scrollIntoView({ behavior: "smooth", block: "start" });
  });

  render();
  loadDiscordSettings();
  connectStream();
  refresh();
  window.setInterval(refresh, Number(config.pollMs || 10000));
})();
