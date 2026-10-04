'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { validateOperation, redact } = require('./protocol.cjs');
const { MAX_RESPONSE_BYTES } = require('./constants.cjs');
const { normalizeAccess, validateTargets, assertCommentAutomation, needsPublishing } = require('./zernio-workflow.cjs');

function serviceUrl(provider, apiPath, vault) {
  if (provider === 'zernio') return `https://zernio.com/api${apiPath}`;
  if (provider === 'cloudflare') {
    const account = String(vault.public('cloudflareAccountId') || '');
    if (!/^[a-f0-9]{32}$/i.test(account) ||
      apiPath !== `/accounts/${account}/ai/run/@cf/black-forest-labs/flux-1-schnell`)
      throw new Error('Set your own Cloudflare account ID for optional LOCAL FLUX access');
    return `https://api.cloudflare.com/client/v4${apiPath}`;
  }
  throw new Error('Unknown API host');
}
async function readLimited(response, max = MAX_RESPONSE_BYTES) {
  const length = Number(response.headers?.get?.('content-length') || 0);
  if (length > max) throw new Error('Provider response exceeds size limit');
  if (!response.body?.getReader) {
    const bytes = Buffer.from(await response.arrayBuffer());
    if (bytes.length > max) throw new Error('Provider response exceeds size limit');
    return bytes;
  }
  const reader = response.body.getReader(), chunks = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.length;
    if (size > max) { await reader.cancel(); throw new Error('Provider response exceeds size limit'); }
    chunks.push(value);
  }
  return Buffer.concat(chunks, size);
}

