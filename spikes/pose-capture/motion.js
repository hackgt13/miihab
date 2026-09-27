// Live AirPods IMU view: read-only viewer of the relay's raw /motion — the patient's two pairs, Mac 1 (this Mac) and
// Mac 2 (the other Mac, over the tailnet), whichever Motion app either runs. The games read one stream that follows
// the more active pair; the relay says which (`following`).
const RELAY = 'ws://127.0.0.1:8767';
const LABEL = { mac1: 'Mac 1 · this Mac', mac2: 'Mac 2 · the other Mac' };
const WINDOW_MS = 6000, STALE_MS = 1000;
const chart = document.getElementById('imu-chart');
const list = document.getElementById('imu-streams');
const empty = document.getElementById('imu-empty');
const streams = new Map();   // key: mac1 | mac2
const relayOpen = new Set();
let following = null;

function connect() {
  const ws = new WebSocket(`${RELAY}/motion?role=viewer`);
  ws.onopen = () => { relayOpen.add('/motion'); };
  ws.onmessage = event => {
    let p; try { p = JSON.parse(event.data); } catch { return; }
    if (p.type === 'motion.disconnected') { streams.get(p.mac)?.card.remove(); streams.delete(p.mac); return; }
    if (p.type === 'motion.sample' && LABEL[p.mac]) add(p.mac, p);
  };
  ws.onclose = () => { relayOpen.delete('/motion'); setTimeout(connect, 1500); };
}

function card(mac) {
  let s = streams.get(mac);
  if (!s) {
    const el = document.createElement('div');
    el.className = 'imu-card';
    el.style.setProperty('--series', `var(--series-${mac === 'mac1' ? 1 : 2})`);
    el.innerHTML = `<div class="imu-name"><i></i><span></span></div>
      <div class="imu-speed"><strong>0.0</strong><small>rad/s</small></div>
      <dl><dt>Peak</dt><dd data-k="peak">—</dd><dt>Rate</dt><dd data-k="rate">—</dd>
      <dt>Roll</dt><dd data-k="roll">—</dd><dt>Pitch</dt><dd data-k="pitch">—</dd><dt>Yaw</dt><dd data-k="yaw">—</dd></dl>
      <p class="imu-note hint" hidden></p>`;
    list.append(el);
    s = { card: el, history: [], arrivals: [], color: '', at: -Infinity, label: LABEL[mac] };
    streams.set(mac, s);
  }
  return s;
}

function add(mac, p) {
  const s = card(mac);
  const now = performance.now();
  const [x, y, z] = p.rotationRate;
  s.last = p; s.at = now;
  s.history.push({ t: now, v: Math.hypot(x, y, z) });
  s.arrivals.push(now);
  s.label = `${LABEL[mac]} · ${p.sourceId} AirPod${following === mac ? ' · games follow this one' : ''}`;
}

// A motion app can hold its relay connection while its AirPods send nothing (Automatic Ear Detection dropped them,
// or they are asleep). Samples never arrive then, so ask the relay who is connected and say which one is silent.
const since = ms => ms < 60000 ? `${Math.round(ms / 1000)} s` : `${Math.floor(ms / 60000)} min ${Math.round(ms % 60000 / 1000)} s`;
async function pollHealth() {
  try {
    const health = await (await fetch('/api/motion-health', { cache: 'no-store' })).json();
    const macs = health.macs ?? {};
    following = health.following ?? null;
    for (const mac of Object.keys(macs)) {
      const s = card(mac), age = macs[mac].ageMs;
      s.silent = age == null ? 'Connected, but no motion has arrived yet.'
        : age > 2000 ? `Connected, but no motion for ${since(age)}. On that Mac: turn off Automatic Ear Detection for the AirPods and keep them as the sound output.`
        : '';
    }
    for (const [mac, s] of streams) if (!(mac in macs)) s.silent = 'Motion app disconnected from the relay.';
  } catch { /* coordinator restarting; keep what is shown */ }
  setTimeout(pollHealth, 1000);
}

