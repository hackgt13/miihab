// Capture initialization and inference extracted from Wheelgentic web/main.js
// lines 3575–3599 and 3622–3632. See README.md for provenance and adaptations.
import { FilesetResolver, PoseLandmarker } from './vendor/vision_bundle.mjs';
import { assessFraming, EXERCISE_VIEWS, FRAMING_RULE, jointIssue } from './framing.mjs';

const video = document.getElementById('cam');
const canvas = document.getElementById('overlay');
const context = canvas.getContext('2d');
const ui = Object.fromEntries(['camera', 'start', 'file', 'stop', 'export', 'status',
  'source', 'frames', 'subject', 'latency', 'placeholder', 'framing', 'unity', 'bridge'].map(id => [id, document.getElementById(id)]));
const framingCards = EXERCISE_VIEWS.map(view => {
  const card = document.createElement('div');
  const label = document.createElement('span');
  const result = document.createElement('strong');
  const detail = document.createElement('small');
  label.textContent = view.label;
  card.append(label, result, detail);
  ui.framing.append(card);
  return { ...view, card, result, detail };
});
const MAX_FRAMES = 10000;
const CONNECTIONS = [[0,1],[1,2],[2,3],[3,7],[0,4],[4,5],[5,6],[6,8],[9,10],
  [11,12],[11,13],[13,15],[15,17],[15,19],[15,21],[17,19],[12,14],[14,16],
  [16,18],[16,20],[16,22],[18,20],[11,23],[12,24],[23,24],[23,25],[25,27],
  [27,29],[29,31],[27,31],[24,26],[26,28],[28,30],[30,32],[28,32]];
let landmarker = null;
let delegate = null;
let active = false;
let busy = false;
let frames = [];
let session = null;
let objectURL = null;
let lastVideoTime = -1;
const captureClock = new Worker('./capture-clock.js');
captureClock.onmessage = () => step();
let unitySocket = null;
let unityWanted = true;
let unityRetry = null;

function openUnityBridge() {
  if (!unityWanted || unitySocket) return;
  const socket = new WebSocket('ws://127.0.0.1:8766/pose?role=producer');
  unitySocket = socket;
  ui.bridge.textContent = 'Connecting to the local Unity bridge…';
  document.body.dataset.bridge = 'connecting';
  socket.onopen = () => {
    document.body.dataset.bridge = 'connected';
    ui.unity.textContent = 'Disconnect Unity';
    ui.bridge.textContent = 'Unity bridge connected · pose is recorded locally while capture runs.';
  };
  socket.onmessage = event => {
    try {
      const command = JSON.parse(event.data);
      if (command.type === 'capture.start') {
        if (active) video.play().catch(fail);
        else startPose();
      }
    } catch (error) { console.warn('Capture command failed', error); }
  };
  socket.onclose = event => {
    if (unitySocket !== socket) return;
    unitySocket = null;
    ui.unity.textContent = 'Reconnect Unity';
    document.body.dataset.bridge = unityWanted ? 'waiting' : 'paused';
    ui.bridge.textContent = unityWanted ?
      `Unity bridge disconnected${event.reason ? ': ' + event.reason : '.'} Retrying…` : 'Unity bridge paused.';
    if (unityWanted) unityRetry = setTimeout(openUnityBridge, 1500);
  };
  socket.onerror = () => { document.body.dataset.bridge = 'waiting'; ui.bridge.textContent = 'Waiting for the local Unity bridge on port 8766…'; };
}
function connectUnity() {
  if (unitySocket?.readyState === WebSocket.OPEN) {
    unityWanted = false;
    unitySocket.close();
    ui.unity.textContent = 'Reconnect Unity';
    ui.bridge.textContent = 'Unity bridge paused.';
    document.body.dataset.bridge = 'paused';
  } else {
    unityWanted = true;
    clearTimeout(unityRetry);
    unitySocket?.close();
    unitySocket = null;
    openUnityBridge();
  }
}
function sendUnityStatus(reason) {
  if (unitySocket?.readyState === WebSocket.OPEN) unitySocket.send(JSON.stringify({type:'pose.status', payload:{reason}}));
}

function status(message, error = false) {
  ui.status.textContent = message;
  ui.status.dataset.error = String(error);
  if (error) sendUnityStatus(message);
}

