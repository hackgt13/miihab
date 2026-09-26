// Clinician review: all numbers come from saved exercise summaries (engine output) or the labeled synthetic
// fixture. The narrative sentences here are deterministic templates over those numbers, not model output.
const $ = s => document.querySelector(s);
const get = async p => { const r = await fetch(p, { cache: 'no-store' }); if (!r.ok) throw Error(await r.text()); return r.json(); };
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const deg = v => v == null ? '—' : `${Math.round(v)}°`;
const REASONS = { did_not_reach_target: 'short of target', trunk_compensation: 'trunk compensation', tracking_lost: 'tracking lost', too_fast: 'too fast' };

let state = { plans: [], active: null, history: null, sessions: [] };

async function load() {
  const [plans, active, history, sessions] = await Promise.all([get('/api/plans'), get('/api/plans/active'), get('/api/history'), get('/api/sessions')]);
  state = { plans, active, history, sessions: sessions.filter(s => s.calibrated && s.attempted > 0) };
  render();
}

function rows() {
  const synthetic = state.history.sessions.map(s => ({ ...s, synthetic: true, when: s.label, trunk: s.trunkCompensationReps }));
  const real = [...state.sessions].reverse().map(s => ({
    synthetic: false, when: new Date(s.endedAt).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }),
    planVersion: s.planVersion, attempted: s.attempted, valid: s.valid, prescribed: s.prescribed, medianValidPeakDeg: s.medianValidPeakDeg,
    trunk: s.invalidReasons?.trunk_compensation ?? 0, simulated: s.simulated !== false, reasons: s.invalidReasons ?? {}, tracking: s.validFrameRatio, id: s.exerciseId, targetDeg: s.config?.targetDeg,
  }));
  return [...synthetic, ...real];
}

function render() {
  const { history, active } = state, all = rows();
  $('#patient-summary').textContent = history.patient.summary;
  $('#patient-goal').textContent = history.patient.goal;
  const baseline = all[0], latest = all[all.length - 1], latestReal = all.filter(r => !r.synthetic).at(-1);

  // Functional-change review request: explicit, deterministic rule inputs.
  const reported = history.sessions.at(-1)?.patientReport ?? '';
  const reachUp = latest.medianValidPeakDeg != null && baseline.medianValidPeakDeg != null && latest.medianValidPeakDeg - baseline.medianValidPeakDeg >= 10;
  const compensation = latest.trunk > 0;
  const stiffness = /stiff|tight|spasm/i.test(reported);
  const trigger = $('#trigger');
  if (reachUp && compensation && stiffness) {
    trigger.hidden = false;
    trigger.innerHTML = `<strong>Functional change · review requested</strong>This is a request to review, not a diagnosis.<ul>
      <li>Reach up ${Math.round(latest.medianValidPeakDeg - baseline.medianValidPeakDeg)}° since ${esc(baseline.when)}</li>
      <li>Trunk compensation on ${latest.trunk} of ${latest.attempted} attempts in the latest session</li>
      <li>Patient reports: “${esc(reported)}”</li></ul>`;
  } else trigger.hidden = true;

  $('#change-sentence').textContent = latest === baseline ? 'No sessions recorded yet.' :
    `Median reach is ${deg(latest.medianValidPeakDeg)}, up from ${deg(baseline.medianValidPeakDeg)} at ${baseline.when}. ` +
    `${latest.valid} of ${latest.attempted} attempted reps met the plan in the latest session` +
    (latest.trunk ? `; ${latest.trunk} ${latest.trunk === 1 ? 'was' : 'were'} not counted for trunk compensation.` : '.') +
    (latestReal ? '' : ' No live session yet — all points shown are synthetic history.');

  const tiles = [
    ['Median reach', deg(latest.medianValidPeakDeg), `baseline ${deg(baseline.medianValidPeakDeg)}`],
    ['Reps counted', `${latest.valid}/${latest.attempted}`, latest.prescribed ? `plan asks for ${latest.prescribed}` : 'attempted'],
    ['Trunk compensation', `${latest.trunk}`, 'reps not counted'],
    ['Tracking quality', latest.tracking != null ? `${Math.round(latest.tracking * 100)}%` : '—', 'frames with required joints'],
  ];
  $('#tiles').innerHTML = tiles.map(([l, v, s]) => `<div class="tile"><div class="label">${l}</div><div class="value">${v}</div><div class="sub">${s}</div></div>`).join('');

  $('#sessions tbody').innerHTML = [...all].reverse().map(r => `<tr class="${r.synthetic ? 'synthetic' : ''}">
    <td>${esc(r.when)}${r.synthetic ? ' <span class="muted">(synthetic)</span>' : r.simulated ? ' <span class="muted">(simulated input)</span>' : ''}</td><td>v${r.planVersion}</td>
    <td class="num">${r.valid}/${r.attempted}</td><td class="num">${deg(r.medianValidPeakDeg)}</td>
    <td>${r.synthetic ? (r.trunk ? `${r.trunk} × trunk compensation` : '—') : Object.entries(r.reasons).map(([k, n]) => `${n} × ${REASONS[k] ?? k}`).join(', ') || '—'}</td>
    <td>${r.tracking != null ? Math.round(r.tracking * 100) + '%' : '—'}</td></tr>`).join('');

  $('#plan-version').textContent = `v${active.version}`;
  const e = active.exercise;
  $('#plan').innerHTML = [['Exercise', `Seated shoulder raise · ${e.side}`], ['Target', `${e.targetDeg}°`], ['Reps', e.prescribedReps],
    ['Hold', `${e.holdMs} ms`], ['Trunk lean limit', `${e.maxTrunkDeviationDeg}°`], ['Coaching note', esc(active.coachingNote)]]
    .map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('');
  $('#plan-approved').textContent = `Approved ${new Date(active.approvedAt).toLocaleString()} by ${active.approvedBy}. Next session uses this version.`;
  const form = $('#plan-form');
  for (const k of ['targetDeg', 'prescribedReps', 'holdMs', 'maxTrunkDeviationDeg']) form.elements[k].value = e[k];
  form.elements.coachingNote.value = active.coachingNote;
  $('#plan-history').innerHTML = [...state.plans].reverse().map(p => `<li><strong>v${p.version}</strong> · target ${p.exercise.targetDeg}°, ${p.exercise.prescribedReps} reps, lean ≤ ${p.exercise.maxTrunkDeviationDeg}°
    <div class="why">${esc(p.rationale)}</div><div class="muted small">${new Date(p.approvedAt).toLocaleString()} · ${esc(p.approvedBy)}</div></li>`).join('');

  drawChart(all);
}

