#!/usr/bin/env node
'use strict';
/*
 * Agent-side tool for the Bazino Marketing Studio command mailbox («صندوق فرمان»).
 * Zero dependencies; Node ≥ 18. Wire format is byte-compatible with Core/Mailbox/CryptoBox.cs:
 *   ECDH P-256 → HKDF-SHA256(salt16, info "bazino-mailbox-v1") → AES-256-GCM (nonce12, tag16, data = cipher||tag)
 *   ECDSA P-256 / SHA-256 / IEEE-P1363 over SignedBytes() = "\n"-joined canonical header (see signedText()).
 *
 * Commands (all print JSON unless noted):
 *   init [--label L]         create/load the agent identity (private keys stay in $BAZINO_AGENT_HOME, default ~/.bazino-agent)
 *   publish [--label L]      write <mailbox>/agent-identity.json into the repo working tree (commit + push it yourself)
 *   send --cmd C [--args JSON] [--note N] [--risk read|write|os] [--ttl SEC]
 *                            seal a command to the app and write <mailbox>/inbox/NNNNNN-id.json (commit + push it yourself)
 *   wait [--id ID] [--timeout SEC] [--ref origin/BRANCH]
 *                            git fetch + read sealed replies straight from the remote branch (no checkout needed)
 *   read                     open replies present in the working tree's outbox (after git pull)
 *   ack                      delete replies from the working tree's outbox (commit + push it yourself)
 *   state [--ref ...]        verify and print the app heartbeat (state.json)
 *   seal --to peer.json --session S --seq N [--kind K] [--risk R] [--ttl SEC] [--id ID]   (stdin → envelope; used by tests)
 *   open --from peer.json [--as app|agent]                                                  (stdin envelope → plaintext; used by tests)
 *   fingerprint FILE         print the fingerprint of an identity file
 */
const crypto = require('crypto');
const fs = require('fs');
const https = require('https');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');

const INFO = 'bazino-mailbox-v1';
const AGENT_HOME = process.env.BAZINO_AGENT_HOME || path.join(os.homedir(), '.bazino-agent');
const MAILBOX_REL = process.env.BAZINO_MAILBOX_PATH || 'marketing-app-mailbox';

// ------------------------------------------------------------------ helpers
function parseArgs(argv) {
  const out = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) out[key] = true;
      else { out[key] = next; i++; }
    } else out._.push(a);
  }
  return out;
}
function die(msg, code = 2) { process.stderr.write(String(msg) + '\n'); process.exit(code); }
function b64(buf) { return Buffer.from(buf).toString('base64'); }
function readStdin() { return fs.readFileSync(0, 'utf8'); }
function repoRoot() {
  try { return execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim(); }
  catch { return process.cwd(); }
}
function mailboxDir() { return path.join(repoRoot(), MAILBOX_REL); }

/** C# DateTimeOffset "O" in UTC: yyyy-MM-ddTHH:mm:ss.fffffff+00:00 (exactly 7 fraction digits). */
function canonTime(input) {
  if (input instanceof Date) {
    const iso = input.toISOString(); // 2026-09-27T10:15:30.123Z
    return iso.slice(0, 19) + '.' + iso.slice(20, 23) + '0000' + '+00:00';
  }
  const m = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/.exec(String(input).trim());
  if (!m) throw new Error('unparseable timestamp: ' + input);
  let base = m[1];
  const frac = (m[2] || '').padEnd(7, '0');
  const off = m[3];
  if (off !== 'Z' && off !== '+00:00') {
    // Shift whole minutes to UTC; sub-second digits are untouched.
    const d = new Date(base + 'Z');
    const sign = off[0] === '-' ? -1 : 1;
    const minutes = sign * (parseInt(off.slice(1, 3), 10) * 60 + parseInt(off.slice(4, 6), 10));
    d.setUTCMinutes(d.getUTCMinutes() - minutes);
    base = d.toISOString().slice(0, 19);
  }
  return base + '.' + frac + '+00:00';
}
function toDate(canon) { return new Date(canon.slice(0, 23) + 'Z'); }

