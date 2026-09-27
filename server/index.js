// RehabMii session API
// Receives completed session data from Unity and serves it to the physician portal.
//
// Run:  node server/index.js
//
// Unity endpoint (POST):  http://localhost:3001/api/session
// React fetch (GET):      http://localhost:3001/api/session/:patientId

import http from 'http'

const PORT = 3001

// In-memory store: patientId → session[]
// Replace with a DB write when persisting across restarts matters.
const store = new Map()

// ── Helpers ───────────────────────────────────────────────────────────────────

function cors(res) {
  res.setHeader('Access-Control-Allow-Origin', '*')
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type')
}

function json(res, status, data) {
  cors(res)
  res.writeHead(status, { 'Content-Type': 'application/json' })
  res.end(JSON.stringify(data))
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let raw = ''
    req.on('data', chunk => { raw += chunk })
    req.on('end', () => {
      try { resolve(JSON.parse(raw)) }
      catch { reject(new Error('Body is not valid JSON')) }
    })
    req.on('error', reject)
  })
}

// ── Routes ────────────────────────────────────────────────────────────────────

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, `http://localhost:${PORT}`)

  // CORS preflight
  if (req.method === 'OPTIONS') {
    cors(res)
    res.writeHead(204)
    res.end()
    return
  }

  // ── POST /api/session ──────────────────────────────────────────────────────
  // Unity calls this at the end of (or during) a session.
  // See src/data/unityProtocol.ts for the expected JSON shape.
  if (req.method === 'POST' && url.pathname === '/api/session') {
    let body
    try {
      body = await readBody(req)
    } catch (e) {
      return json(res, 400, { error: e.message })
    }

    const { patientId } = body
    if (!patientId || typeof patientId !== 'string') {
      return json(res, 400, { error: '`patientId` is required (string)' })
    }

    const entry = { ...body, receivedAt: new Date().toISOString() }
    const list = store.get(patientId) ?? []
    list.push(entry)
    store.set(patientId, list)

    console.log(`[POST /api/session] patientId=${patientId}  total=${list.length}`)
    return json(res, 201, { ok: true, sessionCount: list.length })
  }

  // ── GET /api/session/:patientId ────────────────────────────────────────────
  // Portal polls this to check whether Unity has submitted new data.
  const patientMatch = url.pathname.match(/^\/api\/session\/([^/]+)$/)
  if (req.method === 'GET' && patientMatch) {
    const patientId = decodeURIComponent(patientMatch[1])
    const list = store.get(patientId) ?? []
    return json(res, 200, {
      patientId,
      sessionCount: list.length,
      latest: list[list.length - 1] ?? null,
    })
  }

  // ── GET /api/sessions ──────────────────────────────────────────────────────
  // Debug endpoint - dumps everything in the store.
  if (req.method === 'GET' && url.pathname === '/api/sessions') {
    const all = {}
    for (const [id, list] of store) all[id] = list
    return json(res, 200, all)
  }

  json(res, 404, { error: 'Not found' })
})

server.listen(PORT, () => {
  console.log(`\nRehabMii session API  →  http://localhost:${PORT}`)
  console.log('  POST /api/session          submit session from Unity')
  console.log('  GET  /api/session/:id      latest session for a patient')
  console.log('  GET  /api/sessions         all sessions (debug)\n')
})
