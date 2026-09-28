#!/usr/bin/env node
/*
 * agent-bus.js — sandbox side of the Bazino Bridge.
 *
 * Writes CDP commands to the `cdp-bus` branch and reads results back.
 * The desktop app on Windows polls that branch, runs the command against
 * Chrome, and pushes the reply.
 *
 * Transport is git push + gh api, because those are the only routes out of
 * this sandbox (api.github.com is reachable; raw.githubusercontent.com is not).
 *
 * Usage:
 *   node agent-bus.js init                        create the orphan branch
 *   node agent-bus.js send '{"method":"..."}'     send one raw CDP command
 *   node agent-bus.js eval "location.href"        shorthand for Runtime.evaluate
 *   node agent-bus.js targets                     list open tabs
 *   node agent-bus.js status                      is the desktop app alive?
 */

const { execSync } = require('child_process');
const fs   = require('fs');
const path = require('path');
const os   = require('os');

const OWNER  = 'paymanshafayan';
const REPO   = 'bazino-advertising-campaign';
const BRANCH = 'cdp-bus';
const WORK   = path.join(os.tmpdir(), 'cdp-bus-work');

let _id = Date.now() % 100000;
const nextId = () => ++_id;

const sh = (cmd, opts = {}) =>
  execSync(cmd, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], ...opts }).trim();

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/* ---------------------------------------------------------------- worktree */

function ensureWorktree() {
  if (fs.existsSync(path.join(WORK, '.git'))) return;
  fs.rmSync(WORK, { recursive: true, force: true });
  fs.mkdirSync(WORK, { recursive: true });

  sh('git init -q', { cwd: WORK });
  sh(`git remote add origin https://github.com/${OWNER}/${REPO}.git`, { cwd: WORK });
  sh('git config user.email "agent@bazino.local"', { cwd: WORK });
  sh('git config user.name  "Bazino Agent"',       { cwd: WORK });

  try {
    sh(`git fetch -q --depth 1 origin ${BRANCH}`, { cwd: WORK });
    sh(`git checkout -q -B ${BRANCH} FETCH_HEAD`, { cwd: WORK });
  } catch {
    sh(`git checkout -q --orphan ${BRANCH}`, { cwd: WORK });
  }
  fs.mkdirSync(path.join(WORK, 'cmd'), { recursive: true });
  fs.mkdirSync(path.join(WORK, 'res'), { recursive: true });
}

function nextSeq() {
  ensureWorktree();
  try { sh(`git fetch -q --depth 1 origin ${BRANCH} && git reset -q --hard FETCH_HEAD`, { cwd: WORK }); }
  catch { /* branch not there yet */ }

  const dir = path.join(WORK, 'cmd');
  if (!fs.existsSync(dir)) return 1;
  const nums = fs.readdirSync(dir)
    .map((f) => parseInt(f, 10))
    .filter((n) => !Number.isNaN(n));
  return nums.length ? Math.max(...nums) + 1 : 1;
}

/* ------------------------------------------------------------------ commands */

function init() {
  ensureWorktree();
  fs.mkdirSync(path.join(WORK, 'cmd'), { recursive: true });
  fs.mkdirSync(path.join(WORK, 'res'), { recursive: true });
  fs.writeFileSync(path.join(WORK, 'cmd', '.keep'), '');
  fs.writeFileSync(path.join(WORK, 'res', '.keep'), '');
  fs.writeFileSync(
    path.join(WORK, 'README.md'),
    '# CDP bus\n\nMachine-generated transport branch for Bazino Bridge.\n' +
    'Safe to delete and recreate: `node agent-bus.js init`.\n'
  );
  sh('git add -A', { cwd: WORK });
  try { sh('git commit -qm "init cdp bus"', { cwd: WORK }); } catch { /* nothing new */ }
  sh(`git push -q -u origin ${BRANCH}`, { cwd: WORK });
  console.log(`Bus branch '${BRANCH}' ready.`);
}

async function send(payload, { timeoutMs = 90000 } = {}) {
  const seq = nextSeq();
  const msg = typeof payload === 'string' ? payload : JSON.stringify(payload);

  fs.writeFileSync(path.join(WORK, 'cmd', `${seq}.json`), msg);
  sh('git add -A', { cwd: WORK });
  sh(`git commit -qm "cmd ${seq}"`, { cwd: WORK });
  sh(`git push -q origin ${BRANCH}`, { cwd: WORK });

  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    await sleep(1200);
    try {
      const raw = sh(
        `gh api repos/${OWNER}/${REPO}/contents/res/${seq}.json?ref=${BRANCH} --jq .content`
      );
      return JSON.parse(Buffer.from(raw, 'base64').toString('utf8'));
    } catch {
      /* result not pushed yet */
    }
  }
  throw new Error(`Timed out waiting for result of command ${seq}`);
}

async function status() {
  try {
    const raw = sh(
      `gh api repos/${OWNER}/${REPO}/contents/status.json?ref=${BRANCH} --jq .content`
    );
    const s = JSON.parse(Buffer.from(raw, 'base64').toString('utf8'));
    const age = Math.round((Date.now() - new Date(s.ts).getTime()) / 1000);
    console.log(`Desktop app: ${age < 30 ? 'ALIVE' : 'STALE'}  (heartbeat ${age}s ago)`);
    console.log(JSON.stringify(s, null, 2));
  } catch {
    console.log('No heartbeat yet — is the desktop app connected?');
  }
}

/* ---------------------------------------------------------------------- cli */

(async () => {
  const [cmd, arg] = process.argv.slice(2);
  try {
    switch (cmd) {
      case 'init':
        init();
        break;

      case 'send':
        console.log(JSON.stringify(await send(arg), null, 2));
        break;

      case 'eval': {
        // Runtime.* needs a page session; the browser-level endpoint has no
        // JS context of its own. Attach to the first page target, then run.
        const t = await send({ id: nextId(), method: 'Target.getTargets' });
        const page = (t.result?.targetInfos || []).find((x) => x.type === 'page');
        if (!page) throw new Error('No page target open in Chrome');

        const att = await send({
          id: nextId(),
          method: 'Target.attachToTarget',
          params: { targetId: page.targetId, flatten: true },
        });
        const sessionId = att.result?.sessionId;
        if (!sessionId) throw new Error('Could not attach to the tab');

        const r = await send({
          id: nextId(),
          sessionId,
          method: 'Runtime.evaluate',
          params: { expression: arg, returnByValue: true, awaitPromise: true },
        });
        console.log(JSON.stringify(r, null, 2));
        break;
      }

      case 'nav': {
        const t = await send({ id: nextId(), method: 'Target.getTargets' });
        const page = (t.result?.targetInfos || []).find((x) => x.type === 'page');
        if (!page) throw new Error('No page target open in Chrome');

        const att = await send({
          id: nextId(),
          method: 'Target.attachToTarget',
          params: { targetId: page.targetId, flatten: true },
        });
        const sessionId = att.result?.sessionId;

        const r = await send({
          id: nextId(),
          sessionId,
          method: 'Page.navigate',
          params: { url: arg },
        });
        console.log(JSON.stringify(r, null, 2));
        break;
      }

      case 'targets':
        console.log(JSON.stringify(await send({
          id: nextId(),
          method: 'Target.getTargets',
        }), null, 2));
        break;

      case 'status':
        await status();
        break;

      default:
        console.log('usage: agent-bus.js init | send <json> | eval <js> | nav <url> | targets | status');
        process.exit(1);
    }
  } catch (e) {
    console.error('ERROR:', e.message);
    process.exit(1);
  }
})();