// ------------------------------------------------------------------ identity
function fingerprintOf(exchange, signing) {
  const hex = crypto.createHash('sha256').update(exchange + '|' + signing, 'ascii').digest('hex').slice(0, 16);
  return [0, 4, 8, 12].map(i => hex.slice(i, i + 4)).join(':');
}
function createIdentity() {
  const x = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const s = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
  return {
    x: b64(x.privateKey.export({ type: 'pkcs8', format: 'der' })),
    s: b64(s.privateKey.export({ type: 'pkcs8', format: 'der' }))
  };
}
function loadIdentity(json) {
  const p = typeof json === 'string' ? JSON.parse(json) : json;
  const exchange = crypto.createPrivateKey({ key: Buffer.from(p.x, 'base64'), format: 'der', type: 'pkcs8' });
  const signing = crypto.createPrivateKey({ key: Buffer.from(p.s, 'base64'), format: 'der', type: 'pkcs8' });
  const exchangePub = b64(crypto.createPublicKey(exchange).export({ type: 'spki', format: 'der' }));
  const signingPub = b64(crypto.createPublicKey(signing).export({ type: 'spki', format: 'der' }));
  return { exchange, signing, exchangePub, signingPub, fingerprint: fingerprintOf(exchangePub, signingPub) };
}
function peerFromFile(file) {
  const f = JSON.parse(fs.readFileSync(file, 'utf8'));
  return peerFromIdentityFile(f);
}
function peerFromIdentityFile(f) {
  if (f.v !== 1 || !f.exchange || !f.signing) throw new Error('identity file malformed');
  const fp = fingerprintOf(f.exchange, f.signing);
  if (f.fingerprint && f.fingerprint !== fp) throw new Error('identity fingerprint mismatch');
  return {
    exchangePub: f.exchange, signingPub: f.signing, fingerprint: fp, label: f.label || '',
    exchangeKey: crypto.createPublicKey({ key: Buffer.from(f.exchange, 'base64'), format: 'der', type: 'spki' }),
    signingKey: crypto.createPublicKey({ key: Buffer.from(f.signing, 'base64'), format: 'der', type: 'spki' })
  };
}
function identityFile(id, label) {
  return { v: 1, exchange: id.exchangePub, signing: id.signingPub, fingerprint: id.fingerprint, label, updatedAt: canonTime(new Date()) };
}
function agentPrivatePath() { return path.join(AGENT_HOME, 'agent-identity-private.json'); }
function sessionPath() { return path.join(AGENT_HOME, 'session.json'); }
function ensureAgent() {
  fs.mkdirSync(AGENT_HOME, { recursive: true, mode: 0o700 });
  const p = agentPrivatePath();
  if (!fs.existsSync(p)) fs.writeFileSync(p, JSON.stringify(createIdentity()), { mode: 0o600 });
  return loadIdentity(fs.readFileSync(p, 'utf8'));
}
function loadSession() {
  try { return JSON.parse(fs.readFileSync(sessionPath(), 'utf8')); } catch { return null; }
}
function newSession() {
  const s = { session: Date.now().toString(36) + crypto.randomBytes(3).toString('hex'), seq: 0, startedAt: canonTime(new Date()) };
  fs.writeFileSync(sessionPath(), JSON.stringify(s));
  return s;
}
function saveSession(s) { fs.writeFileSync(sessionPath(), JSON.stringify(s)); }

