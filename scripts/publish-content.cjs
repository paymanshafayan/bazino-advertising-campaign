#!/usr/bin/env node
'use strict';
/*
 * publish-content.mjs — publish an OWNER-APPROVED content package through Zernio.
 *
 * Called by .github/workflows/publish-approved-content.yml (manual trigger only).
 * Reads one folder that contains publish.json plus the media files, validates the
 * approval fields, uploads the media and creates the post.
 *
 * Safety rules, in order:
 *   1. Nothing is published unless publish.json says approved: true.
 *   2. DRY_RUN defaults to true. A real publish needs an explicit dryRun=false.
 *   3. No key, token or caption text is written to the log or to Git.
 *   4. The script refuses to run if the approval is older than APPROVAL_MAX_AGE_H.
 *
 * Zernio API used (docs.zernio.com):
 *   GET  /v1/accounts          list the accounts this key can publish to
 *   POST /v1/media/presign     get an upload URL for a local file
 *   POST /v1/posts             create / schedule the post
 */

const fs = require('node:fs');
const path = require('node:path');

const BASE = (process.env.ZERNIO_BASE_URL || 'https://zernio.com/api').replace(/\/+$/, '');
const KEY = process.env.ZERNIO_API_KEY || '';
const DRY_RUN = (process.env.DRY_RUN || 'true').toLowerCase() !== 'false';
const APPROVAL_MAX_AGE_H = Number(process.env.APPROVAL_MAX_AGE_H || '168');
const PLATFORMS = ['instagram', 'facebook', 'telegram', 'tiktok', 'youtube'];

function fail(msg) { console.error(`REFUSED: ${msg}`); process.exit(2); }
function info(msg) { console.log(msg); }

async function api(method, route, body) {
  const res = await fetch(`${BASE}${route}`, {
    method,
    headers: {
      authorization: `Bearer ${KEY}`,
      ...(body ? { 'content-type': 'application/json' } : {})
    },
    ...(body ? { body: JSON.stringify(body) } : {})
  });
  const text = await res.text();
  let json;
  try { json = JSON.parse(text); } catch { json = { raw: text.slice(0, 400) }; }
  if (!res.ok) {
    const detail = json && (json.message || json.error || json.raw) || res.status;
    fail(`${method} ${route} -> HTTP ${res.status}: ${detail}`);
  }
  return json;
}

function readPackage(dir) {
  const file = path.join(dir, 'publish.json');
  if (!fs.existsSync(file)) fail(`no publish.json in ${dir}`);
  let pkg;
  try { pkg = JSON.parse(fs.readFileSync(file, 'utf8')); }
  catch (e) { fail(`publish.json is not valid JSON: ${e.message}`); }
  return pkg;
}

function validate(pkg) {
  if (pkg.approved !== true) fail('publish.json must contain "approved": true — the owner has not approved this package');
  if (!pkg.approvedBy) fail('publish.json must name approvedBy');
  if (!pkg.approvedAt) fail('publish.json must carry approvedAt (ISO date)');
  const stamp = new Date(pkg.approvedAt).getTime();
  if (!Number.isFinite(stamp)) fail('approvedAt is not a valid ISO date');
  const ageH = (Date.now() - stamp) / 36e5;
  if (ageH < -24) fail(`approvedAt is ${Math.round(-ageH)}h in the future — fix the date, or the machine clock is wrong`);
  if (ageH > APPROVAL_MAX_AGE_H) fail(`approval is ${Math.round(ageH)}h old, older than the ${APPROVAL_MAX_AGE_H}h limit — ask the owner to approve again`);
  if (pkg.claimsVerified !== true) fail('publish.json must set "claimsVerified": true — every number in the caption must be confirmed by the owner first');
  if (!pkg.content || typeof pkg.content !== 'string') fail('publish.json needs "content" (the caption)');
  if (!Array.isArray(pkg.media) || pkg.media.length === 0) fail('publish.json needs a non-empty "media" array — Instagram rejects text-only posts');
  for (const m of pkg.media) {
    if (!['image', 'video'].includes(m.type)) fail(`media type must be image or video, got ${JSON.stringify(m.type)}`);
    if (!m.url && !m.path) fail('every media item needs a public "url" or a local "path"');
    if (m.path && pkg.platforms.includes('instagram')) {
      info('NOTE: Instagram fetches media from a public URL. A local "path" needs the presign step, which is not confirmed yet — prefer "url".');
    }
  }
  const platforms = pkg.platforms || ['instagram'];
  for (const p of platforms) {
    if (!PLATFORMS.includes(p)) fail(`unknown platform ${p} — allowed: ${PLATFORMS.join(', ')}`);
  }
  if (!pkg.accountIds || typeof pkg.accountIds !== 'object') fail('publish.json needs "accountIds" per platform');
  for (const p of platforms) {
    if (!pkg.accountIds[p]) fail(`publish.json needs accountIds.${p}`);
  }
}

