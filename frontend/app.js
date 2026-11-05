const $ = selector => document.querySelector(selector);
const elements = {
  apiBase: $('#apiBase'), apiKey: $('#apiKey'), autoRefresh: $('#autoRefresh'), refreshButton: $('#refreshButton'), saveButton: $('#saveButton'), message: $('#message'),
  connectionDot: $('#connectionDot'), connectionText: $('#connectionText'), bridgeCount: $('#bridgeCount'), linkageCount: $('#linkageCount'), flightWindowCount: $('#flightWindowCount'), remoteStopCount: $('#remoteStopCount'),
  lastUpdated: $('#lastUpdated'), emptyState: $('#emptyState'), bridgeTable: $('#bridgeTable'), bridgeRows: $('#bridgeRows'), startBeforeMinutes: $('#startBeforeMinutes'), stopAfterMinutes: $('#stopAfterMinutes'),
  saveLinkageSettings: $('#saveLinkageSettings'), enableAllButton: $('#enableAllButton'), disableAllButton: $('#disableAllButton'), allLinkageStatus: $('#allLinkageStatus'), flightDataSource: $('#flightDataSource'),
  flightTabCount: $('#flightTabCount'), standFilter: $('#standFilter'), clearStandFilter: $('#clearStandFilter'), flightRows: $('#flightRows'), flightTable: $('#flightTable'), flightEmptyState: $('#flightEmptyState'), flightListSource: $('#flightListSource'),
  bridgesView: $('#bridgesView'), flightsView: $('#flightsView'), confirmDialog: $('#confirmDialog'), confirmTitle: $('#confirmTitle'), confirmMessage: $('#confirmMessage')
};

let refreshInProgress = false;
let pendingAction = null;
let linkageSettingsDirty = false;
let bridges = [];
let flights = [];
let linkage = { Settings: {}, Bridges: [], DataSource: '' };

function normalizedBase() { return elements.apiBase.value.trim().replace(/\/+$/, ''); }
function showMessage(text, type = '') { elements.message.textContent = text; elements.message.className = `message ${type}`.trim(); }
function setConnection(state, text) { elements.connectionDot.className = `dot dot-${state}`; elements.connectionText.textContent = text; }
function requestHeaders() { return { 'X-Api-Key': elements.apiKey.value.trim() }; }

async function apiRequest(path, options = {}) {
  const response = await fetch(`${normalizedBase()}${path}`, { ...options, headers: { ...requestHeaders(), ...(options.headers || {}) } });
  const text = await response.text();
  let payload;
  try { payload = text ? JSON.parse(text) : null; } catch { payload = { Message: text }; }
  if (!response.ok) throw new Error(payload?.Message || payload?.message || `HTTP ${response.status}`);
  return payload;
}

