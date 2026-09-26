// Clinician review: every number here comes from a saved exercise summary (engine output). There is no
// fixture. The narrative sentences here are deterministic templates over those numbers, not model output.
const $ = s => document.querySelector(s);
const get = async p => { const r = await fetch(p, { cache: 'no-store' }); if (!r.ok) throw Error(await r.text()); return r.json(); };
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const deg = v => v == null ? '—' : `${Math.round(v)}°`;
const REASONS = { did_not_reach_target: 'short of target', trunk_compensation: 'trunk compensation', tracking_lost: 'tracking lost', too_fast: 'too fast' };

let state = { plans: [], active: null, sessions: [], proposals: [], library: {}, games: [] };
// This portal view follows the first measured prescription (the shoulder raise); the rest are listed in the plan card.
const primary = plan => plan.activities.find(a => a.exerciseKind) ?? plan.activities[0];
const label = a => a.exerciseKind ? state.library[a.exerciseKind]?.label ?? a.exerciseKind : ({ 'golf.adaptive': 'Golf with a friend' }[a.activityId] ?? a.activityId);
const sensorOf = a => state.library[a.exerciseKind]?.sensor ?? 'Camera';
// Summaries from before prescription ids name only the camera shoulder raise.
const sessionFor = (s, a) => s.prescriptionId ? s.prescriptionId === a.id : a.exerciseKind === 'shoulder-raise.v1' || s.exercise === 'seated_shoulder_raise' && a.id.startsWith('shoulder-raise');

async function load() {
  const [plans, active, sessions, proposals, library, games] = await Promise.all([get('/api/plans'), get('/api/plans/active'),
    get('/api/sessions'), get('/api/proposals'), get('/api/exercises'), get('/api/game-movement').catch(() => [])]);
  state = { plans, active, sessions: sessions.filter(s => s.calibrated && s.attempted > 0), proposals, library, games };
  render();
}

function rows() {
  // The chart and table follow the plan's shoulder raise; older summaries have no plan exercise id.
  const x = primary(state.active);
  return [...state.sessions].reverse().filter(s => sessionFor(s, x)).map(s => ({
    when: new Date(s.endedAt).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }),
    planVersion: s.planVersion, attempted: s.attempted, valid: s.valid, prescribed: s.prescribed, medianValidPeakDeg: s.medianValidPeakDeg,
    trunk: s.invalidReasons?.trunk_compensation ?? 0, simulated: s.simulated !== false, reasons: s.invalidReasons ?? {}, tracking: s.validFrameRatio, id: s.exerciseId, targetDeg: s.config?.targetDeg,
    sensor: s.sensor ?? 'pose',
    // How well the counted reps were made (exercise/quality.ts): the mean form score, the best hold, controlled lowerings.
    form: s.quality?.formScore ?? null, bestHoldMs: s.quality?.hold?.bestMs ?? null,
    controlledLowers: s.quality?.tempo ? `${s.quality.tempo.controlledLowers}/${s.quality.tempo.reps}` : null,
    fatigued: s.quality?.consistency?.fatigued === true,
  }));
}

