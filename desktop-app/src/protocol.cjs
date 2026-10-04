'use strict';

const { PROVIDERS, KLING_COMMANDS, READ_METHODS, MAX_REQUEST_BYTES } = require('./constants.cjs');
const { FEEDS } = require('./research.cjs');
const { validateReport } = require('./editorial.cjs');
const { validateSchedule } = require('./zernio-workflow.cjs');
const ID_RE = /^[a-f0-9-]{36}$/i;
const CONNECTOR_ID=/^[a-z][a-z0-9-]{0,39}$/;
const MCP_READS=new Set(['tools','resources','read-resource','prompts','get-prompt']);
const SECRET_RE = /(?:password|secret|token|api[_-]?key|authorization|cookie|credential|refresh[_-]?token|private[_-]?key)/i;
const KLING_READS = new Set(['who_am_i','query_tasks','element_list','element_get',
  'motion_library_list','query_membership_and_credits']);
// A local Cloudflare token, if explicitly configured, is usable for FLUX only.
const FLUX_PATH = /^\/accounts\/[a-f0-9]{32}\/ai\/run\/@cf\/black-forest-labs\/flux-1-schnell$/i;

function validPath(input) {
  if (typeof input !== 'string' || input.length < 1 || input.length > 700 || !input.startsWith('/')) throw new Error('A relative API path beginning with / is required');
  if (/^\/\//.test(input) || /[\\\r\n#\0]/.test(input) || /(?:^|\/)\.\.(?:\/|$)/.test(input) || /:\/\//.test(input)) throw new Error('External or unsafe API path rejected');
  const parsed = new URL(input, 'https://example.invalid');
  if (parsed.origin !== 'https://example.invalid' || parsed.username || parsed.password) throw new Error('Cross-origin request rejected');
  const route=input.split('?')[0];
  // The preflight and sensitive-message approval use the literal route. Do not
  // let URL normalization/percent decoding change the route AFTER those checks.
  if(route.includes('%')||route.includes('//')||parsed.pathname!==route)
    throw new Error('API route must be canonical and unencoded');
  if([...parsed.searchParams.keys()].some(key=>/(?:^|[_-])(?:key|token|secret|password|pass|auth|code|credential|signature|sig|session)(?:$|[_-])|(?:apiKey|accessToken|refreshToken|clientSecret|secretKey|authorization)/i.test(key)))
    throw new Error('Credentials may not be supplied in an API query string');
  return input;
}
function validateOperation(op) {
  if (!op || typeof op !== 'object' || Array.isArray(op)) throw new Error('Operation must be an object');
  if (op.kind === 'api') {
    if (!PROVIDERS.includes(op.provider)) throw new Error('Unknown tool');
    const method = String(op.method || 'GET').toUpperCase();
    if (!['GET','HEAD','POST','PUT','PATCH','DELETE'].includes(method)) throw new Error('Unsupported HTTP method');
    const apiPath = validPath(op.path);
    if (op.provider === 'cloudflare' && (method !== 'POST' || !FLUX_PATH.test(apiPath)))
      throw new Error('Optional local Cloudflare access is limited to FLUX image generation');
    if ((method === 'GET' || method === 'HEAD') && op.body != null) throw new Error('GET/HEAD must not include a body');
    const body = op.body == null ? undefined : op.body;
    // WhatsApp uses inbox/broadcast, not posts. On a PUT, a past date may promote
    // an existing draft directly to an immediate publication just like on POST.
    if (op.provider === 'zernio' && ((method === 'POST' && apiPath.split('?')[0] === '/v1/posts') ||
      (method === 'PUT' && /^\/v1\/posts\/[a-f0-9]{24}$/i.test(apiPath.split('?')[0])))) {
      if(Array.isArray(body?.platforms) && body.platforms.some(item => item?.platform === 'whatsapp'))
        throw new Error('WhatsApp uses Zernio inbox/broadcast APIs, not posts');
      validateSchedule(body);
    }
    if (body !== undefined && Buffer.byteLength(JSON.stringify(body)) > MAX_REQUEST_BYTES) throw new Error('Request body too large');
    if(op.idempotencyKey!==undefined && (op.provider!=='zernio'||method!=='POST'||apiPath.split('?')[0]!=='/v1/posts'||
      typeof op.idempotencyKey!=='string'||!ID_RE.test(op.idempotencyKey))) throw new Error('Idempotency key must be a UUID for Zernio POST /v1/posts');
    return { kind: 'api', provider: op.provider, method, path: apiPath, body,
      ...(op.idempotencyKey?{idempotencyKey:op.idempotencyKey}:{}) };
  }
  if(op.kind==='web-fetch'){
    // The URL is checked again by the socket-pinned public transport at execution.
    if(typeof op.url!=='string'||op.url.length>2048)throw new Error('Public page URL required');
    return {kind:'web-fetch',url:op.url};
  }
  if(op.kind==='gateway-list')return {kind:'gateway-list'};
  if(op.kind==='gateway-api'){
    if(!CONNECTOR_ID.test(op.connectorId||''))throw new Error('Registered API connector ID required');
    const method=String(op.method||'GET').toUpperCase();
    if(!['GET','HEAD','POST','PUT','PATCH','DELETE'].includes(method))throw new Error('Unsupported API method');
    const apiPath=validPath(op.path);
    if(['GET','HEAD'].includes(method)&&op.body!=null)throw new Error('GET/HEAD must not include a body');
    const body=op.body==null?undefined:op.body;
    if(body!==undefined&&(typeof body!=='object'||Buffer.byteLength(JSON.stringify(body))>MAX_REQUEST_BYTES))
      throw new Error('Gateway API body must be a small JSON object or array');
    return {kind:'gateway-api',connectorId:op.connectorId,method,path:apiPath,
      ...(body!==undefined?{body}:{})};
  }
  if(op.kind==='gateway-mcp'){
    if(!CONNECTOR_ID.test(op.connectorId||''))throw new Error('Registered MCP connector ID required');
    const action=op.action;
    if(!MCP_READS.has(action)&&action!=='call')throw new Error('Unsupported MCP action');
    if(action==='call'&& (typeof op.tool!=='string'||!/^[-\w.]{1,120}$/.test(op.tool)||
      !op.args||typeof op.args!=='object'||Array.isArray(op.args)||
      Buffer.byteLength(JSON.stringify(op.args))>MAX_REQUEST_BYTES))
      throw new Error('MCP tool call requires a name and bounded JSON arguments');
    if(action==='read-resource'&&(typeof op.uri!=='string'||op.uri.length>2048||/[\r\n\0]/.test(op.uri)))
      throw new Error('MCP resource URI required');
    if(action==='get-prompt'&&(typeof op.name!=='string'||!op.name||op.name.length>120||
      (op.args!==undefined&&(!op.args||typeof op.args!=='object'||Array.isArray(op.args)||
        Buffer.byteLength(JSON.stringify(op.args))>MAX_REQUEST_BYTES))))
      throw new Error('MCP prompt name and small arguments required');
    return {kind:'gateway-mcp',connectorId:op.connectorId,action,
      ...(action==='call'?{tool:op.tool,args:op.args}:{}),
      ...(action==='read-resource'?{uri:op.uri}:{}),
      ...(action==='get-prompt'?{name:op.name,args:op.args||{}}:{})};
  }
  if (op.kind === 'kling') {
    if (!KLING_COMMANDS.includes(op.command)) throw new Error('Unknown Kling MCP tool');
    if (!op.args || typeof op.args !== 'object' || Array.isArray(op.args) ||
      Buffer.byteLength(JSON.stringify(op.args)) > MAX_REQUEST_BYTES) throw new Error('Kling MCP arguments must be a small JSON object');
    return { kind: 'kling', command: op.command, args: op.args };
  }
  if (op.kind === 'zernio-media-upload') {
    if (typeof op.assetId !== 'string' || !ID_RE.test(op.assetId)) throw new Error('Use an imported asset ID, not a file path');
    return { kind:'zernio-media-upload', assetId:op.assetId };
  }
  if (op.kind === 'zernio-access')return {kind:'zernio-access'};
  if (op.kind === 'zernio-engagement-check') {
    if(typeof op.accountId!=='string'||!/^[a-zA-Z0-9_-]{1,100}$/.test(op.accountId))
      throw new Error('Use an account ID discovered from Zernio');
    return {kind:'zernio-engagement-check',accountId:op.accountId};
  }
  if (op.kind === 'research') {
    if(op.action!=='scan'||!Array.isArray(op.sourceIds)||op.sourceIds.length>FEEDS.length||
      new Set(op.sourceIds).size!==op.sourceIds.length||op.sourceIds.some(id=>!FEEDS.some(feed=>feed.id===id)))
      throw new Error('Select only curated public research feeds');
    return {kind:'research',action:'scan',sourceIds:op.sourceIds};
  }
  if (op.kind === 'editorial') {
    if(op.action==='list')return {kind:'editorial',action:'list'};
    if(op.action==='save' && op.report && Buffer.byteLength(JSON.stringify(op.report))<=MAX_REQUEST_BYTES) {
      validateReport(op.report);
      return {kind:'editorial',action:'save',report:op.report};
    }
    throw new Error('Invalid local research report request');
  }
  throw new Error('Unknown operation type');
}
function isMutating(op) {
  return op.kind === 'zernio-media-upload' ||
    (op.kind==='gateway-api'&&!READ_METHODS.includes(op.method)) ||
    (op.kind==='gateway-mcp'&&op.action==='call') ||
    (op.kind === 'editorial' && op.action === 'save') ||
    (op.kind === 'api' && !READ_METHODS.includes(op.method)) ||
    (op.kind === 'kling' && !KLING_READS.has(op.command));
}
function redact(value, depth = 0, secrets = []) {
  if (depth > 12) return '[depth-limit]';
  if (Array.isArray(value)) return value.slice(0, 100).map(item => redact(item, depth + 1, secrets));
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).slice(0, 150).map(([k,v]) =>
      [k, SECRET_RE.test(k) && !['tokenValid','tokenExpiresAt'].includes(k) ? '[redacted]' :
        redact(v, depth + 1, secrets)]));
  }
  if (typeof value === 'string') {
    let text=value;
    for (const secret of secrets) if (typeof secret==='string' && secret.length>=8) text=text.replaceAll(secret,'[redacted]');
    text=text.replace(/(Bearer\s+)[^\s"']+/gi,'$1[redacted]')
      .replace(/(api\.telegram\.org\/bot)[^/\s"']+/gi,'$1[redacted]')
      .replace(/([?&](?:key|token|api_key|access_token)=)[^&\s"']+/gi,'$1[redacted]');
    return text.length>24000 ? text.slice(0,24000)+'…[truncated]' : text;
  }
  return value;
}
function validateEnvelope(request, now = Date.now()) {
  if (!request || request.v !== 1 || !ID_RE.test(request.id || '')) throw new Error('Invalid relay request');
  const at = Date.parse(request.issuedAt), until = Date.parse(request.expiresAt);
  if (!Number.isFinite(at) || !Number.isFinite(until) || at > now + 30000 || until < now || until - at > 10 * 60_000) throw new Error('Expired or invalid relay request');
  if (typeof request.replyPublicKey !== 'string' || !request.replyPublicKey.includes('BEGIN PUBLIC KEY') || request.replyPublicKey.length > 1000) throw new Error('Reply encryption key missing');
  if (!request.payload || typeof request.payload !== 'object') throw new Error('Encrypted request missing');
  return request;
}
module.exports = { validPath, validateOperation, isMutating, redact, validateEnvelope, SECRET_RE };
