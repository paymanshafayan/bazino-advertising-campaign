'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { EventEmitter } = require('node:events');
const { REPO, BRANCH, RELAY_ROOT, MAX_RESPONSE_BYTES } = require('./constants.cjs');
const { seal, unseal, verifyRequest, fingerprint, signReply } = require('./crypto.cjs');
const { validateEnvelope, validateOperation, isMutating, redact } = require('./protocol.cjs');

function encodedPath(value) { return value.split('/').map(encodeURIComponent).join('/'); }
function remotePath(file) { return `repos/${REPO}/contents/${encodedPath(`${RELAY_ROOT}/${file}`)}`; }

class Relay extends EventEmitter {
  constructor({ vault, service, approve, statePath, fetcher = globalThis.fetch, intervalMs = 20000 }) {
    super();
    this.vault = vault; this.service = service; this.approve = approve;
    this.statePath = statePath; this.fetcher = fetcher; this.intervalMs = intervalMs;
    this.state = { completed: {}, inProgress: {}, pending: {}, rejected: {} };
    this.connected = false; this.timer = null; this.polling = false;
    try { this.state = { ...this.state, ...JSON.parse(fs.readFileSync(statePath, 'utf8')) }; } catch { /* new machine */ }
  }
  saveState() {
    fs.mkdirSync(path.dirname(this.statePath), { recursive:true, mode:0o700 });
    const tmp = `${this.statePath}.${process.pid}.tmp`;
    fs.writeFileSync(tmp, JSON.stringify(this.state), { mode:0o600 });
    fs.renameSync(tmp, this.statePath);
  }
  async github(resource, { method='GET', body } = {}) {
    const token = this.vault.get('githubToken');
    if (!token) throw new Error('GitHub fine-grained Contents token is missing in desktop Settings');
    const target = new URL(`https://api.github.com/${resource}`);
    // A temporary network failure once prevented the Windows relay from connecting
    // even though the browser bridge was online. Retry *reads* only: blindly
    // repeating a PUT after an uncertain response could duplicate a signed reply.
    const attempts=method==='GET'?3:1;
    for(let attempt=1;attempt<=attempts;attempt++){
      let response;
      try {
        response=await this.fetcher(target, {
          method, redirect:'manual',
          headers: { Authorization:`Bearer ${token}`, Accept:'application/vnd.github+json',
            'X-GitHub-Api-Version':'2022-11-28', 'User-Agent':'BazinoMarketingDesktop/0.1',
            ...(body ? { 'Content-Type':'application/json' } : {}) },
          body: body ? JSON.stringify(body) : undefined,
          signal: AbortSignal.timeout(method==='GET'?12000:20000)
        });
      } catch(e) {
        if(attempt===attempts || !['TypeError','AbortError','TimeoutError'].includes(e?.name))throw e;
        await new Promise(resolve=>setTimeout(resolve,350*attempt));
        continue;
      }
      if(method==='GET'&&attempt<attempts&&[502,503,504].includes(response.status)){
        await response.body?.cancel?.().catch(()=>{});
        await new Promise(resolve=>setTimeout(resolve,350*attempt));
        continue;
      }
      const reply = await response.json().catch(() => ({}));
      if (!response.ok) {
        const err = new Error(`GitHub ${response.status} on ${method} ${resource.split('?')[0]}`);
        err.status = response.status; throw err;
      }
      return reply;
    }
  }
  async getFile(file) {
    try {
      const item = await this.github(`${remotePath(file)}?ref=${encodeURIComponent(BRANCH)}`);
      return JSON.parse(Buffer.from(item.content.replace(/\s/g,''), 'base64').toString('utf8'));
    } catch (e) { if (e.status === 404) return null; throw e; }
  }
  assertSameFile(file, existing, expected) {
    if(file.startsWith('responses/')&&JSON.stringify(existing)!==JSON.stringify(expected))
      throw new Error('Existing GitHub reply differs from local signed result; investigate possible tampering');
    if(file==='desktop-identity.json'&&
      (existing.fingerprint!==expected.fingerprint||existing.publicKey!==expected.publicKey||
        existing.signingPublicKey!==expected.signingPublicKey))
      throw new Error('Existing desktop public keys differ from this device; pairing must stop');
    return existing;
  }
  async putFile(file, value) {
    const text = JSON.stringify(value);
    if (Buffer.byteLength(text) > 900000) throw new Error('Relay result is too large for GitHub Contents');
    const current = await this.getFile(file);
    if (current) return this.assertSameFile(file,current,value); // immutable result/identity
    try {
      return await this.github(remotePath(file), { method:'PUT', body: {
        message:`marketing relay: ${file.split('/').pop()}`, content:Buffer.from(text).toString('base64'), branch:BRANCH
      } });
    } catch (e) {
      if (e.status === 409 || e.status === 422) {
        const raced = await this.getFile(file); if (raced) return this.assertSameFile(file,raced,value);
      }
      throw e;
    }
  }
  async check() {
    const meta = await this.github(`repos/${REPO}`);
    if (meta.private !== true) throw new Error('Repository is not private; refusing to start relay');
    await this.github(`${remotePath('README.md')}?ref=${encodeURIComponent(BRANCH)}`);
    return true;
  }
  async candidateAgent() {
    await this.check();
    const identity=await this.getFile('agent-identity.json');
    if (!identity || identity.v!==1 || typeof identity.publicKey!=='string' ||
      !identity.publicKey.includes('BEGIN PUBLIC KEY') || identity.publicKey.length>1000 ||
      identity.fingerprint!==fingerprint(identity.publicKey)) throw new Error('Agent signing key has not been published or is invalid');
    return {publicKey:identity.publicKey,fingerprint:identity.fingerprint};
  }
  async connect() {
    await this.check();
    const identity = this.vault.identity();
    const published = await this.getFile('desktop-identity.json');
    if (published && (published.fingerprint !== identity.fingerprint ||
      published.publicKey !== identity.publicKey || published.signingPublicKey !== identity.signingPublicKey)) {
      throw new Error('Desktop public key changed on GitHub. Stop: owner must investigate and pair again; never overwrite silently.');
    }
    if (!published) await this.putFile('desktop-identity.json', {
      version:1, publicKey:identity.publicKey, signingPublicKey:identity.signingPublicKey,
      fingerprint:identity.fingerprint, publishedAt:new Date().toISOString()
    });
    this.connected = true;
    const status={ connected:true, fingerprint:identity.fingerprint,
      agentFingerprint:this.vault.publicSettings().agentFingerprint };
    this.emit('status',status);
    this.timer = setInterval(() => this.poll().catch(e => this.emit('error', e)), this.intervalMs);
    try {await this.poll();}catch(e){this.disconnect();throw e;}
    return status;
  }
  disconnect() {
    this.connected = false;
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.emit('status',{ connected:false });
  }
  async listRequests() {
    try {
      const rows = await this.github(`${remotePath('requests')}?ref=${encodeURIComponent(BRANCH)}`);
      return (Array.isArray(rows) ? rows : []).filter(x => /^[a-f0-9-]{36}\.json$/i.test(x.name)).sort((a,b) => a.name.localeCompare(b.name));
    } catch (e) { if (e.status === 404) return []; throw e; }
  }
  async publish(id, response, replyPublicKey) {
    let safe = redact(response,0,this.vault.allSecrets());
    let envelope;
    try { envelope = seal(safe, replyPublicKey, id, MAX_RESPONSE_BYTES); }
    catch { safe = { ok:false, error:'RESULT_TOO_LARGE', message:'Result is too large for the relay. Inspect it locally.' };
      envelope = seal(safe, replyPublicKey, id); }
    const file = `responses/${id}.json`;
    const signed={v:1,id,createdAt:new Date().toISOString(),payload:envelope};
    signed.signature=signReply(signed,this.vault.identity().signingPrivateKey);
    this.state.pending[id]=signed;
    this.saveState();
    await this.putFile(file,signed);
    delete this.state.pending[id]; delete this.state.inProgress[id];
    this.state.completed[id] = new Date().toISOString();
    // Requests expire after 10 min; keep replay markers for at least 24 hours.
    for(const [key,time] of Object.entries(this.state.completed))
      if(Date.now()-Date.parse(time)>24*3600_000)delete this.state.completed[key];
    this.saveState();
  }
  async poll() {
    if (!this.connected || this.polling) return;
    this.polling = true;
    try {
      for (const [id, signed] of Object.entries(this.state.pending)) {
        await this.putFile(`responses/${id}.json`,signed);
        delete this.state.pending[id]; delete this.state.inProgress[id]; this.state.completed[id] = new Date().toISOString(); this.saveState();
      }
      if (!this.vault.public('agentPublicKey')) return; // pairing required before any remote execution
      const requests = await this.listRequests();
      for (const item of requests) {
        if(!this.connected)break;
        const id = item.name.replace(/\.json$/, '');
        if (this.state.completed[id] || this.state.pending[id] || this.state.rejected[id]) continue;
        const request = await this.getFile(`requests/${id}.json`);
        if (!request) continue;
        if (!verifyRequest(request,this.vault.public('agentPublicKey'))) {
          this.state.rejected[id]=new Date().toISOString();
          for(const [key,time] of Object.entries(this.state.rejected))
            if(Date.now()-Date.parse(time)>24*3600_000)delete this.state.rejected[key];
          this.saveState();
          this.emit('activity',{id,status:'rejected (unpaired or invalid signature)'});
          continue;
        }
        try {
          validateEnvelope(request);
          if (request.id !== id) throw new Error('Request filename does not match ID');
          if (this.state.inProgress[id]) throw new Error('UNCERTAIN_PREVIOUS_ATTEMPT: do not repeat a possible publication');
          const op = validateOperation(unseal(request.payload, this.vault.identity().privateKey, id));
          const before=['gateway-api','gateway-mcp'].includes(op.kind)?
            this.vault.connector(op.connectorId):null;
          if(['gateway-api','gateway-mcp'].includes(op.kind)){
            if(!before||before.kind!==(op.kind==='gateway-api'?'api':'mcp')||
              before.agentAccess==='none'||isMutating(op)&&before.agentAccess!=='approved-writes')
              throw new Error('CONNECTOR_NOT_SHARED_WITH_AGENT');
          }
          this.state.inProgress[id] = { startedAt:new Date().toISOString(), operation:isMutating(op)?'write':'read' };
          this.saveState(); // crash after this point never replays a mutating operation automatically
          // An automation or outbound message can contact many people after its single
          // creation. Never let the broad auto-approve-writes toggle waive its own prompt.
          const messaging=op.kind==='api'&&op.provider==='zernio'&&isMutating(op)&&
            /(?:^|\/)(?:comment-automations|comments|messages|inbox|broadcasts|sequences|replies)(?:\/|$)/
              .test(op.path.split('?')[0]);
          // Generic connectors and MCP tool calls never inherit a blanket write grant:
          // tool schemas are untrusted and a new API may send or spend unexpectedly.
          const gatewayWrite=op.kind==='gateway-api'&&isMutating(op)||
            op.kind==='gateway-mcp'&&op.action==='call';
          const needsPrompt = messaging || gatewayWrite ||
            (isMutating(op) ? !this.vault.public('autoApproveWrites') :
              !this.vault.public('autoApproveReads'));
          const approved = needsPrompt ? await this.approve(op) : true;
          if (!approved) { await this.publish(id,{ok:false,error:'DECLINED_BY_OWNER'},request.replyPublicKey); continue; }
          if(before&&this.vault.connector(op.connectorId)!==before)
            throw new Error('CONNECTOR_CHANGED_DURING_APPROVAL');
          const result = await this.service(op);
          await this.publish(id,result,request.replyPublicKey);
          this.emit('activity',{ id, tool:op.kind==='api'?op.provider:op.kind, mutating:isMutating(op), status:'completed' });
        } catch (e) {
          if (request?.replyPublicKey) {
            try { await this.publish(id,{ok:false,error:String(e.message||'FAILED').slice(0,250)},request.replyPublicKey); }
            catch (postError) { this.emit('error',postError); }
          }
          this.emit('activity',{ id, status:'failed' });
        }
      }
    } finally { this.polling = false; }
  }
}
module.exports = { Relay, encodedPath, remotePath };