function setButtons() {
  ui.start.disabled = busy || active;
  ui.camera.disabled = busy || active;
  ui.file.disabled = busy || active;
  ui.stop.disabled = busy || !active;
  ui.export.disabled = busy || !frames.length;
  document.body.dataset.capture = active ? 'live' : busy ? 'starting' : 'idle';
}

function clearOverlay() { context.clearRect(0, 0, canvas.width, canvas.height); }

function showFraming(framing = null, subjectDetected = false) {
  for (const view of framingCards) {
    const state = framing?.[view.id];
    view.card.dataset.visible = String(Boolean(state?.jointsInView));
    view.card.dataset.state = !state ? 'waiting' : state.jointsInView && subjectDetected ? 'ok' : 'adjust';
    view.result.textContent = !state ? 'Waiting' : state.jointsInView ? 'Joints in view' : 'Adjust framing';
    view.detail.textContent = !state ? 'Start capture to check.' : !subjectDetected ? 'No person detected.' :
      state.jointsInView ? 'Keep these joints visible throughout the motion.' :
        state.missing.map(item => `${item.joint}: ${item.reason}`).join(' · ');
  }
}

function releaseSource() {
  captureClock.postMessage('stop');
  video.pause();
  if (video.srcObject) video.srcObject.getTracks().forEach(track => track.stop());
  video.srcObject = null;
  delete video.dataset.stream;
  video.removeAttribute('src');
  video.load();
  if (objectURL) URL.revokeObjectURL(objectURL);
  objectURL = null;
  clearOverlay();
}

function beginSession(source) {
  sendUnityStatus('New capture warming up');
  releaseSource();
  frames = [];
  lastVideoTime = -1;
  session = {
    schemaVersion: 'kinesthetic.pose-capture.v1',
    sessionID: crypto.randomUUID(),
    startedAt: new Date().toISOString(),
    timeOriginMs: performance.timeOrigin,
    source,
    model: 'pose_landmarker_lite.task',
    coordinates: {
      image: 'MediaPipe normalized image coordinates; unmirrored source',
      world: 'MediaPipe estimated world landmarks; hip-centered; unmodified',
    },
    timing: {
      sourceMediaTimeMs: 'HTMLVideoElement.currentTime × 1000; media timeline, not a hardware exposure timestamp',
      observedAtMonotonicMs: 'performance.now() immediately before inference',
      inferenceCompletedAtMonotonicMs: 'performance.now() after synchronous inference',
      inferenceDurationMs: 'Inference completion minus observation time',
    },
    maxFrames: MAX_FRAMES,
    framingRule: FRAMING_RULE,
  };
  ui.frames.textContent = '0';
  ui.subject.textContent = '—';
  ui.latency.textContent = '—';
  ui.source.textContent = source.kind === 'camera' ? 'Live camera' : 'Recorded video';
  ui.placeholder.hidden = true;
  showFraming();
}

async function loadLandmarker() {
  if (landmarker) return;
  status('Loading the local MediaPipe model…');
  sendUnityStatus('Loading pose tracking…');
  // Original Wheelgentic initialization; dependency/model paths are unchanged.
  const fileset = await FilesetResolver.forVisionTasks('./vendor/wasm');
  const opts = (delegate) => ({
    baseOptions: { modelAssetPath: './models/pose_landmarker_lite.task', delegate },
    runningMode: 'VIDEO',
    numPoses: 1,
  });
  try {
    landmarker = await PoseLandmarker.createFromOptions(fileset, opts('GPU'));
    delegate = 'GPU';
  } catch (gpuError) {
    console.warn('MediaPipe GPU initialization failed; trying CPU.', gpuError);
    landmarker = await PoseLandmarker.createFromOptions(fileset, opts('CPU'));
    delegate = 'CPU';
  }
}

async function refreshCameras() {
  const selected = ui.camera.value;
  const devices = await navigator.mediaDevices.enumerateDevices();
  ui.camera.replaceChildren(new Option('Default camera', ''));
  for (const [index, device] of devices.filter(d => d.kind === 'videoinput').entries()) {
    ui.camera.add(new Option(device.label || `Camera ${index + 1}`, device.deviceId));
  }
  const available = [...ui.camera.options];
  if (available.some(option => option.value === selected && selected)) ui.camera.value = selected;
}