// CoreMotion attitude quaternion [x, y, z, w] → roll / pitch / yaw in degrees.
function euler([x, y, z, w]) {
  const d = 180 / Math.PI;
  return {
    roll: Math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y)) * d,
    pitch: Math.asin(Math.max(-1, Math.min(1, 2 * (w * y - z * x)))) * d,
    yaw: Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)) * d,
  };
}

function render() {
  const now = performance.now();
  let fresh = 0;
  for (const s of streams.values()) {
    s.history = s.history.filter(h => now - h.t <= WINDOW_MS);
    s.arrivals = s.arrivals.filter(t => now - t <= 1000);
    const live = now - s.at < STALE_MS;
    if (live) fresh++;
    s.card.dataset.live = String(live);
    s.card.querySelector('.imu-name span').textContent = s.label;
    s.color = getComputedStyle(s.card).getPropertyValue('--series-color').trim();
    s.card.querySelector('.imu-speed strong').textContent = (live ? s.history.at(-1)?.v ?? 0 : 0).toFixed(1);
    const peak = Math.max(0, ...s.history.map(h => h.v));
    const note = s.card.querySelector('.imu-note');
    note.hidden = live || !s.silent; note.textContent = s.silent ?? '';
    if (!s.last) continue;
    const a = euler(s.last.quaternion);
    const set = (k, v) => { s.card.querySelector(`[data-k="${k}"]`).textContent = v; };
    set('peak', `${peak.toFixed(1)} rad/s`);
    set('rate', live ? `${s.arrivals.length} Hz` : 'stale');
    set('roll', `${a.roll.toFixed(0)}°`); set('pitch', `${a.pitch.toFixed(0)}°`); set('yaw', `${a.yaw.toFixed(0)}°`);
    s.color = getComputedStyle(s.card).getPropertyValue('--series-color').trim();
  }
  document.body.dataset.imu = fresh ? 'live' : relayOpen.size ? 'waiting' : 'off';
  empty.hidden = streams.size > 0;
  drawChart(now);
  requestAnimationFrame(render);
}

function drawChart(now) {
  const dpr = window.devicePixelRatio || 1;
  const w = chart.clientWidth, h = chart.clientHeight;
  if (chart.width !== w * dpr || chart.height !== h * dpr) { chart.width = w * dpr; chart.height = h * dpr; }
  const ctx = chart.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  const css = getComputedStyle(chart);
  const peak = Math.max(20, ...[...streams.values()].flatMap(s => s.history.map(p => p.v)));
  const top = Math.ceil(peak / 5) * 5, pad = { l: 34, r: 8, t: 8, b: 18 };
  const X = t => pad.l + (1 - (now - t) / WINDOW_MS) * (w - pad.l - pad.r);
  const Y = v => pad.t + (1 - v / top) * (h - pad.t - pad.b);
  ctx.font = '11px -apple-system, system-ui, sans-serif';
  ctx.fillStyle = css.getPropertyValue('--muted'); ctx.strokeStyle = css.getPropertyValue('--line'); ctx.lineWidth = 1;
  for (let v = 0; v <= top; v += top > 30 ? 10 : 5) {
    ctx.beginPath(); ctx.moveTo(pad.l, Y(v)); ctx.lineTo(w - pad.r, Y(v)); ctx.stroke();
    ctx.fillText(String(v), 4, Y(v) + 4);
  }
  ctx.fillText('rad/s · last 6 s', pad.l, h - 4);
  ctx.lineWidth = 2; ctx.lineJoin = 'round';
  for (const s of streams.values()) {
    if (s.history.length < 2) continue;
    ctx.strokeStyle = s.color || css.getPropertyValue('--accent');
    ctx.beginPath();
    s.history.forEach((p, i) => i ? ctx.lineTo(X(p.t), Y(p.v)) : ctx.moveTo(X(p.t), Y(p.v)));
    ctx.stroke();
  }
}

connect();
pollHealth();
requestAnimationFrame(render);
