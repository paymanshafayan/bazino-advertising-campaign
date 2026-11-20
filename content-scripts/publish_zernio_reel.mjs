#!/usr/bin/env node
/**
 * publish_zernio_reel.mjs
 * Publish or schedule a BAZINO Reel (with custom Instagram Reel cover) via Zernio API.
 *
 * Usage:
 *   $env:ZERNIO_API_KEY="sk_..."
 *   node Publish-Studio/publish_zernio_reel.mjs --dir "C:\projects\Antigravity Bazino\Publish-Studio\Ready-Media\reel-gta-vistamare-heist" --video "reel-gta-vistamare-heist-en.mp4" --cover "cover.jpg"
 *   Optional: --schedule "2026-11-19T17:15:00+03:00"  (omit for immediate publishNow: true)
 *   Optional: --dry-run
 */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const BASE = (process.env.ZERNIO_BASE_URL || 'https://zernio.com/api').replace(/\/+$/, '');
const KEY = process.env.ZERNIO_API_KEY || '';
if (!KEY) {
  console.error('ERROR: ZERNIO_API_KEY environment variable is required.');
  process.exit(1);
}

const ACCOUNTS = [
  { platform: 'instagram', accountId: '6ab391ba8d284ffb21332381' },
  { platform: 'youtube', accountId: '6ab964044d0196fa5a722376' },
  { platform: 'facebook', accountId: '6aba79525ad41c33d8cdeba7' },
  { platform: 'tiktok', accountId: '6aba84ea5be037602e0dc96f' },
  { platform: 'telegram', accountId: '6abaaaff42b1748681d57975' },
];

function parseArgs(argv) {
  const out = {};
  for (let i = 2; i < argv.length; i++) {
    const k = argv[i];
    if (k === '--dry-run') { out.dryRun = true; continue; }
    if (k.startsWith('--') && i + 1 < argv.length) {
      out[k.slice(2)] = argv[++i];
    }
  }
  return out;
}

async function api(method, route, body) {
  let lastErr;
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      const res = await fetch(`${BASE}${route}`, {
        method,
        headers: {
          authorization: `Bearer ${KEY}`,
          ...(body ? { 'content-type': 'application/json' } : {}),
        },
        ...(body ? { body: JSON.stringify(body) } : {}),
      });
      const text = await res.text();
      let json;
      try { json = JSON.parse(text); } catch { json = { raw: text.slice(0, 500) }; }
      if (!res.ok) throw new Error(`${method} ${route} -> HTTP ${res.status}: ${JSON.stringify(json)}`);
      return json;
    } catch (e) {
      lastErr = e;
      if (attempt < 4) await new Promise(r => setTimeout(r, 2000));
    }
  }
  throw lastErr;
}

async function uploadFile(filePath, contentType) {
  const bytes = fs.readFileSync(filePath);
  const filename = path.basename(filePath);
  console.log(`Uploading ${filename} (${(bytes.length / 1048576).toFixed(2)} MB)...`);
  const presign = await api('POST', '/v1/media/presign', {
    filename,
    contentType,
    size: bytes.length,
  });
  const uploadUrl = presign.uploadUrl || presign.url || presign.data?.uploadUrl;
  const fields = presign.fields || presign.data?.fields;
  if (!uploadUrl) throw new Error('No uploadUrl from presign');
  if (fields) {
    const args = ['-sS', '-f', '--retry', '3', '-X', 'POST', uploadUrl];
    for (const [k, v] of Object.entries(fields)) args.push('-F', `${k}=${v}`);
    args.push('-F', `file=@${filePath};type=${contentType}`);
    execFileSync('curl.exe', args, { stdio: 'inherit' });
  } else {
    execFileSync('curl.exe', [
      '-sS', '-f', '--retry', '3', '-X', 'PUT',
      '-H', `Content-Type: ${contentType}`,
      '--upload-file', filePath, uploadUrl,
    ], { stdio: 'inherit' });
  }
  const publicUrl = presign.publicUrl || presign.url || presign.data?.publicUrl;
  if (!publicUrl) throw new Error('No publicUrl from presign');
  console.log(`  Uploaded OK -> ${publicUrl}`);
  return publicUrl;
}

const args = parseArgs(process.argv);
if (!args.dir || !args.video) {
  console.error('Usage: node publish_zernio_reel.mjs --dir <folder> --video <mp4> [--cover <jpg>] [--caption <txt>] [--schedule <ISO>] [--dry-run]');
  process.exit(1);
}

const videoPath = path.resolve(args.dir, args.video);
const coverPath = args.cover ? path.resolve(args.dir, args.cover) : null;
const captionPath = path.resolve(args.dir, args.caption || 'caption.txt');

const caption = fs.readFileSync(captionPath, 'utf8').trim();
const videoUrl = args.dryRun ? 'https://example.com/video.mp4' : await uploadFile(videoPath, 'video/mp4');
const coverUrl = coverPath
  ? (args.dryRun ? 'https://example.com/cover.jpg' : await uploadFile(coverPath, 'image/jpeg'))
  : null;

const platforms = ACCOUNTS.map(acc => {
  if (acc.platform === 'instagram' && coverUrl) {
    return {
      ...acc,
      platformSpecificData: {
        instagramThumbnail: coverUrl,
        shareToFeed: true,
      },
    };
  }
  return acc;
});

const body = {
  content: caption,
  mediaItems: [{ type: 'video', url: videoUrl }],
  platforms,
  ...(args.schedule ? { scheduledFor: args.schedule } : { publishNow: true }),
};

if (args.dryRun) {
  console.log('DRY RUN PAYLOAD:', JSON.stringify(body, null, 2));
  process.exit(0);
}

const res = await api('POST', '/v1/posts', body);
const postId = res.id || res._id || res.post?._id || res.data?.id;
console.log('PUBLISHED OK:', JSON.stringify({ postId, status: res.status || res.post?.status || 'ok' }));