async function startPose() {
  if (busy || active) return;
  busy = true;
  setButtons();
  try {
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      throw new Error('Camera access requires localhost or HTTPS. A plain HTTP LAN address will not work.');
    }
    beginSession({ kind: 'camera', deviceID: ui.camera.value || null });
    status('Waiting for camera permission…');
    sendUnityStatus('Allow camera access in the browser');
    // Original Wheelgentic getUserMedia/play sequence, with selected-camera support.
    const requestedCamera = ui.camera.value;
    video.srcObject = await navigator.mediaDevices.getUserMedia({
      audio: false,
      video: { width: 640, height: 480, ...(requestedCamera ? { deviceId: { exact: requestedCamera } } : {}) },
    });
    video.dataset.stream = 'true';
    await video.play();
    await refreshCameras();
    const track = video.srcObject.getVideoTracks()[0];
    session.source.label = track.label;
    session.source.settings = track.getSettings();
    await loadLandmarker();
    startLoop();
  } catch (error) {
    fail(error);
  } finally {
    busy = false;
    setButtons();
  }
}

async function openRecordedVideo(file) {
  if (!file || busy || active) return;
  busy = true;
  setButtons();
  try {
    beginSession({ kind: 'recorded-video', name: file.name, sizeBytes: file.size, type: file.type });
    status('Loading the selected video…');
    objectURL = URL.createObjectURL(file);
    video.src = objectURL;
    await new Promise((resolve, reject) => {
      video.addEventListener('loadedmetadata', resolve, { once: true });
      video.addEventListener('error', () => reject(new Error('The browser cannot decode this video. Try an MP4 with H.264 video.')), { once: true });
    });
    session.source.durationMs = Number.isFinite(video.duration) ? video.duration * 1000 : null;
    await loadLandmarker();
    startLoop();
    await video.play();
  } catch (error) {
    fail(error);
  } finally {
    busy = false;
    ui.file.value = '';
    setButtons();
  }
}

function startLoop() {
  active = true;
  session.delegate = delegate;
  session.width = video.videoWidth;
  session.height = video.videoHeight;
  status(`Warming up tracking · ${delegate} · the first inference can take a few seconds…`);
  setButtons();
  // Inference must continue when Unity is in front; rAF stops in hidden tabs.
  captureClock.postMessage('start');
}

function copyLandmarks(landmarks) {
  if (!landmarks) return [];
  return landmarks.map((point, index) => ({
    index,
    x: Number.isFinite(point.x) ? point.x : null,
    y: Number.isFinite(point.y) ? point.y : null,
    z: Number.isFinite(point.z) ? point.z : null,
    visibility: Number.isFinite(point.visibility) ? point.visibility : null,
    presence: Number.isFinite(point.presence) ? point.presence : null,
  }));
}

function drawOverlay(landmarks) {
  const width = video.videoWidth, height = video.videoHeight;
  if (!width || !height) return;
  canvas.width = width;
  canvas.height = height;
  // Match the actual contained video image, including any letterboxing.
  const box = video.getBoundingClientRect();
  const scale = Math.min(box.width / width, box.height / height);
  canvas.style.width = `${width * scale}px`;
  canvas.style.height = `${height * scale}px`;
  clearOverlay();
  if (!landmarks.length) return;
  const valid = point => point && Number.isFinite(point.x) && Number.isFinite(point.y);
  context.lineWidth = Math.max(2, width / 350);
  context.strokeStyle = '#5C9EC6';
  context.globalAlpha = .7;
  for (const [a, b] of CONNECTIONS) {
    const p = landmarks[a], q = landmarks[b];
    if (jointIssue(p) || jointIssue(q)) continue;
    context.beginPath();
    context.moveTo(p.x * width, p.y * height);
    context.lineTo(q.x * width, q.y * height);
    context.stroke();
  }
  context.globalAlpha = 1;
  for (const point of landmarks) {
    if (!valid(point) || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) continue;
    context.fillStyle = (point.visibility ?? 0) >= .5 ? '#CFE4F0' : '#FF7C73';
    context.beginPath();
    context.arc(point.x * width, point.y * height, Math.max(3, width / 220), 0, Math.PI * 2);
    context.fill();
  }
}