function render() {
  const { active } = state, all = rows();
  $('#patient-goal').textContent = active.goal?.text ?? '—';
  const baseline = all[0], latest = all.at(-1);

  // Every tile, sentence and rule below reads a measured session. With none recorded there is nothing
  // to say, and saying it plainly is the point: this page used to fill the gap with authored history,
  // which read exactly like evidence and was the first thing anyone looked at.
  $('#trigger').hidden = true;
  if (!all.length) {
    $('#change-sentence').textContent =
      'No sessions recorded yet. Nothing on this page is estimated or filled in — it stays empty until a session is measured.';
    $('#tiles').innerHTML = '';
    $('#sessions tbody').innerHTML = '<tr><td colspan="7" class="muted">No measured sessions yet.</td></tr>';
    $('#chart').innerHTML = '';
    renderPlan();
    renderGames();
    return;
  }

  // Functional-change review request: explicit, deterministic rule inputs.
  const reachUp = latest.medianValidPeakDeg != null && baseline.medianValidPeakDeg != null && latest.medianValidPeakDeg - baseline.medianValidPeakDeg >= 10;
  const trigger = $('#trigger');
  if (reachUp && latest.trunk > 0) {
    trigger.hidden = false;
    trigger.innerHTML = `<strong>Functional change · review requested</strong>This is a request to review, not a diagnosis.<ul>
      <li>Reach up ${Math.round(latest.medianValidPeakDeg - baseline.medianValidPeakDeg)}° since ${esc(baseline.when)}</li>
      <li>Trunk compensation on ${latest.trunk} of ${latest.attempted} attempts in the latest session</li></ul>`;
  }

  $('#change-sentence').textContent = latest === baseline ?
    `One session recorded. Median reach ${deg(latest.medianValidPeakDeg)}, ${latest.valid} of ${latest.attempted} reps counted. ` +
    'A trend needs a second session.' :
    `Median reach is ${deg(latest.medianValidPeakDeg)}, up from ${deg(baseline.medianValidPeakDeg)} at ${baseline.when}. ` +
    `${latest.valid} of ${latest.attempted} attempted reps met the plan in the latest session` +
    (latest.trunk ? `; ${latest.trunk} ${latest.trunk === 1 ? 'was' : 'were'} not counted for trunk compensation.` : '.');

  const tiles = [
    ['Median reach', deg(latest.medianValidPeakDeg), `baseline ${deg(baseline.medianValidPeakDeg)}`],
    ['Reps counted', `${latest.valid}/${latest.attempted}`, latest.prescribed ? `plan asks for ${latest.prescribed}` : 'attempted'],
    ['Trunk compensation', `${latest.trunk}`, 'reps not counted'],
    ['Tracking quality', latest.tracking != null ? `${Math.round(latest.tracking * 100)}%` : '—', 'frames with required joints'],
  ];
  $('#tiles').innerHTML = tiles.map(([l, v, s]) => `<div class="tile"><div class="label">${l}</div><div class="value">${v}</div><div class="sub">${s}</div></div>`).join('');

  $('#sessions tbody').innerHTML = [...all].reverse().map(r => `<tr>
    <td>${esc(r.when)}${r.simulated ? ' <span class="muted">(simulated input)</span>' : ''}</td><td>v${r.planVersion}</td>
    <td class="num">${r.valid}/${r.attempted}</td><td class="num">${deg(r.medianValidPeakDeg)}</td>
    <td class="num" title="${r.bestHoldMs != null ? `best hold ${(r.bestHoldMs / 1000).toFixed(1)} s · ` : ''}${r.controlledLowers != null ? `${r.controlledLowers} lowered with control` : ''}">${r.form != null ? Math.round(r.form * 100) + '%' : '—'}${r.fatigued ? ' <span class="muted small">fatigue</span>' : ''}</td>
    <td>${Object.entries(r.reasons).map(([k, n]) => `${n} × ${REASONS[k] ?? k}`).join(', ') || '—'}</td>
    <td>${r.tracking != null ? Math.round(r.tracking * 100) + '%' : '—'}</td>
    <td>${r.sensor === 'imu' ? '<span class="muted small">AirPod</span>' : `<button type="button" class="small-btn" data-replay="${esc(r.id)}">Replay</button>`}</td></tr>`).join('');
  document.querySelectorAll('[data-replay]').forEach(b => b.addEventListener('click', () => openReplay(b.dataset.replay)));

  renderPlan();
  drawChart(all);
  renderGames();
}