class Providers {
  constructor({ vault, assets, fetcher = globalThis.fetch, timeoutMs = 60000 }) {
    this.vault = vault; this.assets = assets; this.fetcher = fetcher; this.timeoutMs = timeoutMs;
  }
  async access() {
    const [listed,health]=await Promise.all([
      this.api({kind:'api',provider:'zernio',method:'GET',path:'/v1/accounts?status=connected'}),
      this.api({kind:'api',provider:'zernio',method:'GET',path:'/v1/accounts/health'})
    ]);
    if(!listed.ok||!health.ok)throw new Error(`Zernio discovery or health check failed (${listed.status}/${health.status}); no write was sent`);
    return normalizeAccess(listed.body,health.body);
  }
  async engagementAccess(accountId,access) {
    if(!access)access=await this.access();
    const account=access.accounts.find(a=>a.accountId===accountId && a.connected &&
      ['instagram','facebook'].includes(a.platform));
    if(!account)throw new Error('Select a live Instagram/Facebook account for the engagement check');
    const params=`?accountId=${encodeURIComponent(accountId)}&limit=1`;
    const [comments,messages]=await Promise.all([
      this.api({kind:'api',provider:'zernio',method:'GET',path:`/v1/inbox/comments${params}`}),
      this.api({kind:'api',provider:'zernio',method:'GET',path:`/v1/inbox/conversations${params}`})
    ]);
    function readable(result) {
      const meta=result.body?.meta;
      return Boolean(result.ok && Array.isArray(result.body?.data) &&
        Number.isInteger(meta?.accountsQueried) && meta.accountsQueried>=1 &&
        meta.accountsFailed===0 && Array.isArray(meta.failedAccounts) &&
        Array.isArray(meta.accountsSkipped) &&
        !meta.failedAccounts.some(row=>row.accountId===accountId) &&
        !meta.accountsSkipped.some(row=>row.accountId===accountId));
    }
    return {ok:true,accountId,platform:account.platform,commentsReadable:readable(comments),
      messagesReadable:readable(messages),checkedAt:new Date().toISOString(),
      note:'The comments endpoint lists commented posts, not each comment. Successful listing and inbox reads do not prove per-comment reads or outbound DM permission/delivery. Verify actual supported delivery before promising it.'};
  }
  async api(input) {
    const op = validateOperation(input);
    if (op.kind !== 'api') throw new Error('Expected an API operation');
    if (op.provider==='zernio') {
      const route=op.path.split('?')[0];
      if(op.method==='POST'&&route==='/v1/posts') {
        if(op.body?.isDraft!==true || op.body?.platforms?.length)
          validateTargets(op.body,await this.access());
      } else if(op.method==='PUT'&&/^\/v1\/posts\/[a-f0-9]{24}$/i.test(route)&&
        op.body?.isDraft!==true&&(needsPublishing(op.body)||op.body?.isDraft===false)) {
        // Draft promotion is a write to EXISTING content; verify the post and every
        // inherited account before it can be scheduled or published.
        const existing=await this.api({kind:'api',provider:'zernio',method:'GET',path:route});
        if(!existing.ok||existing.body?.post?._id!==route.split('/')[3]||
          existing.body.post.status==='published')
          throw new Error('Cannot confirm an editable Zernio post; inspect its status before promotion');
        const targets=op.body?.platforms?.length?op.body.platforms:
          existing.body.post.platforms?.map(p=>({platform:p.platform,
            accountId:typeof p.accountId==='string'?p.accountId:p.accountId?._id}));
        validateTargets({...op.body,platforms:targets},await this.access());
      } else if(op.method==='POST'&&route==='/v1/comment-automations') {
        const access=await this.access();
        assertCommentAutomation(op.body,access);
        const inbox=await this.engagementAccess(op.body.accountId,access);
        if(!inbox.commentsReadable||!inbox.messagesReadable)
          throw new Error('Zernio has not confirmed comment and message read access; do not activate a DM automation');
      }
    }
    const target = new URL(serviceUrl(op.provider, op.path, this.vault));
    const headers = { Accept: 'application/json' };
    let requestIdempotencyKey;
    if (op.provider === 'zernio') {
      const key = this.vault.get('zernioKey'); if (!key) throw new Error('Set a Zernio API key in local Settings; the portal vault does not expose keys to this app');
      headers.Authorization = `Bearer ${key}`;
      // POST /v1/posts: the operation reference recommends Idempotency-Key (24h).
      // x-request-id only deduplicates matching content and is not a safe changed-body retry.
      if (op.method === 'POST' && op.path.split('?')[0] === '/v1/posts') {
        requestIdempotencyKey=op.idempotencyKey||crypto.randomUUID();
        headers['Idempotency-Key']=requestIdempotencyKey;
      }
    } else if (op.provider === 'cloudflare') {
      const key = this.vault.get('cloudflareToken');
      if (!key) throw new Error('Use portal FLUX by default; optional LOCAL FLUX needs a token in Settings');
      headers.Authorization = `Bearer ${key}`;
    }
    const body = op.body === undefined ? undefined : JSON.stringify(op.body);
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await this.fetcher(target.toString(), {
      method: op.method, headers, body, redirect: 'manual', signal: AbortSignal.timeout(this.timeoutMs)
    });
    if (response.status >= 300 && response.status < 400) throw new Error(`Redirect refused (${response.status}); no credentials were forwarded`);
    const bytes = await readLimited(response, op.provider === 'cloudflare' ? 20 * 1024 * 1024 : MAX_RESPONSE_BYTES);
    const type = String(response.headers?.get?.('content-type') || '');
    let value;
    if (type.includes('image/png') || type.includes('image/jpeg')) {
      value = { imported: this.assets.saveImage(bytes, type), note: 'Generated image is on the owner’s computer' };
    } else {
      const text = bytes.toString('utf8');
      try { value = JSON.parse(text); } catch { value = { text }; }
      if (op.provider === 'cloudflare' && value?.result?.image && typeof value.result.image === 'string') {
        const image = Buffer.from(value.result.image, 'base64');
        const mime = image.subarray(0,3).equals(Buffer.from([0xff,0xd8,0xff])) ? 'image/jpeg' : 'image/png';
        value.result.image = { imported: this.assets.saveImage(image, mime) };
      }
    }
    return { ok: response.ok, status: response.status, provider: op.provider, method: op.method,
      ...(requestIdempotencyKey?{idempotencyKey:requestIdempotencyKey}:{}),
      observedAt: new Date().toISOString(), body: redact(value,0,[this.vault.get('zernioKey'),this.vault.get('cloudflareToken')]) };
  }
  async uploadZernioMedia(input) {
    const op = validateOperation(input);
    if (op.kind !== 'zernio-media-upload') throw new Error('Expected a Zernio media upload');
    const file = this.assets.find(op.assetId);
    const key = this.vault.get('zernioKey'); if (!key) throw new Error('Set the Zernio API key in local Settings');
    const presign = await this.fetcher('https://zernio.com/api/v1/media/presign', {
      method:'POST', redirect:'manual', headers:{ Authorization:`Bearer ${key}`, 'Content-Type':'application/json' },
      body:JSON.stringify({ filename: path.basename(file.path), contentType:file.mime, size:file.bytes }),
      signal:AbortSignal.timeout(this.timeoutMs)
    });
    const details = JSON.parse((await readLimited(presign)).toString('utf8'));
    if (!presign.ok || !details.uploadUrl || !details.publicUrl) throw new Error(`Zernio presign failed (HTTP ${presign.status})`);
    const dest = new URL(details.uploadUrl), publicUrl = new URL(details.publicUrl);
    if (dest.protocol !== 'https:' || !dest.hostname.endsWith('.r2.cloudflarestorage.com') ||
      dest.username || dest.password || publicUrl.protocol !== 'https:' || publicUrl.hostname !== 'media.zernio.com') {
      throw new Error('Zernio returned an unexpected media host; stopped before uploading bytes');
    }
    const upload = await this.fetcher(dest.toString(), {
      method:'PUT', redirect:'manual', headers:{'Content-Type':file.mime},
      body:fs.createReadStream(file.path), duplex:'half', signal:AbortSignal.timeout(10*60_000)
    });
    if (!upload.ok) throw new Error(`Zernio media upload failed (HTTP ${upload.status})`);
    return {ok:true,status:upload.status,provider:'zernio',publicUrl:publicUrl.toString(),
      assetId:op.assetId, note:'Media URL may expire after 7 days if not published; see Zernio media guide.',
      observedAt:new Date().toISOString()};
  }
}
module.exports = { Providers, serviceUrl, readLimited };