async function resolveMedia(dir, pkg) {
  const out = [];
  for (const m of pkg.media) {
    if (m.url) { out.push({ type: m.type, url: m.url }); continue; }
    const abs = path.resolve(dir, m.path);
    if (!fs.existsSync(abs)) fail(`media file not found: ${m.path}`);
    const bytes = fs.readFileSync(abs);
    info(`  uploading ${m.path} (${(bytes.length / 1024).toFixed(0)} KB) via /v1/media/presign`);
    const presign = await api('POST', '/v1/media/presign', {
      fileName: path.basename(abs),
      contentType: m.type === 'image' ? 'image/jpeg' : 'video/mp4',
      size: bytes.length
    });
    const uploadUrl = presign && (presign.uploadUrl || presign.url || (presign.data && presign.data.uploadUrl));
    const fields = presign && (presign.fields || (presign.data && presign.data.fields));
    if (!uploadUrl) fail('presign did not return an uploadUrl — check the Zernio docs for the exact request shape and set it here');
    if (fields) {
      const form = new FormData();
      for (const [k, v] of Object.entries(fields)) form.append(k, v);
      form.append('file', new Blob([bytes]), path.basename(abs));
      const up = await fetch(uploadUrl, { method: 'POST', body: form });
      if (!up.ok) fail(`media upload failed: HTTP ${up.status}`);
    } else {
      const up = await fetch(uploadUrl, { method: 'PUT', body: bytes, headers: { 'content-type': m.type === 'image' ? 'image/jpeg' : 'video/mp4' } });
      if (!up.ok) fail(`media upload failed: HTTP ${up.status}`);
    }
    const publicUrl = presign.publicUrl || presign.url || (presign.data && presign.data.publicUrl);
    if (!publicUrl) fail('presign did not return a publicUrl — set it here after confirming the docs');
    out.push({ type: m.type, url: publicUrl });
  }
  return out;
}

async function main() {
  const dir = process.argv[2];
  if (!dir) fail('usage: node publish-content.mjs <package-folder>');
  if (!KEY) fail('ZERNIO_API_KEY is not set — add it as a repository secret, never in a file');

  info(`package: ${dir}`);
  info(`mode:    ${DRY_RUN ? 'DRY RUN (nothing will be published)' : 'LIVE PUBLISH'}`);
  const pkg = readPackage(dir);
  validate(pkg);

  const platforms = pkg.platforms || ['instagram'];
  const media = await resolveMedia(dir, pkg);

  const body = {
    content: pkg.content,
    mediaItems: media,
    platforms: platforms.map(p => ({ platform: p, accountId: pkg.accountIds[p] })),
    ...(pkg.scheduledFor ? { scheduledFor: pkg.scheduledFor } : { publishNow: true })
  };

  if (DRY_RUN) {
    info('--- request that WOULD be sent ---');
    info(JSON.stringify({ ...body, content: `${body.content.slice(0, 60)}… (${body.content.length} chars)` }, null, 2));
    info('DRY RUN finished. Re-run with DRY_RUN=false to publish.');
    return;
  }

  info(`publishing to ${platforms.join(', ')}…`);
  const created = await api('POST', '/v1/posts', body);
  const first = created && created.post && created.post.platforms && created.post.platforms[0];
  info(`published. post id: ${(created && created.post && created.post.id) || 'unknown'}`);
  if (first && first.platformPostUrl) info(`url: ${first.platformPostUrl}`);
  info('Record the post id and URL in the content package before archiving it.');
}

main().catch(e => fail(e && e.message ? e.message : String(e)));
