const elements = {
  apiBase: document.querySelector('#apiBase'),
  apiKey: document.querySelector('#apiKey'),
  autoRefresh: document.querySelector('#autoRefresh'),
  refreshButton: document.querySelector('#refreshButton'),
  saveButton: document.querySelector('#saveButton'),
  message: document.querySelector('#message'),
  connectionDot: document.querySelector('#connectionDot'),
  connectionText: document.querySelector('#connectionText'),
  bridgeCount: document.querySelector('#bridgeCount'),
  runningCount: document.querySelector('#runningCount'),
  remoteStartCount: document.querySelector('#remoteStartCount'),
  remoteStopCount: document.querySelector('#remoteStopCount'),
  lastUpdated: document.querySelector('#lastUpdated'),
  emptyState: document.querySelector('#emptyState'),
  bridgeTable: document.querySelector('#bridgeTable'),
  bridgeRows: document.querySelector('#bridgeRows'),
  confirmDialog: document.querySelector('#confirmDialog'),
  confirmTitle: document.querySelector('#confirmTitle'),
  confirmMessage: document.querySelector('#confirmMessage')
};

let refreshInProgress = false;
let pendingAction = null;

function normalizedBase() {
  return elements.apiBase.value.trim().replace(/\/+$/, '');
}

function showMessage(text, type = '') {
  elements.message.textContent = text;
  elements.message.className = `message ${type}`.trim();
}

function setConnection(state, text) {
  elements.connectionDot.className = `dot dot-${state}`;
  elements.connectionText.textContent = text;
}

function requestHeaders() {
  return { 'X-Api-Key': elements.apiKey.value.trim() };
}

async function apiRequest(path, options = {}) {
  const response = await fetch(`${normalizedBase()}${path}`, {
    ...options,
    headers: { ...requestHeaders(), ...(options.headers || {}) }
  });
  const text = await response.text();
  let payload;
  try { payload = text ? JSON.parse(text) : null; } catch { payload = { Message: text }; }
  if (!response.ok) throw new Error(payload?.Message || payload?.message || `HTTP ${response.status}`);
  return payload;
}

function badge(text, className) {
  const node = document.createElement('span');
  node.className = `badge ${className}`;
  node.textContent = text;
  return node;
}

function actionButton(label, className, bridgeId, operation) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = `button ${className}`;
  button.textContent = label;
  button.addEventListener('click', () => openConfirmation(bridgeId, operation, label));
  return button;
}

function renderBridges(bridges) {
  elements.bridgeRows.replaceChildren();
  elements.emptyState.hidden = bridges.length > 0;
  elements.bridgeTable.hidden = bridges.length === 0;

  for (const bridge of bridges) {
    const row = document.createElement('tr');
    const id = String(bridge.BridgeId ?? '—');
    const cells = [document.createElement('td'), document.createElement('td'), document.createElement('td'), document.createElement('td'), document.createElement('td'), document.createElement('td')];
    cells[0].textContent = id;
    cells[1].append(badge(bridge.BridgeRunning ? '运行' : '停止', bridge.BridgeRunning ? 'badge-on' : 'badge-off'));
    cells[2].append(badge(bridge.Do1RemoteStart ? '闭合' : '断开', bridge.Do1RemoteStart ? 'badge-on' : 'badge-off'));
    cells[3].append(badge(bridge.Do2CutOriginal ? '已切断' : '未切断', bridge.Do2CutOriginal ? 'badge-cut' : 'badge-off'));
    cells[4].textContent = bridge.Mode || '—';
    cells[5].className = 'actions';
    cells[5].append(
      actionButton('远程开启', 'button-start', id, 'start'),
      actionButton('远程关闭', 'button-stop', id, 'stop'),
      actionButton('释放联动', 'button-release', id, 'release')
    );
    row.append(...cells);
    elements.bridgeRows.append(row);
  }

  elements.bridgeCount.textContent = bridges.length;
  elements.runningCount.textContent = bridges.filter(x => x.BridgeRunning).length;
  elements.remoteStartCount.textContent = bridges.filter(x => x.Do1RemoteStart).length;
  elements.remoteStopCount.textContent = bridges.filter(x => x.Do2CutOriginal).length;
}

async function refresh() {
  if (refreshInProgress) return;
  if (!normalizedBase()) { showMessage('请输入 Server API 地址。', 'error'); return; }
  if (!elements.apiKey.value.trim()) { showMessage('请输入 API Key 后再刷新。', 'error'); return; }

  refreshInProgress = true;
  elements.refreshButton.disabled = true;
  try {
    const bridges = await apiRequest('/api/bridges');
    renderBridges(Array.isArray(bridges) ? bridges : []);
    const now = new Date();
    elements.lastUpdated.textContent = `最后刷新：${now.toLocaleTimeString('zh-CN', { hour12: false })}`;
    setConnection('online', '服务器已连接');
    showMessage(`状态刷新成功，共 ${bridges.length} 台工控机。`, 'success');
  } catch (error) {
    setConnection('error', '连接异常');
    showMessage(`刷新失败：${error.message}`, 'error');
  } finally {
    refreshInProgress = false;
    elements.refreshButton.disabled = false;
  }
}

function openConfirmation(bridgeId, operation, label) {
  pendingAction = { bridgeId, operation };
  elements.confirmTitle.textContent = label;
  elements.confirmMessage.textContent = `确认对登机桥 ${bridgeId} 执行“${label}”吗？请先确认现场无人检修。`;
  elements.confirmDialog.showModal();
}

async function executePendingAction() {
  if (!pendingAction) return;
  const { bridgeId, operation } = pendingAction;
  pendingAction = null;
  try {
    const result = await apiRequest(`/api/bridges/${encodeURIComponent(bridgeId)}/air-conditioner/${operation}`, { method: 'POST' });
    showMessage(result?.Message || '操作成功。', 'success');
    await refresh();
  } catch (error) {
    showMessage(`操作失败：${error.message}`, 'error');
  }
}

function saveSettings() {
  localStorage.setItem('t2.apiBase', normalizedBase());
  sessionStorage.setItem('t2.apiKey', elements.apiKey.value);
  localStorage.setItem('t2.autoRefresh', String(elements.autoRefresh.checked));
  showMessage('连接设置已保存；API Key 会在关闭本标签页后清除。', 'success');
  refresh();
}

function restoreSettings() {
  elements.apiBase.value = localStorage.getItem('t2.apiBase') || elements.apiBase.value;
  elements.apiKey.value = sessionStorage.getItem('t2.apiKey') || '';
  const savedAutoRefresh = localStorage.getItem('t2.autoRefresh');
  if (savedAutoRefresh !== null) elements.autoRefresh.checked = savedAutoRefresh === 'true';
}

elements.refreshButton.addEventListener('click', refresh);
elements.saveButton.addEventListener('click', saveSettings);
elements.apiKey.addEventListener('keydown', event => { if (event.key === 'Enter') refresh(); });
elements.confirmDialog.addEventListener('close', () => {
  if (elements.confirmDialog.returnValue === 'confirm') executePendingAction();
  else pendingAction = null;
});

restoreSettings();
if (elements.apiKey.value) refresh();
setInterval(() => { if (elements.autoRefresh.checked && elements.apiKey.value.trim()) refresh(); }, 3000);