/** The plan card and its editor. Independent of the evidence: a new plan is set up before any session exists. */
function renderPlan() {
  const active = state.active;
  $('#plan-version').textContent = `v${active.version}`;
  const e = primary(active);
  $('#plan').innerHTML = [['Goal', esc(active.goal.text)],
    ...active.activities.map(a => [esc(label(a)), !a.exerciseKind ? `${a.targetCount} · ${esc(a.note)}` :
      `${esc(a.params.side)} · ${esc(sensorOf(a))} · ${a.params.targetDeg}–${a.params.targetMaxDeg}° · ${a.targetCount} reps` +
      (a.params.loadKg ? ` · ${a.params.loadKg} kg` : '') + (a.progression ? `<div class="muted small">${a.progression.autoApply ? 'Auto' : 'Clinician-approved'} levels ` +
      `${a.progression.minTargetDeg}–${a.progression.maxTargetDeg}°, +${a.progression.stepDeg}° after ${a.progression.sessionsToProgress} good sessions</div>` : '')]),
    ['Coaching note', esc(active.coachingNote)]]
    .map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('');
  renderProposals();
  $('#plan-approved').textContent = `Approved ${new Date(active.approvedAt).toLocaleString()} by ${active.approvedBy}. Next session uses this version.`;
  const form = $('#plan-form');
  $('#form-exercise').textContent = `Editing ${label(e)} · ${e.params.side}`;
  for (const k of ['targetDeg', 'targetMaxDeg', 'holdMs', 'maxCompensationDeg']) form.elements[k].value = e.params[k] ?? '';
  form.elements.prescribedReps.value = e.targetCount;
  // The same movement measured another way: the kinds that share this one's movement in the catalog.
  // A camera prescription from before MediaPipe was switched off offers its AirPod equivalent.
  const movement = state.library[e.exerciseKind]?.movement ?? { 'shoulder-raise.v1': 'shoulder_raise' }[e.exerciseKind];
  form.elements.exerciseKind.innerHTML = Object.values(state.library).filter(k => k.movement === movement)
    .map(k => `<option value="${esc(k.kind)}">${esc(k.sensor)}</option>`).join('');
  form.elements.exerciseKind.value = state.library[e.exerciseKind] ? e.exerciseKind : form.elements.exerciseKind.options[0]?.value ?? '';
  $('#trunk-field').hidden = sensorOf(e) !== 'Camera';
  form.elements.maxTargetDeg.value = e.progression?.maxTargetDeg ?? ''; form.elements.autoApply.checked = !!e.progression?.autoApply;
  form.elements.coachingNote.value = active.coachingNote;
  $('#plan-history').innerHTML = [...state.plans].reverse().map(p => { const x = primary(p); return `<li><strong>v${p.version}</strong>` +
    `${p.origin === 'auto-progression' ? ' <span class="tag">auto</span>' : ''} · ${esc(label(x))} ${x.params.targetDeg}–${x.params.targetMaxDeg ?? '?'}°, ${x.targetCount} reps
    <div class="why">${esc(p.rationale)}</div><div class="muted small">${new Date(p.approvedAt).toLocaleString()} · ${esc(p.approvedBy)}</div></li>`; }).join('');
}