// ------------------------------------------------------------------ envelope crypto
function signedText(env, data) {
  return [INFO, String(env.v), env.id, env.session, String(env.seq), env.kind, env.risk,
    canonTime(env.issuedAt), canonTime(env.expiresAt), env.from, env.enc.salt, env.enc.nonce, data].join('\n');
}
function deriveKey(myPrivate, peerPublic, salt) {
  const shared = crypto.diffieHellman({ privateKey: myPrivate, publicKey: peerPublic });
  return Buffer.from(crypto.hkdfSync('sha256', shared, salt, Buffer.from(INFO, 'ascii'), 32));
}
function seal(me, peer, plaintext, { kind = 'command', risk = 'read', session, seq, ttlSec = 600, id, now } = {}) {
  const issued = now || new Date();
  const env = {
    v: 1,
    id: id || crypto.randomBytes(16).toString('hex'),
    session, seq, kind, risk,
    issuedAt: canonTime(issued),
    expiresAt: canonTime(new Date(issued.getTime() + ttlSec * 1000)),
    from: me.fingerprint,
    enc: { salt: b64(crypto.randomBytes(16)), nonce: b64(crypto.randomBytes(12)), data: '' },
    sig: ''
  };
  const salt = Buffer.from(env.enc.salt, 'base64');
  const nonce = Buffer.from(env.enc.nonce, 'base64');
  const key = deriveKey(me.exchange, peer.exchangeKey, salt);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, nonce, { authTagLength: 16 });
  cipher.setAAD(Buffer.from(signedText(env, ''), 'utf8'));
  const body = Buffer.concat([cipher.update(Buffer.from(plaintext, 'utf8')), cipher.final()]);
  env.enc.data = b64(Buffer.concat([body, cipher.getAuthTag()]));
  env.sig = b64(crypto.sign('sha256', Buffer.from(signedText(env, env.enc.data), 'utf8'), { key: me.signing, dsaEncoding: 'ieee-p1363' }));
  return env;
}
function open(me, peer, env, { now } = {}) {
  if (env.v !== 1) throw new Error('unsupported envelope version');
  if (env.from !== peer.fingerprint) throw new Error(`envelope from ${env.from}, expected ${peer.fingerprint}`);
  const ok = crypto.verify('sha256', Buffer.from(signedText(env, env.enc.data), 'utf8'), { key: peer.signingKey, dsaEncoding: 'ieee-p1363' }, Buffer.from(env.sig, 'base64'));
  if (!ok) throw new Error('signature verification failed');
  const t = now || new Date();
  if (toDate(canonTime(env.expiresAt)) < t) throw new Error('message expired');
  const data = Buffer.from(env.enc.data, 'base64');
  const key = deriveKey(me.exchange, peer.exchangeKey, Buffer.from(env.enc.salt, 'base64'));
  const decipher = crypto.createDecipheriv('aes-256-gcm', key, Buffer.from(env.enc.nonce, 'base64'), { authTagLength: 16 });
  decipher.setAAD(Buffer.from(signedText(env, ''), 'utf8'));
  decipher.setAuthTag(data.subarray(data.length - 16));
  return Buffer.concat([decipher.update(data.subarray(0, data.length - 16)), decipher.final()]).toString('utf8');
}
function verifyState(state, appPeer) {
  const text = ['bazino-state-v1', String(state.v), state.listening ? 'true' : 'false', state.status || '', state.session || '', state.agent || '',
    state.approvedUntil ? canonTime(state.approvedUntil) : '', canonTime(state.updatedAt), state.app || '', state.from || ''].join('\n');
  if (state.from !== appPeer.fingerprint) return false;
  return crypto.verify('sha256', Buffer.from(text, 'utf8'), { key: appPeer.signingKey, dsaEncoding: 'ieee-p1363' }, Buffer.from(state.sig || '', 'base64'));
}