function drawChart(all) {
  const svg = $('#chart'), tip = $('#tooltip');
  const W = svg.clientWidth || 700, H = 260, m = { l: 44, r: 16, t: 14, b: 34 };
  svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  const pts = all.filter(r => r.medianValidPeakDeg != null);
  const target = state.active.exercise.targetDeg;
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
    g += `<circle cx="${x(i)}" cy="${y(p.medianValidPeakDeg)}" r="5" fill="${p.synthetic ? c('--surface') : c('--series-1')}" stroke="${c('--series-1')}" stroke-width="2"/>
      <text x="${x(i)}" y="${H - 12}" text-anchor="middle" font-size="11.5" fill="${c('--muted')}">${esc(p.synthetic ? p.when : 'S' + (i - state.history.sessions.length + 1))}</text>
      <rect data-i="${i}" x="${x(i) - 14}" y="${m.t}" width="28" height="${H - m.t - m.b}" fill="transparent"/>`;
  });
  const last = pts.at(-1);
  if (last) g += `<text x="${x(pts.length - 1) - 8}" y="${y(last.medianValidPeakDeg) - 10}" text-anchor="end" font-size="12.5" font-weight="600" fill="${c('--text')}">${deg(last.medianValidPeakDeg)}</text>`;
  svg.innerHTML = g;
  svg.querySelectorAll('rect[data-i]').forEach(r => {
    r.addEventListener('mouseenter', () => { const p = pts[+r.dataset.i];
      tip.innerHTML = `<strong>${esc(p.when)}</strong>${p.synthetic ? ' · synthetic' : ''}<br>Median peak ${deg(p.medianValidPeakDeg)} · counted ${p.valid}/${p.attempted} · plan v${p.planVersion}`;
      tip.hidden = false; tip.style.left = `${Math.min(x(+r.dataset.i) + 12, W - 240)}px`; tip.style.top = `${y(p.medianValidPeakDeg) - 10}px`; });
    r.addEventListener('mouseleave', () => { tip.hidden = true; });
  });
}

$('#plan-form').addEventListener('submit', async ev => {
  ev.preventDefault();
  const f = ev.target, status = $('#form-status'), button = $('#approve');
  const exercise = Object.fromEntries(['targetDeg', 'prescribedReps', 'holdMs', 'maxTrunkDeviationDeg'].map(k => [k, Number(f.elements[k].value)]));
  button.disabled = true; status.className = 'form-status'; status.textContent = 'Saving…';
  try {
    const r = await fetch('/api/plans', { method: 'POST', body: JSON.stringify({
      exercise, rationale: f.elements.rationale.value, coachingNote: f.elements.coachingNote.value,
      expectedActiveVersion: state.active.version, basedOnExerciseIds: state.sessions.slice(0, 3).map(s => s.exerciseId) }) });
    const body = await r.json();
    if (!r.ok) throw Error(body.error);
    f.elements.rationale.value = '';
    status.className = 'form-status ok'; status.textContent = `Plan v${body.version} approved. The patient's next session will use it.`;
    await load();
  } catch (e) { status.className = 'form-status err'; status.textContent = e.message; }
  finally { button.disabled = false; }
});

addEventListener('resize', () => state.history && drawChart(rows()));
load().catch(e => { $('#change-sentence').textContent = 'Could not load data: ' + e.message; });
setInterval(() => get('/api/sessions').then(s => { const f = s.filter(x => x.calibrated && x.attempted > 0);
  if (f.length !== state.sessions.length) { state.sessions = f; render(); } }).catch(() => {}), 5000);