// Games are practice, not tests: what is shown is how much the patient moved and how alike the movements were,
// compared with their own earlier sessions. Game score, distance and power are left out on purpose.
const GAME = { 'golf.adaptive': ['Golf', 'swing', 'swings'], 'bowling.adaptive': ['Bowling', 'roll', 'rolls'] };
const num = (v, unit = '') => v == null ? '—' : `${Math.round(v)}${unit}`;
const signed = v => v == null ? '—' : `${v > 0 ? '+' : ''}${Math.round(v)}%`;
function renderGames() {
  const games = state.games.filter(g => g && g.movements != null);
  const when = g => new Date(g.endedAt).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  if (!games.length) {
    $('#games-sentence').textContent = 'No golf or bowling sessions yet. A round appears here when it ends, or when the patient leaves it partway.';
    $('#games-tiles').innerHTML = ''; $('#games tbody').innerHTML = ''; $('#games-notes').textContent = ''; return;
  }
  // The headline is the newest session with movement in it; a session where the sensor sat still stays in the table.
  const latest = games.find(g => g.movements > 0) ?? games[0], [name, one, many] = GAME[latest.activityId] ?? [latest.activityId, 'movement', 'movements'];
  const previous = games.find(g => g !== latest && g.activityId === latest.activityId && g.movements > 0 && Date.parse(g.endedAt) < Date.parse(latest.endedAt));
  const steadier = previous && latest.speedVariationPct != null && previous.speedVariationPct != null
    ? latest.speedVariationPct < previous.speedVariationPct - 3 ? ', steadier than the session before'
    : latest.speedVariationPct > previous.speedVariationPct + 3 ? ', less steady than the session before' : ', about as steady as the session before' : '';
  $('#games-sentence').textContent = `${name}, ${when(latest)}${latest.completed ? '' : ' (left partway)'}: ${latest.movements} ${latest.movements === 1 ? one : many}` +
    (latest.medianPeakDegPerSec != null ? `, typically ${num(latest.medianPeakDegPerSec, '°/s')} through ${num(latest.medianExcursionDeg, '°')}` : '') +
    (latest.speedVariationPct != null ? `. Speed varied ${num(latest.speedVariationPct, '%')} from ${one} to ${one}${steadier}.` : '.') +
    (latest.earlyToLateSpeedChangePct != null && Math.abs(latest.earlyToLateSpeedChangePct) >= 15
      ? ` The last third were ${Math.abs(Math.round(latest.earlyToLateSpeedChangePct))}% ${latest.earlyToLateSpeedChangePct < 0 ? 'slower' : 'faster'} than the first.` : '');
  const tiles = [
    [`${name} ${many}`, `${latest.movements}`, `${latest.activeSeconds ?? 0} s moving`],
    ['Typical speed', num(latest.medianPeakDegPerSec, '°/s'), previous ? `previous ${num(previous.medianPeakDegPerSec, '°/s')}` : 'median peak rotation'],
    ['Typical turn', num(latest.medianExcursionDeg, '°'), `${latest.sensor} rotation`],
    ['Speed variation', num(latest.speedVariationPct, '%'), 'lower is steadier'],
    ...(latest.forearmShareMedian != null ? [['Forearm ÷ club', `${Math.round(latest.forearmShareMedian * 100)}%`, `of the club's turn · ${latest.forearmShareMovements} ${many}`]] : []),
  ];
  $('#games-tiles').innerHTML = tiles.map(([l, v, sub]) => `<div class="tile"><div class="label">${esc(l)}</div><div class="value">${esc(v)}</div><div class="sub">${esc(sub)}</div></div>`).join('');
  $('#games tbody').innerHTML = games.map(g => `<tr>
    <td>${esc(when(g))}${g.completed ? '' : ' <span class="muted">(left partway)</span>'}</td><td>${esc(GAME[g.activityId]?.[0] ?? g.activityId)}</td>
    <td class="num">${g.movements}</td><td class="num">${num(g.medianPeakDegPerSec, '°/s')}</td><td class="num">${num(g.medianExcursionDeg, '°')}</td>
    <td class="num">${num(g.speedVariationPct, '%')}</td><td class="num">${signed(g.earlyToLateSpeedChangePct)}</td>
    <td class="num">${g.forearmShareMedian != null ? Math.round(g.forearmShareMedian * 100) + '%' : '—'}</td>
    <td class="num">${g.tracking?.sensorCoverage != null ? Math.round(g.tracking.sensorCoverage * 100) + '%' : '—'}</td></tr>`).join('');
  $('#games-notes').textContent = [...new Set(games.flatMap(g => g.notes ?? []))].join(' ') +
    ' Early → late compares the median speed of the last third of movements with the first third. Sensor is the share of the session the AirPod was reporting.';
}