// ------------------------------------------------------------------ git helpers (read remote state without touching the checkout)
function gitRef(args) {
  if (args.ref) return String(args.ref);
  let branch = 'HEAD';
  try { branch = execFileSync('git', ['rev-parse', '--abbrev-ref', 'HEAD'], { encoding: 'utf8' }).trim(); } catch { }
  try { execFileSync('git', ['fetch', '--quiet', 'origin', branch], { stdio: ['ignore', 'ignore', 'inherit'] }); } catch (e) { process.stderr.write('git fetch failed: ' + e.message + '\n'); }
  return 'FETCH_HEAD';
}
function gitShow(ref, rel) {
  try { return execFileSync('git', ['show', `${ref}:${MAILBOX_REL}/${rel}`], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }); }
  catch { return null; }
}
function gitList(ref, dir) {
  try {
    return execFileSync('git', ['ls-tree', '--name-only', `${ref}:${MAILBOX_REL}/${dir}`], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] })
      .split('\n').map(s => s.trim()).filter(Boolean).sort();
  } catch { return []; }
}
function appPeer(ref) {
  const text = ref ? gitShow(ref, 'app-identity.json') : (fs.existsSync(path.join(mailboxDir(), 'app-identity.json')) ? fs.readFileSync(path.join(mailboxDir(), 'app-identity.json'), 'utf8') : null);
  if (!text) throw new Error('app-identity.json not found — the app has not published its identity yet');
  return peerFromIdentityFile(JSON.parse(text));
}

// ------------------------------------------------------------------ direct GitHub REST API helpers (persistent HTTPS keep-alive + ETag)
const extraCa = fs.existsSync('/etc/ssl/certs/ca-certificates.crt') ? fs.readFileSync('/etc/ssl/certs/ca-certificates.crt') : undefined;
const keepAliveAgent = new https.Agent({ keepAlive: true, maxSockets: 4, ...(extraCa ? { ca: extraCa } : {}) });
let cachedGhToken = null;
function getGhToken() {
  if (cachedGhToken) return cachedGhToken;
  if (process.env.GITHUB_TOKEN) return (cachedGhToken = process.env.GITHUB_TOKEN.trim());
  try {
    return (cachedGhToken = execFileSync('gh', ['auth', 'token'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim());
  } catch {
    return '';
  }
}
function getRepoAndBranch(args) {
  let repo = args.repo || process.env.BAZINO_GITHUB_REPO || '';
  if (!repo) {
    try {
      const url = execFileSync('git', ['remote', 'get-url', 'origin'], { encoding: 'utf8' }).trim();
      const m = /github\.com[:/]([^/]+\/[^/.]+)(?:\.git)?$/i.exec(url);
      if (m) repo = m[1];
    } catch { }
  }
  let branch = args.branch || process.env.BAZINO_GITHUB_BRANCH || '';
  if (!branch) {
    try { branch = execFileSync('git', ['rev-parse', '--abbrev-ref', 'HEAD'], { encoding: 'utf8' }).trim(); } catch { branch = 'main'; }
  }
  return { repo: repo || 'paymanshafayan/bazino-gamenet-portal', branch };
}
function ghRequest(method, apiPath, { body, etag } = {}) {
  const token = getGhToken();
  if (!token) return Promise.reject(new Error('no GitHub token available for direct API call'));
  const payload = body ? JSON.stringify(body) : null;
  const headers = {
    'Authorization': `Bearer ${token}`,
    'Accept': 'application/vnd.github+json',
    'User-Agent': 'BazinoAgentMailbox/0.6',
    'X-GitHub-Api-Version': '2022-11-28'
  };
  if (etag) headers['If-None-Match'] = etag;
  if (payload) {
    headers['Content-Type'] = 'application/json; charset=utf-8';
    headers['Content-Length'] = Buffer.byteLength(payload);
  }
  return new Promise((resolve, reject) => {
    const req = https.request({
      hostname: 'api.github.com',
      port: 443,
      path: apiPath,
      method,
      headers,
      agent: keepAliveAgent,
      timeout: 20000
    }, res => {
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        let json = null;
        try { json = text ? JSON.parse(text) : null; } catch { }
        resolve({ status: res.statusCode || 0, headers: res.headers, text, json });
      });
    });
    req.on('timeout', () => { req.destroy(new Error('timeout')); });
    req.on('error', reject);
    if (payload) req.write(payload);
    req.end();
  });
}
function sleepAsync(ms) { return new Promise(r => setTimeout(r, ms)); }