function step() {
  if (!active) return;
  try {
    // Same new-video-frame gate and synchronous VIDEO inference as Wheelgentic.
    if (landmarker && video.readyState >= 2 && video.currentTime !== lastVideoTime) {
      lastVideoTime = video.currentTime;
      const observedAtMonotonicMs = performance.now();
      const res = landmarker.detectForVideo(video, observedAtMonotonicMs);
      const inferenceCompletedAtMonotonicMs = performance.now();
      const imageLandmarks = copyLandmarks(res.landmarks?.[0]);
      const worldLandmarks = copyLandmarks(res.worldLandmarks?.[0]);
      const framing = assessFraming(imageLandmarks);
      const frame = {
        frameID: frames.length,
        source: session.source.kind,
        sourceMediaTimeMs: lastVideoTime * 1000,
        observedAtMonotonicMs,
        inferenceCompletedAtMonotonicMs,
        inferenceDurationMs: inferenceCompletedAtMonotonicMs - observedAtMonotonicMs,
        subjectDetected: imageLandmarks.length > 0,
        framing,
        imageLandmarks,
        worldLandmarks,
      };
      frames.push(frame);
      if (unitySocket?.readyState === WebSocket.OPEN && unitySocket.bufferedAmount < 128 * 1024) {
        unitySocket.send(JSON.stringify({schemaVersion:'kinesthetic.session.v1', type:'pose.frame',
          sessionId:session.sessionID, sourceId:'browser-pose', sequence:frame.frameID,
          captureMonotonicMs:null, planVersion:null, calibrationId:null, payload:frame}));
      }
      if (frames.length === 1) {
        session.firstInferenceDurationMs = frame.inferenceDurationMs;
        status(`Capturing locally · ${delegate} · check the exercise framing below.`);
      }
      drawOverlay(imageLandmarks);
      showFraming(framing, frame.subjectDetected);
      ui.frames.textContent = String(frames.length);
      ui.subject.textContent = frame.subjectDetected ? 'Detected' : 'Not detected';
      ui.latency.textContent = `${frame.inferenceDurationMs.toFixed(1)} ms`;
      ui.export.disabled = false;
      if (frames.length >= MAX_FRAMES) {
        stopCapture(`Stopped at the ${MAX_FRAMES.toLocaleString()}-frame memory limit. Export JSON to keep this session.`);
        return;
      }
    }

  } catch (error) {
    fail(error);
  }
}

function stopCapture(message = 'Capture stopped. Export JSON to keep the recorded frames.') {
  sendUnityStatus('Capture stopped');
  active = false;
  if (session) session.stoppedAt = new Date().toISOString();
  releaseSource();
  ui.subject.textContent = 'Stopped';
  showFraming();
  ui.placeholder.hidden = false;
  status(message);
  setButtons();
}

function fail(error) {
  console.error(error);
  stopCapture();
  status(`${error.message || error} Check that models/pose_landmarker_lite.task and the vendor files are present.`, true);
}

function exportFrames() {
  if (!frames.length || !session) return;
  const payload = { ...session, exportedAt: new Date().toISOString(), frameCount: frames.length, frames };
  const url = URL.createObjectURL(new Blob([JSON.stringify(payload)], { type: 'application/json' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = `kinesthetic-pose-${session.sessionID}.json`;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

ui.start.addEventListener('click', startPose);
ui.camera.addEventListener('change', () => {
  if (active) { stopCapture('Switching camera…'); startPose(); }
});
ui.file.addEventListener('change', event => openRecordedVideo(event.target.files[0]));
ui.stop.addEventListener('click', () => stopCapture());
ui.export.addEventListener('click', exportFrames);
ui.unity.addEventListener('click', connectUnity);
video.addEventListener('ended', () => { if (active) stopCapture('Recorded video complete. Export JSON to keep the session.'); });
window.addEventListener('pagehide', () => {
  active = false; releaseSource(); unityWanted = false;
  clearTimeout(unityRetry); unitySocket?.close();
});
showFraming();
openUnityBridge();
if (new URLSearchParams(location.search).has('recorded')) {
  status('Open a recorded video to check its pose tracking.');
  refreshCameras().catch(() => {});
} else if (navigator.mediaDevices?.enumerateDevices) {
  refreshCameras().catch(() => {}).finally(startPose);
} else startPose();