function badge(text, className) { const node = document.createElement('span'); node.className = `badge ${className}`; node.textContent = text; return node; }
function button(label, className, action) { const node = document.createElement('button'); node.type = 'button'; node.className = `button ${className}`; node.textContent = label; node.addEventListener('click', action); return node; }
function linkageStateFor(bridgeId) { return (linkage.Bridges || []).find(x => String(x.BridgeId).toLowerCase() === String(bridgeId).toLowerCase()); }
function parseServerDate(value) { if (!value) return null; const match = /^\/Date\((-?\d+)/.exec(value); return match ? new Date(Number(match[1])) : new Date(value); }
function formatTime(value) { const date = parseServerDate(value); if (!date || Number.isNaN(date.getTime())) return '—'; return date.toLocaleString('zh-CN', { hour12: false, month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' }); }
function deviationMinutes(flight) { return Math.round((parseServerDate(flight.EstimatedArrivalUtc) - parseServerDate(flight.ScheduledArrivalUtc)) / 60000); }

function renderBridges() {
  elements.bridgeRows.replaceChildren();
  elements.emptyState.hidden = bridges.length > 0;
  elements.bridgeTable.hidden = bridges.length === 0;
  for (const bridge of bridges) {
    const id = String(bridge.BridgeId ?? '—');
    const auto = linkageStateFor(id) || { StandId: id.replace(/[A-Za-z]+$/, ''), Enabled: false, ShouldRun: false, AutomationStatus: '未启用' };
    const row = document.createElement('tr');
    const cells = Array.from({ length: 7 }, () => document.createElement('td'));
    const idBlock = document.createElement('div'); idBlock.className = 'bridge-id';
    const idStrong = document.createElement('strong'); idStrong.textContent = id;
    const standSmall = document.createElement('small'); standSmall.textContent = `机位 ${auto.StandId}`;
    idBlock.append(idStrong, standSmall); cells[0].append(idBlock);
    cells[1].append(badge(bridge.BridgeRunning ? '运行' : '停止', bridge.BridgeRunning ? 'badge-on' : 'badge-off'));
    cells[2].append(badge(bridge.Do1RemoteStart ? '闭合' : '断开', bridge.Do1RemoteStart ? 'badge-on' : 'badge-off'));
    cells[3].append(badge(bridge.Do2CutOriginal ? '已切断' : '未切断', bridge.Do2CutOriginal ? 'badge-cut' : 'badge-off'));
    cells[4].append(badge(auto.Enabled ? '已开启' : '已关闭', auto.Enabled ? 'badge-on' : 'badge-off'));
    const autoText = document.createElement('div'); autoText.className = 'automation-status'; autoText.textContent = auto.AutomationStatus || '等待调度';
    if (auto.LastError) { const error = document.createElement('small'); error.className = 'inline-error'; error.textContent = auto.LastError; autoText.append(error); }
    cells[5].append(autoText);
    cells[6].className = 'actions actions-wrap';
    cells[6].append(
      button('远程开启', 'button-start', () => confirmApiAction('远程开启', `确认对登机桥 ${id} 执行远程开启吗？`, `/api/bridges/${encodeURIComponent(id)}/air-conditioner/start`)),
      button('远程关闭', 'button-stop', () => confirmApiAction('远程关闭', `确认对登机桥 ${id} 执行远程关闭吗？`, `/api/bridges/${encodeURIComponent(id)}/air-conditioner/stop`)),
      button('释放联动', 'button-release', () => confirmApiAction('释放联动', `确认释放登机桥 ${id} 的远程控制吗？`, `/api/bridges/${encodeURIComponent(id)}/air-conditioner/release`)),
      button(auto.Enabled ? '关闭航班联动' : '开启航班联动', auto.Enabled ? 'button-secondary' : 'button-flight', () => confirmApiAction(auto.Enabled ? '关闭航班联动' : '开启航班联动', `确认${auto.Enabled ? '关闭' : '开启'}登机桥 ${id} 的航班联动自动启停吗？`, `/api/bridges/${encodeURIComponent(id)}/flight-linkage/${auto.Enabled ? 'disable' : 'enable'}`)),
      button('查看该机位航班', 'button-link', () => showFlights(auto.StandId))
    );
    row.append(...cells); elements.bridgeRows.append(row);
  }
  elements.bridgeCount.textContent = bridges.length;
  elements.linkageCount.textContent = (linkage.Bridges || []).filter(x => x.Enabled).length;
  elements.flightWindowCount.textContent = (linkage.Bridges || []).filter(x => x.Enabled && x.ShouldRun).length;
  elements.remoteStopCount.textContent = bridges.filter(x => x.Do2CutOriginal).length;
}

function renderFlightFilter() {
  const current = elements.standFilter.value;
  const stands = [...new Set(flights.map(x => x.StandId))].sort();
  elements.standFilter.replaceChildren(new Option('全部机位', ''));
  for (const stand of stands) elements.standFilter.add(new Option(stand, stand));
  if (stands.includes(current)) elements.standFilter.value = current;
}

function renderFlights() {
  const stand = elements.standFilter.value;
  const visible = flights.filter(x => !stand || x.StandId === stand);
  elements.flightRows.replaceChildren();
  for (const flight of visible) {
    const row = document.createElement('tr');
    const values = [flight.FlightNumber, flight.StandId, `${flight.Origin} → ${flight.Destination}`, `${flight.EstimatedFlightMinutes} 分钟`, formatTime(flight.ScheduledArrivalUtc), formatTime(flight.EstimatedArrivalUtc)];
    for (const value of values) { const cell = document.createElement('td'); cell.textContent = value ?? '—'; row.append(cell); }
    const offset = deviationMinutes(flight); const deviation = document.createElement('td'); deviation.append(badge(offset === 0 ? '准点' : `${offset > 0 ? '+' : ''}${offset} 分钟`, offset > 3 ? 'badge-cut' : 'badge-off')); row.append(deviation);
    const status = document.createElement('td'); status.append(badge(flight.Status || '预计到达', flight.Status === '已到达' ? 'badge-off' : 'badge-on')); row.append(status);
    const updated = document.createElement('td'); updated.textContent = formatTime(flight.UpdatedUtc); row.append(updated);
    elements.flightRows.append(row);
  }
  elements.flightTable.hidden = visible.length === 0;
  elements.flightEmptyState.hidden = visible.length !== 0;
  elements.flightTabCount.textContent = flights.length;
}

function renderLinkage() {
  const settings = linkage.Settings || {};
  if (!linkageSettingsDirty) {
    elements.startBeforeMinutes.value = settings.StartBeforeMinutes ?? 15;
    elements.stopAfterMinutes.value = settings.StopAfterMinutes ?? 20;
  }
  elements.allLinkageStatus.textContent = settings.AllEnabled ? '所有在线桥已启用' : '未全部启用';
  elements.allLinkageStatus.className = `badge ${settings.AllEnabled ? 'badge-on' : 'badge-off'}`;
  elements.flightDataSource.textContent = linkage.DataSource || '—'; elements.flightListSource.textContent = linkage.DataSource || '—';
}

async function refresh() {
  if (refreshInProgress) return;
  if (!normalizedBase()) { showMessage('请输入 Server API 地址。', 'error'); return; }
  if (!elements.apiKey.value.trim()) { showMessage('请输入 API Key 后再刷新。', 'error'); return; }
  refreshInProgress = true; elements.refreshButton.disabled = true;
  try {
    [bridges, linkage, flights] = await Promise.all([apiRequest('/api/bridges'), apiRequest('/api/flight-linkage'), apiRequest('/api/flights')]);
    renderLinkage(); renderBridges(); renderFlightFilter(); renderFlights();
    elements.lastUpdated.textContent = `最后刷新：${new Date().toLocaleTimeString('zh-CN', { hour12: false })}`;
    setConnection('online', '服务器已连接'); showMessage(`刷新成功：${bridges.length} 台工控机，${flights.length} 个航班。`, 'success');
  } catch (error) { setConnection('error', '连接异常'); showMessage(`刷新失败：${error.message}`, 'error'); }
  finally { refreshInProgress = false; elements.refreshButton.disabled = false; }
}

function confirmApiAction(title, message, path) {
  pendingAction = async () => { await apiRequest(path, { method: 'POST' }); await refresh(); };
  elements.confirmTitle.textContent = title; elements.confirmMessage.textContent = message; elements.confirmDialog.showModal();
}

async function executePendingAction() {
  if (!pendingAction) return; const action = pendingAction; pendingAction = null;
  try { await action(); showMessage('操作成功。', 'success'); }
  catch (error) { showMessage(`操作失败：${error.message}`, 'error'); }
}

function switchView(name) {
  const flightsVisible = name === 'flights';
  elements.bridgesView.hidden = flightsVisible; elements.flightsView.hidden = !flightsVisible;
  document.querySelectorAll('.view-tab').forEach(tab => tab.classList.toggle('active', tab.dataset.view === name));
}
function showFlights(standId) { switchView('flights'); elements.standFilter.value = standId; renderFlights(); document.querySelector('#flightsTitle').scrollIntoView({ behavior: 'smooth', block: 'start' }); }

async function saveLinkageSettings() {
  const start = Number(elements.startBeforeMinutes.value), stop = Number(elements.stopAfterMinutes.value);
  if (!Number.isInteger(start) || start < 0 || start > 240 || !Number.isInteger(stop) || stop < 0 || stop > 240) { showMessage('联动分钟数必须是 0–240 的整数。', 'error'); return; }
  try { await apiRequest('/api/flight-linkage/settings', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ StartBeforeMinutes: start, StopAfterMinutes: stop }) }); linkageSettingsDirty = false; await refresh(); showMessage('航班联动时间已保存。', 'success'); }
  catch (error) { showMessage(`保存失败：${error.message}`, 'error'); }
}

function saveConnectionSettings() {
  localStorage.setItem('t2.apiBase', normalizedBase()); sessionStorage.setItem('t2.apiKey', elements.apiKey.value); localStorage.setItem('t2.autoRefresh', String(elements.autoRefresh.checked));
  showMessage('连接设置已保存；API Key 会在关闭本标签页后清除。', 'success'); refresh();
}
function restoreSettings() { elements.apiBase.value = localStorage.getItem('t2.apiBase') || elements.apiBase.value; elements.apiKey.value = sessionStorage.getItem('t2.apiKey') || ''; const saved = localStorage.getItem('t2.autoRefresh'); if (saved !== null) elements.autoRefresh.checked = saved === 'true'; }

elements.refreshButton.addEventListener('click', refresh); elements.saveButton.addEventListener('click', saveConnectionSettings); elements.apiKey.addEventListener('keydown', event => { if (event.key === 'Enter') refresh(); });
elements.startBeforeMinutes.addEventListener('input', () => { linkageSettingsDirty = true; }); elements.stopAfterMinutes.addEventListener('input', () => { linkageSettingsDirty = true; });
elements.saveLinkageSettings.addEventListener('click', saveLinkageSettings); elements.enableAllButton.addEventListener('click', () => confirmApiAction('一键开启所有桥', '确认开启所有当前在线登机桥的航班联动自动启停吗？', '/api/flight-linkage/all/enable'));
elements.disableAllButton.addEventListener('click', () => confirmApiAction('一键关闭所有桥', '确认关闭所有当前在线登机桥的航班联动并释放远程控制吗？', '/api/flight-linkage/all/disable'));
elements.standFilter.addEventListener('change', renderFlights); elements.clearStandFilter.addEventListener('click', () => { elements.standFilter.value = ''; renderFlights(); });
document.querySelectorAll('.view-tab').forEach(tab => tab.addEventListener('click', () => switchView(tab.dataset.view)));
elements.confirmDialog.addEventListener('close', () => { if (elements.confirmDialog.returnValue === 'confirm') executePendingAction(); else pendingAction = null; });

restoreSettings(); if (elements.apiKey.value) refresh();
setInterval(() => { if (elements.autoRefresh.checked && elements.apiKey.value.trim()) refresh(); }, 3000);