// ------------------------------------------------------------------ commands
const cmds = {
  init(args) {
    const me = ensureAgent();
    const label = args.label || ('Arena agent');
    console.log(JSON.stringify({ fingerprint: me.fingerprint, privateKeyFile: agentPrivatePath(), identity: identityFile(me, label) }, null, 2));
  },
  publish(args) {
    const me = ensureAgent();
    const label = args.label || 'Arena agent';
    const dir = mailboxDir();
    fs.mkdirSync(dir, { recursive: true });
    const file = path.join(dir, 'agent-identity.json');
    fs.writeFileSync(file, JSON.stringify(identityFile(me, label), null, 2) + '\n');
    const s = newSession();
    console.log(JSON.stringify({ written: file, fingerprint: me.fingerprint, session: s.session }, null, 2));
  },
  send(args) {
    if (!args.cmd) die('--cmd is required');
    const me = ensureAgent();
    const ref = args.local ? null : gitRef(args);
    const peer = appPeer(ref);
    let session = loadSession();
    if (!session) session = newSession();
    session.seq += 1;
    let parsedArgs = {};
    if (args.args) { try { parsedArgs = JSON.parse(args.args); } catch (e) { die('--args is not valid JSON: ' + e.message); } }
    const request = { cmd: String(args.cmd), args: parsedArgs, note: args.note ? String(args.note) : '' };
    const env = seal(me, peer, JSON.stringify(request), {
      kind: 'command', risk: args.risk || 'read', session: session.session, seq: session.seq,
      ttlSec: args.ttl ? parseInt(args.ttl, 10) : 600
    });
    const dir = path.join(mailboxDir(), 'inbox');
    fs.mkdirSync(dir, { recursive: true });
    const name = `${String(session.seq).padStart(6, '0')}-${env.id}.json`;
    fs.writeFileSync(path.join(dir, name), JSON.stringify(env) + '\n');
    saveSession(session);
    console.log(JSON.stringify({ id: env.id, seq: session.seq, session: session.session, file: path.join(MAILBOX_REL, 'inbox', name), to: peer.fingerprint, expiresAt: env.expiresAt }, null, 2));
  },
  wait(args) {
    const me = ensureAgent();
    const timeoutMs = (args.timeout ? parseInt(args.timeout, 10) : 90) * 1000;
    const wantId = args.id ? String(args.id) : null;
    const started = Date.now();
    const seen = new Set();
    const sleep = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
    for (;;) {
      const ref = gitRef(args);
      let peer = null;
      try { peer = appPeer(ref); } catch (e) { process.stderr.write(e.message + '\n'); }
      const found = [];
      if (peer) {
        for (const name of gitList(ref, 'outbox')) {
          if (seen.has(name)) continue;
          const text = gitShow(ref, 'outbox/' + name);
          if (!text) continue;
          let env;
          try { env = JSON.parse(text); } catch { continue; }
          if (wantId && !name.includes(wantId)) continue;
          try {
            const plain = open(me, peer, env);
            found.push({ file: name, seq: env.seq, session: env.session, reply: JSON.parse(plain) });
          } catch (e) {
            found.push({ file: name, error: e.message });
          }
          seen.add(name);
        }
      }
      if (found.length) {
        console.log(JSON.stringify(found, null, 2));
        if (wantId || !args.all) return;
      }
      if (Date.now() - started > timeoutMs) { if (!found.length) die('timeout: no reply within ' + timeoutMs / 1000 + 's', 3); return; }
      sleep(args.every ? parseInt(args.every, 10) * 1000 : 1000);
    }
  },
  async call(args) {
    if (!args.cmd) die('--cmd is required');
    const me = ensureAgent();
    const { repo, branch } = getRepoAndBranch(args);
    const encRepo = repo.split('/').map(encodeURIComponent).join('/');
    const encBranch = encodeURIComponent(branch);

    // 1. Load peer identity from local file first (instant), fallback to API
    let peer = null;
    const localPeerPath = path.join(mailboxDir(), 'app-identity.json');
    if (fs.existsSync(localPeerPath)) {
      peer = peerFromIdentityFile(JSON.parse(fs.readFileSync(localPeerPath, 'utf8')));
    } else {
      const idRes = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/app-identity.json?ref=${encBranch}`);
      if (idRes.status !== 200 || !idRes.json || !idRes.json.content) die('app-identity.json not found on branch ' + branch);
      const text = Buffer.from(idRes.json.content.replace(/\s+/g, ''), 'base64').toString('utf8');
      fs.mkdirSync(mailboxDir(), { recursive: true });
      fs.writeFileSync(localPeerPath, text);
      peer = peerFromIdentityFile(JSON.parse(text));
    }

    // 2. Seal command envelope in memory
    let session = loadSession();
    if (!session) session = newSession();
    session.seq += 1;
    let parsedArgs = {};
    if (args.args) { try { parsedArgs = JSON.parse(args.args); } catch (e) { die('--args is not valid JSON: ' + e.message); } }
    const request = { cmd: String(args.cmd), args: parsedArgs, note: args.note ? String(args.note) : '' };
    const env = seal(me, peer, JSON.stringify(request), {
      kind: 'command', risk: args.risk || 'read', session: session.session, seq: session.seq,
      ttlSec: args.ttl ? parseInt(args.ttl, 10) : 600
    });
    saveSession(session);

    const inName = `${String(session.seq).padStart(6, '0')}-${env.id}.json`;
    const envJson = JSON.stringify(env) + '\n';

    // Detect if app is >= 0.6.0 (supports single-slot fast-inbox.json / fast-outbox.json with 0 DELETEs)
    let useFastSlot = Boolean(args.fast);
    if (!useFastSlot && !args.legacy) {
      try {
        const localStatePath = path.join(mailboxDir(), 'state.json');
        let st = fs.existsSync(localStatePath) ? JSON.parse(fs.readFileSync(localStatePath, 'utf8')) : null;
        if (!st || !st.app || st.app.localeCompare('0.6.0', undefined, { numeric: true }) < 0) {
          const stRes = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/state.json?ref=${encBranch}`);
          if (stRes.status === 200 && stRes.json && stRes.json.content) {
            const rawSt = Buffer.from(stRes.json.content.replace(/\s+/g, ''), 'base64').toString('utf8');
            fs.writeFileSync(localStatePath, rawSt);
            st = JSON.parse(rawSt);
          }
        }
        if (st && st.app && st.app.localeCompare('0.6.0', undefined, { numeric: true }) >= 0) useFastSlot = true;
      } catch { }
    }

    const started = Date.now();
    const timeoutMs = (args.timeout ? parseInt(args.timeout, 10) : 90) * 1000;
    const pollMs = args.pollMs ? parseInt(args.pollMs, 10) : 350;

    if (useFastSlot) {
      const shaCacheFile = path.join(AGENT_HOME, 'fast-inbox-sha.txt');
      let fastSha = fs.existsSync(shaCacheFile) ? fs.readFileSync(shaCacheFile, 'utf8').trim() : '';
      if (!fastSha) {
        const cur = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/fast-inbox.json?ref=${encBranch}`);
        if (cur.status === 200 && cur.json && cur.json.sha) fastSha = cur.json.sha;
      }
      let putFast = await ghRequest('PUT', `/repos/${encRepo}/contents/${MAILBOX_REL}/fast-inbox.json`, {
        body: {
          message: `mailbox: fast ${args.cmd} (${env.id})`,
          content: Buffer.from(envJson, 'utf8').toString('base64'),
          branch,
          ...(fastSha ? { sha: fastSha } : {})
        }
      });
      if ((putFast.status === 409 || putFast.status === 422) ) {
        const cur = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/fast-inbox.json?ref=${encBranch}`);
        fastSha = (cur.status === 200 && cur.json && cur.json.sha) ? cur.json.sha : '';
        putFast = await ghRequest('PUT', `/repos/${encRepo}/contents/${MAILBOX_REL}/fast-inbox.json`, {
          body: {
            message: `mailbox: fast ${args.cmd} (${env.id})`,
            content: Buffer.from(envJson, 'utf8').toString('base64'),
            branch,
            ...(fastSha ? { sha: fastSha } : {})
          }
        });
      }
      if (putFast.status >= 200 && putFast.status < 300 && putFast.json && putFast.json.content && putFast.json.content.sha) {
        fs.writeFileSync(shaCacheFile, putFast.json.content.sha);
      } else {
        die(`failed to write fast-inbox.json (HTTP ${putFast.status}): ${putFast.text}`);
      }

      let etag = null;
      while (Date.now() - started <= timeoutMs) {
        const outRes = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/fast-outbox.json?ref=${encBranch}`, { etag });
        if (outRes.status === 200 && outRes.json && outRes.json.content) {
          etag = outRes.headers.etag || null;
          try {
            const rawReply = Buffer.from(outRes.json.content.replace(/\s+/g, ''), 'base64').toString('utf8');
            const replyEnv = JSON.parse(rawReply);
            if (replyEnv.id === env.id) {
              const plain = open(me, peer, replyEnv);
              const parsedReply = JSON.parse(plain);
              if (parsedReply.inReplyTo === env.id) {
                console.log(JSON.stringify([{
                  file: 'fast-outbox.json',
                  seq: replyEnv.seq,
                  session: replyEnv.session,
                  roundTripMs: Date.now() - started,
                  reply: parsedReply
                }], null, 2));
                return;
              }
            }
          } catch { }
        }
        await sleepAsync(pollMs);
      }
      die('timeout: no fast reply within ' + timeoutMs / 1000 + 's', 3);
    }

    // 3. Legacy multi-file path (used for app <= 0.5.0)
    const putRes = await ghRequest('PUT', `/repos/${encRepo}/contents/${MAILBOX_REL}/inbox/${encodeURIComponent(inName)}`, {
      body: {
        message: `mailbox: ${args.cmd} (${env.id})`,
        content: Buffer.from(envJson, 'utf8').toString('base64'),
        branch
      }
    });
    if (putRes.status < 200 || putRes.status >= 300) {
      die(`failed to send command to inbox (HTTP ${putRes.status}): ${putRes.text}`);
    }

    let etag = null;
    while (Date.now() - started <= timeoutMs) {
      const listRes = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/outbox?ref=${encBranch}`, { etag });
      if (listRes.status === 200 && Array.isArray(listRes.json)) {
        etag = listRes.headers.etag || null;
        const match = listRes.json.find(item => item && item.type === 'file' && typeof item.name === 'string' && item.name.includes(env.id));
        if (match) {
          const fileRes = await ghRequest('GET', `/repos/${encRepo}/contents/${MAILBOX_REL}/outbox/${encodeURIComponent(match.name)}?ref=${encBranch}`);
          if (fileRes.status === 200 && fileRes.json && fileRes.json.content) {
            const rawReply = Buffer.from(fileRes.json.content.replace(/\s+/g, ''), 'base64').toString('utf8');
            const replyEnv = JSON.parse(rawReply);
            const plain = open(me, peer, replyEnv);
            const out = [{
              file: match.name,
              seq: replyEnv.seq,
              session: replyEnv.session,
              roundTripMs: Date.now() - started,
              reply: JSON.parse(plain)
            }];
            console.log(JSON.stringify(out, null, 2));
            // Delete the reply from outbox immediately over the same keep-alive socket
            if (!args.noAck) {
              await ghRequest('DELETE', `/repos/${encRepo}/contents/${MAILBOX_REL}/outbox/${encodeURIComponent(match.name)}`, {
                body: {
                  message: `mailbox: ack ${env.id}`,
                  sha: fileRes.json.sha || match.sha,
                  branch
                }
              }).catch(() => {});
            }
            return;
          }
        }
      }
      await sleepAsync(pollMs);
    }
    die('timeout: no reply within ' + timeoutMs / 1000 + 's', 3);
  },
  read(args) {
    const me = ensureAgent();
    const peer = appPeer(null);
    const dir = path.join(mailboxDir(), 'outbox');
    const out = [];
    if (fs.existsSync(dir)) {
      for (const name of fs.readdirSync(dir).sort()) {
        if (!name.endsWith('.json')) continue;
        try {
          const env = JSON.parse(fs.readFileSync(path.join(dir, name), 'utf8'));
          out.push({ file: name, seq: env.seq, reply: JSON.parse(open(me, peer, env)) });
        } catch (e) { out.push({ file: name, error: e.message }); }
      }
    }
    console.log(JSON.stringify(out, null, 2));
  },
  ack() {
    const dir = path.join(mailboxDir(), 'outbox');
    const removed = [];
    if (fs.existsSync(dir)) for (const name of fs.readdirSync(dir)) if (name.endsWith('.json')) { fs.unlinkSync(path.join(dir, name)); removed.push(name); }
    console.log(JSON.stringify({ removed }, null, 2));
  },
  state(args) {
    const ref = args.local ? null : gitRef(args);
    const text = ref ? gitShow(ref, 'state.json') : fs.readFileSync(path.join(mailboxDir(), 'state.json'), 'utf8');
    if (!text) die('state.json not found', 3);
    const state = JSON.parse(text);
    let verified = false, error = null;
    try { verified = verifyState(state, appPeer(ref)); } catch (e) { error = e.message; }
    const ageSec = Math.round((Date.now() - toDate(canonTime(state.updatedAt)).getTime()) / 1000);
    console.log(JSON.stringify({ verified, error, ageSec, state }, null, 2));
  },
  seal(args) {
    if (!args.to || !args.session || args.seq === undefined) die('--to, --session, --seq are required');
    const me = args.as === 'app' ? loadIdentity(fs.readFileSync(args.identity, 'utf8')) : (args.identity ? loadIdentity(fs.readFileSync(args.identity, 'utf8')) : ensureAgent());
    const peer = peerFromFile(args.to);
    const env = seal(me, peer, readStdin(), {
      kind: args.kind || 'command', risk: args.risk || 'read', session: String(args.session), seq: parseInt(args.seq, 10),
      ttlSec: args.ttl ? parseInt(args.ttl, 10) : 600, id: args.id ? String(args.id) : undefined
    });
    process.stdout.write(JSON.stringify(env));
  },
  open(args) {
    if (!args.from) die('--from is required');
    const me = args.identity ? loadIdentity(fs.readFileSync(args.identity, 'utf8')) : ensureAgent();
    const peer = peerFromFile(args.from);
    process.stdout.write(open(me, peer, JSON.parse(readStdin())));
  },
  verifystate(args) {
    // stdin: state.json → prints {verified} (used by tests).
    if (!args.from) die('--from is required');
    const peer = peerFromFile(args.from);
    process.stdout.write(JSON.stringify({ verified: verifyState(JSON.parse(readStdin()), peer) }));
  },
  fingerprint(args) {
    const file = args._[0];
    if (!file) die('file required');
    const f = JSON.parse(fs.readFileSync(file, 'utf8'));
    console.log(f.x && f.s ? loadIdentity(f).fingerprint : fingerprintOf(f.exchange, f.signing));
  },
  identity(args) {
    // Print the public identity file for a private identity json (used by tests).
    const me = args.identity ? loadIdentity(fs.readFileSync(args.identity, 'utf8')) : ensureAgent();
    process.stdout.write(JSON.stringify(identityFile(me, args.label || 'agent')));
  },
  keygen(args) {
    // Write a fresh private identity to --out (used by tests).
    if (!args.out) die('--out required');
    fs.writeFileSync(args.out, JSON.stringify(createIdentity()));
    console.log(loadIdentity(fs.readFileSync(args.out, 'utf8')).fingerprint);
  }
};

const argv = parseArgs(process.argv.slice(2));
const name = argv._.shift();
if (!name || !cmds[name]) die('usage: mailbox.cjs <init|publish|send|wait|call|read|ack|state|seal|open|verifystate|fingerprint|identity|keygen> [options]');
Promise.resolve()
  .then(() => cmds[name](argv))
  .catch(e => die((e && e.stack) || String(e), 1));