function drawChart(all) {
  const svg = $('#chart'), tip = $('#tooltip');
  const W = svg.clientWidth || 700, H = 260, m = { l: 44, r: 16, t: 14, b: 34 };
  svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  const pts = all.filter(r => r.medianValidPeakDeg != null);
  const target = Number(primary(state.active).params.targetDeg);
  const ys = [...pts.map(p => p.medianValidPeakDeg), target];
  const lo = Math.floor((Math.min(...ys) - 10) / 10) * 10, hi = Math.ceil((Math.max(...ys) + 10) / 10) * 10;
  const x = i => m.l + (pts.length < 2 ? (W - m.l - m.r) / 2 : i * (W - m.l - m.r) / (pts.length - 1));
  const y = v => m.t + (hi - v) / (hi - lo) * (H - m.t - m.b);
  const css = getComputedStyle(document.documentElement), c = n => css.getPropertyValue(n).trim();
  let g = '';
  for (let v = lo; v <= hi; v += 10) g += `<line x1="${m.l}" x2="${W - m.r}" y1="${y(v)}" y2="${y(v)}" stroke="${c('--line')}" stroke-width="1"/>
    <text x="${m.l - 8}" y="${y(v) + 4}" text-anchor="end" font-size="11.5" fill="${c('--muted')}">${v}°</text>`;
  g += `<line x1="${m.l}" x2="${W - m.r}" y1="${y(target)}" y2="${y(target)}" stroke="${c('--ref')}" stroke-width="1.5" stroke-dasharray="5 4"/>
    <text x="${W - m.r}" y="${y(target) - 6}" text-anchor="end" font-size="11.5" fill="${c('--text-2')}">plan target ${target}°</text>`;
  g += `<polyline fill="none" stroke="${c('--series-1')}" stroke-width="2" stroke-linejoin="round" points="${pts.map((p, i) => `${x(i)},${y(p.medianValidPeakDeg)}`).join(' ')}"/>`;
  pts.forEach((p, i) => {
    g += `<circle cx="${x(i)}" cy="${y(p.medianValidPeakDeg)}" r="5" fill="${c('--series-1')}" stroke="${c('--series-1')}" stroke-width="2"/>
      <text x="${x(i)}" y="${H - 12}" text-anchor="middle" font-size="11.5" fill="${c('--muted')}">${esc('S' + (i + 1))}</text>
      <rect data-i="${i}" x="${x(i) - 14}" y="${m.t}" width="28" height="${H - m.t - m.b}" fill="transparent"/>`;
  });
  const last = pts.at(-1);
  if (last) g += `<text x="${x(pts.length - 1) - 8}" y="${y(last.medianValidPeakDeg) - 10}" text-anchor="end" font-size="12.5" font-weight="600" fill="${c('--text')}">${deg(last.medianValidPeakDeg)}</text>`;
  svg.innerHTML = g;
  svg.querySelectorAll('rect[data-i]').forEach(r => {
    r.addEventListener('mouseenter', () => { const p = pts[+r.dataset.i];
      tip.innerHTML = `<strong>${esc(p.when)}</strong><br>Median peak ${deg(p.medianValidPeakDeg)} · counted ${p.valid}/${p.attempted} · plan v${p.planVersion}`;
      tip.hidden = false; tip.style.left = `${Math.min(x(+r.dataset.i) + 12, W - 240)}px`; tip.style.top = `${y(p.medianValidPeakDeg) - 10}px`; });
    r.addEventListener('mouseleave', () => { tip.hidden = true; });
  });
}

$('#plan-form').elements.exerciseKind.addEventListener('change', ev => { $('#trunk-field').hidden = state.library[ev.target.value]?.sensor !== 'Camera'; });
$('#plan-form').addEventListener('submit', async ev => {
  ev.preventDefault();
  const f = ev.target, status = $('#form-status'), button = $('#approve');
  const e = primary(state.active);
  const kind = f.elements.exerciseKind.value;
  const change = { exerciseKind: kind, targetCount: Number(f.elements.prescribedReps.value),
    params: Object.fromEntries(['targetDeg', 'targetMaxDeg', 'holdMs'].map(k => [k, Number(f.elements[k].value)])) };
  // The camera sees the trunk; with the AirPod alone the lean limit is left to the kind's default.
  if (state.library[kind]?.sensor === 'Camera') change.params.maxCompensationDeg = Number(f.elements.maxCompensationDeg.value) || 12;
  change.progression = { maxTargetDeg: Number(f.elements.maxTargetDeg.value), autoApply: f.elements.autoApply.checked };
  button.disabled = true; status.className = 'form-status'; status.textContent = 'Saving…';
  try {
    const r = await fetch('/api/plans', { method: 'POST', body: JSON.stringify({
      changes: { [e.id]: change }, rationale: f.elements.rationale.value, coachingNote: f.elements.coachingNote.value,
      expectedActiveVersion: state.active.version, basedOnExerciseIds: state.sessions.slice(0, 3).map(s => s.exerciseId) }) });
    const body = await r.json();
    if (!r.ok) throw Error(body.error);
    f.elements.rationale.value = '';
    status.className = 'form-status ok'; status.textContent = `Plan v${body.version} approved. The patient's next session will use it.`;
    await load();
  } catch (e) { status.className = 'form-status err'; status.textContent = e.message; }
  finally { button.disabled = false; }
});

addEventListener('resize', () => state.active && drawChart(rows()));
load().then(async () => {
  // Deep link: /portal/?replay=<exerciseId>&rep=<n> opens that session's replay at that repetition.
  const q = new URLSearchParams(location.search);
  if (q.get('replay')) { await openReplay(q.get('replay'));
    const r = replay?.reps.find(x => x.rep === Number(q.get('rep'))); if (r) showFrame(frameAt((r.startMs + r.endMs) / 2)); }
}).catch(e => { $('#change-sentence').textContent = 'Could not load data: ' + e.message; });
setInterval(() => get('/api/sessions').then(s => { const f = s.filter(x => x.calibrated && x.attempted > 0);
  if (f.length !== state.sessions.length) { state.sessions = f; render(); } }).catch(() => {}), 5000);


// ---- Movement replay: recorded landmarks + engine angles, reps on a timeline ----
const BONES = [[11,12],[11,23],[12,24],[23,24],[11,13],[13,15],[12,14],[14,16],[23,25],[24,26],[0,11],[0,12]];
let replay = null, playing = false, cursor = 0, lastTick = 0;
async function openReplay(id) {
  const card = $('#replay-card'); card.hidden = false; $('#replay-title').textContent = 'Loading replay…';
  try { replay = await get(`/api/sessions/${id}/replay`); }
  catch (e) { $('#replay-title').textContent = 'Replay unavailable: ' + e.message; return; }
  const r = replay;
  $('#replay-title').textContent = `Movement replay · plan v${r.planVersion} · ${new Date(r.endedAt).toLocaleString([], {month:'short', day:'numeric', hour:'numeric', minute:'2-digit'})}${r.simulated ? ' · simulated input' : ''}`;
  $('#replay-target').textContent = `target ${r.targetDeg}°`; $('#replay-trunk-limit').textContent = `limit ${r.maxTrunkDeviationDeg}°`;
  $('#replay-seek').max = r.frames.length - 1; cursor = 0; playing = false; $('#replay-play').textContent = 'Play';
  drawStrip(); showFrame(0); card.scrollIntoView({behavior:'smooth', block:'start'});
}
function frameAt(ms) { let i = 0; while (i < replay.frames.length - 1 && replay.frames[i + 1].t <= ms) i++; return i; }
function showFrame(i) {
  cursor = Math.max(0, Math.min(i, replay.frames.length - 1)); const f = replay.frames[cursor];
  $('#replay-seek').value = cursor; $('#replay-time').textContent = `${(f.t / 1000).toFixed(1)} s`;
  $('#replay-angle').textContent = f.a == null ? '—' : `${Math.round(f.a)}°`;
  $('#replay-trunk').textContent = f.k == null ? '—' : `${Math.round(f.k)}°`;
  const rep = replay.reps.find(r => f.t >= r.startMs && f.t <= r.endMs);
  $('#replay-rep').textContent = rep ? `Rep ${rep.rep}: ${rep.valid ? 'counted' : 'not counted — ' + (REASONS[rep.reason] ?? rep.reason)} · peak ${Math.round(rep.peakDeg)}°` : 'Between repetitions';
  drawPose(f); moveStripCursor(f.t);
}
function drawPose(f) {
  const cv = $('#replay-canvas'), g = cv.getContext('2d'), css = getComputedStyle(document.documentElement), c = n => css.getPropertyValue(n).trim();
  g.clearRect(0, 0, cv.width, cv.height);
  if (!f.p) return;
  // Fit the recorded body into the canvas using the visible landmarks' bounds.
  const vis = f.p.filter(p => p[2] >= .5); if (!vis.length) return;
  const xs = vis.map(p => p[0]), ys = vis.map(p => p[1]);
  const cx = (Math.min(...xs) + Math.max(...xs)) / 2, cy = (Math.min(...ys) + Math.max(...ys)) / 2;
  const span = Math.max(Math.max(...xs) - Math.min(...xs), (Math.max(...ys) - Math.min(...ys)) * 1.2, .25) * 1.35;
  const P = i => [cv.width / 2 + (f.p[i][0] - cx) / span * cv.height, cv.height / 2 + (f.p[i][1] - cy) / span * cv.height];
  const arm = replay.side === 'left' ? [[11,13],[13,15]] : [[12,14],[14,16]];
  g.lineCap = 'round';
  for (const [a, b] of BONES) { if (f.p[a][2] < .5 || f.p[b][2] < .5) continue;
    const measured = arm.some(([x, y]) => x === a && y === b);
    g.strokeStyle = measured ? c('--series-1') : c('--muted'); g.lineWidth = measured ? 7 : 3;
    g.beginPath(); g.moveTo(...P(a)); g.lineTo(...P(b)); g.stroke(); }
  for (const i of [0, 11, 12, 13, 14, 15, 16, 23, 24]) { if (f.p[i][2] < .5) continue;
    g.fillStyle = c('--text'); g.beginPath(); g.arc(...P(i), i === 0 ? 7 : 4, 0, Math.PI * 2); g.fill(); }
}
function drawStrip() {
  const svg = $('#replay-strip'), W = svg.clientWidth || 700, H = 120, m = { l: 36, r: 8, t: 8, b: 20 };
  svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  const css = getComputedStyle(document.documentElement), c = n => css.getPropertyValue(n).trim();
  const T = replay.frames.at(-1).t || 1, x = t => m.l + t / T * (W - m.l - m.r), y = v => m.t + (1 - Math.min(v, 150) / 150) * (H - m.t - m.b);
  let g = '';
  for (const r of replay.reps) g += `<rect data-rep="${r.rep}" x="${x(r.startMs)}" y="${m.t}" width="${Math.max(2, x(r.endMs) - x(r.startMs))}" height="${H - m.t - m.b}" fill="${r.valid ? c('--series-1') : c('--muted')}" fill-opacity="${r.valid ? .12 : .22}" rx="4"/>
    <text x="${(x(r.startMs) + x(r.endMs)) / 2}" y="${H - 5}" text-anchor="middle" font-size="11" fill="${c('--text-2')}">${r.valid ? r.rep : r.rep + ' ✕'}</text>`;
  for (const v of [0, 50, 100, 150]) g += `<text x="${m.l - 6}" y="${y(v) + 4}" text-anchor="end" font-size="10.5" fill="${c('--muted')}">${v}°</text>`;
  g += `<line x1="${m.l}" x2="${W - m.r}" y1="${y(replay.targetDeg)}" y2="${y(replay.targetDeg)}" stroke="${c('--ref')}" stroke-dasharray="5 4" stroke-width="1.5"/>`;
  const pts = replay.frames.filter(f => f.a != null).map(f => `${x(f.t)},${y(f.a)}`).join(' ');
  g += `<polyline points="${pts}" fill="none" stroke="${c('--series-1')}" stroke-width="2" stroke-linejoin="round"/>`;
  g += `<line id="strip-cursor" x1="0" x2="0" y1="${m.t}" y2="${H - m.b}" stroke="${c('--text')}" stroke-width="1.5"/>`;
  svg.innerHTML = g; svg._x = x; svg._inv = px => (px - m.l) / (W - m.l - m.r) * T;
  svg.onclick = ev => { const rep = ev.target.closest('[data-rep]');
    if (rep) { const r = replay.reps.find(q => q.rep === +rep.dataset.rep); showFrame(frameAt(r.startMs)); return; }
    const box = svg.getBoundingClientRect(); showFrame(frameAt(svg._inv((ev.clientX - box.left) / box.width * W))); };
}
function moveStripCursor(t) { const svg = $('#replay-strip'), l = svg.querySelector('#strip-cursor'); if (l && svg._x) { const X = svg._x(t); l.setAttribute('x1', X); l.setAttribute('x2', X); } }
$('#replay-seek').addEventListener('input', e => { playing = false; $('#replay-play').textContent = 'Play'; showFrame(+e.target.value); });
$('#replay-play').addEventListener('click', () => { if (!replay) return; playing = !playing; $('#replay-play').textContent = playing ? 'Pause' : 'Play'; if (playing && cursor >= replay.frames.length - 1) cursor = 0; lastTick = 0; });
$('#replay-close').addEventListener('click', () => { $('#replay-card').hidden = true; playing = false; });
(function tick(now) { if (playing && replay) { const f = replay.frames[cursor]; const target = f.t + (lastTick ? now - lastTick : 0);
  const i = frameAt(target); if (i >= replay.frames.length - 1) { playing = false; $('#replay-play').textContent = 'Play'; } showFrame(i); }
  lastTick = now; requestAnimationFrame(tick); })(0);

const DECISION = { progress: ['Level up', 'good'], regress: ['Step back', 'warn'], clinician_review: ['Needs you', 'warn'], hold: ['Hold', ''] };
function renderProposals() {
  const pending = state.proposals.filter(p => p.status === 'pending');
  const recent = state.proposals.filter(p => p.status !== 'pending' && p.status !== 'superseded').slice(0, 3);
  const item = p => {
    const [name, tone] = DECISION[p.decision];
    const change = p.to ? `${p.from.targetDeg}° → ${p.to.targetDeg}°` : `${p.from.targetDeg}°`;
    const status = { applied: `applied as v${p.appliedPlanVersion}`, approved: `approved as v${p.appliedPlanVersion}`, dismissed: 'dismissed', info: '' }[p.status] ?? '';
    return `<div class="proposal ${tone}"><div class="proposal-head"><strong>${name}</strong> <span>${esc(p.exerciseLabel)} · ${change}</span></div>
      <div class="why">${p.reasons.map(esc).join(' ')}</div>
      <div class="muted small">${new Date(p.createdAt).toLocaleString()} · ${p.evidence.length} session${p.evidence.length === 1 ? '' : 's'}${p.evidence.some(e => e.simulated) ? ' (simulated)' : ''}${status ? ' · ' + status : ''}</div>
      ${p.status === 'pending' ? `<div class="proposal-actions">${p.to ? `<button type="button" class="small-btn" data-approve="${p.id}">Approve</button>` : ''}
        <button type="button" class="ghost" data-dismiss="${p.id}">Dismiss</button></div>` : ''}</div>`;
  };
  $('#proposals').innerHTML = (pending.length ? pending.map(item).join('') : '<p class="muted small">Nothing waiting for you.</p>') +
    (recent.length ? `<h3 class="small muted">Recent</h3>${recent.map(item).join('')}` : '');
  document.querySelectorAll('[data-approve],[data-dismiss]').forEach(b => b.addEventListener('click', async () => {
    const id = b.dataset.approve ?? b.dataset.dismiss, action = b.dataset.approve ? 'approve' : 'dismiss';
    b.disabled = true;
    const r = await fetch(`/api/proposals/${id}/${action}`, { method: 'POST', body: JSON.stringify({ approvedBy: 'PM&R physician (demo)' }) });
    if (!r.ok) { b.disabled = false; alert((await r.json()).error); return; }
    await load();
  }));
}
