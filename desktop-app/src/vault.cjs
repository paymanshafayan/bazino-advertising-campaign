'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { createPublicKey } = require('node:crypto');
const { SECRET_FIELDS, PUBLIC_DEFAULTS } = require('./constants.cjs');
const {publicUrl}=require('./gateway-network.cjs');
const CONNECTOR_ID=/^[a-z][a-z0-9-]{0,39}$/;
const ACCESS=new Set(['none','read','approved-writes']);
const OAUTH_KEYS=new Set(['tokens','clientInformation','discoveryState']);
function normalizeConnector(input,old){
  if(!input||typeof input!=='object'||Array.isArray(input)||
    typeof input.id!=='string'||!CONNECTOR_ID.test(input.id)||
    typeof input.name!=='string'||!input.name.trim()||input.name.length>70||
    !['api','mcp'].includes(input.kind)||!ACCESS.has(input.agentAccess||'none'))
    throw new Error('Invalid connector ID, name, kind or agent access');
  if(typeof input.url==='string'&&/\/(?:\.{1,2}|%2e(?:%2e)?)(?:\/|[?#]|$)/i.test(input.url))
    throw new Error('Connector endpoint path must not contain dot segments');
  const url=publicUrl(input.url,{allowQuery:false});
  // Never bypass existing account/preflight/FLUX-only/Kling-identity safeguards by
  // re-registering the same official provider as an unrestricted generic API.
  if(['zernio.com','api.zernio.com','api.cloudflare.com'].includes(url.hostname)||
    input.kind==='mcp'&&url.hostname==='kling.ai'&&url.pathname==='/mcp')
    throw new Error('Use the dedicated Zernio, optional FLUX, or Kling controls for this provider');
  const pathname=url.pathname;
  if(pathname.includes('%')||pathname.includes('//')||/\/(?:\.|\.\.)\//.test(pathname))
    throw new Error('Connector endpoint path must be canonical and unencoded');
  const auth=input.auth||'none';
  if(!(input.kind==='api'?['none','bearer','header']:['none','bearer','oauth']).includes(auth))
    throw new Error('Unsupported connector authentication');
  const headerName=auth==='header'?String(input.headerName||'').trim():'';
  if(auth==='header'&&(!/^[A-Za-z][A-Za-z0-9-]{0,63}$/.test(headerName)||
    /^(?:host|cookie|set-cookie|content-length|content-type|transfer-encoding|connection|proxy-|forwarded|x-forwarded-|origin|referer|accept|sec-|mcp-session-id)$/i.test(headerName)))
    throw new Error('Use an ordinary API-key header, not a transport or cookie header');
  const urlValue=url.toString().replace(/\/$/,'');
  const sameBoundary=old?.url===urlValue&&old?.auth===auth&&old?.headerName===headerName&&old?.kind===input.kind;
  const provided=input.credential;
  if(provided!==undefined&&(typeof provided!=='string'||provided.length>4096||
    (provided.trim()&&provided.trim().length<8)))throw new Error('API credential must be a valid secret of at least eight characters');
  return {id:input.id,name:input.name.trim(),kind:input.kind,url:urlValue,auth,headerName,
    agentAccess:input.agentAccess||'none',
    credential:['bearer','header'].includes(auth)?(provided?.trim()|| (sameBoundary?old?.credential:'') ||''):'',
    oauth:auth==='oauth'&&sameBoundary?old?.oauth||{}:{}};
}
function publicConnector(item){
  return {id:item.id,name:item.name,kind:item.kind,url:item.url,auth:item.auth,
    headerName:item.headerName,agentAccess:item.agentAccess,
    hasCredential:!!item.credential,authorized:!!item.oauth?.tokens?.access_token,
    configured:item.auth==='none'||item.auth==='oauth'&&!!item.oauth?.tokens?.access_token||!!item.credential};
}
const { newIdentity, newSigningIdentity, fingerprint, combinedFingerprint } = require('./crypto.cjs');

class Vault {
  constructor(file, safeStorage) {
    this.file = file;
    this.safeStorage = safeStorage;
    this.data = { ...PUBLIC_DEFAULTS, secrets: {}, klingOAuth: {}, connectors: [] };
  }
  load() {
    if (!this.safeStorage?.isEncryptionAvailable()) throw new Error('OS-protected encryption is unavailable. No credentials will be stored.');
    if (!fs.existsSync(this.file)) return;
    const payload = JSON.parse(fs.readFileSync(this.file, 'utf8'));
    if (payload.v !== 1 || typeof payload.ciphertext !== 'string') throw new Error('Unknown settings format');
    const decoded = this.safeStorage.decryptString(Buffer.from(payload.ciphertext, 'base64'));
    const values = JSON.parse(decoded);
    const publicValues=Object.fromEntries(Object.keys(PUBLIC_DEFAULTS)
      .filter(key=>Object.hasOwn(values,key)).map(key=>[key,values[key]]));
    // Drop obsolete direct social/Manus credentials from older Windows vaults.
    // Keep the relay's device keys so an upgrade never silently breaks pairing.
    const allowed=new Set([...SECRET_FIELDS,'relayPrivateKey','relayPublicKey',
      'relaySigningPrivateKey','relaySigningPublicKey']);
    const stored=values.secrets && typeof values.secrets==='object' && !Array.isArray(values.secrets) ? values.secrets : {};
    const secrets=Object.fromEntries(Object.entries(stored).filter(([key])=>allowed.has(key)));
    const entries=values.connectors||[];
    if(!Array.isArray(entries)||entries.length>30)throw new Error('Invalid encrypted connector registry');
    const ids=new Set();
    const connectors=entries.map(item=>{
      const normalized=normalizeConnector(item);
      if(ids.has(normalized.id))throw new Error('Duplicate encrypted connector ID');
      ids.add(normalized.id);
      if(item.auth==='oauth'&&item.oauth&&typeof item.oauth==='object'&&
        !Array.isArray(item.oauth)&&Buffer.byteLength(JSON.stringify(item.oauth))<100000)
        normalized.oauth=item.oauth;
      return normalized;
    });
    this.data = { ...PUBLIC_DEFAULTS, ...publicValues, secrets, connectors,
      klingOAuth: { ...(values.klingOAuth || {}) } };
    if(Object.keys(secrets).length!==Object.keys(stored).length)this.save();
  }
  save() {
    if (!this.safeStorage?.isEncryptionAvailable()) throw new Error('OS-protected encryption is unavailable');
    const dir = path.dirname(this.file);
    fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
    const content = JSON.stringify({ v: 1, ciphertext: this.safeStorage.encryptString(JSON.stringify(this.data)).toString('base64') });
    const tmp = `${this.file}.${process.pid}.tmp`;
    fs.writeFileSync(tmp, content, { mode: 0o600, flag: 'wx' });
    fs.renameSync(tmp, this.file);
  }
  publicSettings() {
    const { secrets, agentPublicKey, klingOAuth, connectors, ...publicFields } = this.data;
    const configured = Object.fromEntries(SECRET_FIELDS.map(k => [k, !!secrets[k]]));
    return { ...publicFields, configured, klingAuthorized: !!klingOAuth?.tokens?.access_token,
      identityFingerprint: secrets.relayPublicKey && secrets.relaySigningPublicKey ?
        combinedFingerprint(secrets.relayPublicKey,secrets.relaySigningPublicKey) : '',
      agentFingerprint: agentPublicKey ? fingerprint(agentPublicKey) : '' };
  }
  update(patch) {
    if (!patch || typeof patch !== 'object' || Array.isArray(patch)) throw new Error('Expected settings object');
    const { secrets = {}, clearSecrets = [], ...publicFields } = patch;
    for (const [key, value] of Object.entries(publicFields)) {
      if (!(key in PUBLIC_DEFAULTS) || key === 'agentPublicKey') throw new Error(`Unknown or pairing-only setting: ${key}`);
      if (typeof PUBLIC_DEFAULTS[key] === 'boolean' && typeof value !== 'boolean') throw new Error(`Expected boolean: ${key}`);
      if (typeof PUBLIC_DEFAULTS[key] === 'string' && (typeof value !== 'string' || value.length > 500)) throw new Error(`Invalid text: ${key}`);
      this.data[key] = value;
    }
    if (!secrets || typeof secrets !== 'object' || Array.isArray(secrets)) throw new Error('Invalid credentials object');
    for (const [key, value] of Object.entries(secrets)) {
      if (!SECRET_FIELDS.includes(key)) throw new Error(`Unknown credential: ${key}`);
      if (typeof value !== 'string' || value.length > 4096) throw new Error(`Invalid credential: ${key}`);
      if (value.trim()) this.data.secrets[key] = value.trim(); // blank field means preserve old key
    }
    if (!Array.isArray(clearSecrets)) throw new Error('Invalid credential removal list');
    for (const key of clearSecrets) {
      if (!SECRET_FIELDS.includes(key)) throw new Error('Unknown credential removal target');
      delete this.data.secrets[key];
    }
    this.save();
    return this.publicSettings();
  }
  get(key) { return this.data.secrets[key] || ''; }
  public(key) { return this.data[key]; }
  connector(id){return this.data.connectors.find(row=>row.id===id)||null;}
  listConnectors(){return this.data.connectors.map(publicConnector);}
  saveConnector(input){
    if(!input||typeof input.id!=='string')throw new Error('Connector ID required');
    const old=this.connector(input.id),next=normalizeConnector(input,old);
    if(!old&&this.data.connectors.length>=30)throw new Error('Connector limit reached');
    this.data.connectors=old?this.data.connectors.map(row=>row.id===next.id?next:row):
      [...this.data.connectors,next];
    this.save();return publicConnector(next);
  }
  removeConnector(id){
    if(!this.connector(id))throw new Error('Unknown connector');
    this.data.connectors=this.data.connectors.filter(row=>row.id!==id);
    this.save();return {ok:true};
  }
  getConnectorOAuth(id,key){
    if(!OAUTH_KEYS.has(key))throw new Error('Invalid MCP OAuth key');
    return this.connector(id)?.oauth?.[key];
  }
  saveConnectorOAuth(id,key,value){
    if(!OAUTH_KEYS.has(key))throw new Error('Invalid MCP OAuth key');
    const row=this.connector(id);
    if(!row||row.kind!=='mcp'||row.auth!=='oauth')throw new Error('MCP OAuth connector not configured');
    if(value!==undefined&&(!value||typeof value!=='object'||Array.isArray(value)||
      Buffer.byteLength(JSON.stringify(value))>32768))throw new Error('Invalid MCP OAuth value');
    if(value===undefined)delete row.oauth[key];else row.oauth[key]=value;
    this.save();
  }
  allSecrets(){
    const values=[...SECRET_FIELDS.map(k=>this.get(k)),
      this.data.klingOAuth?.tokens?.access_token,this.data.klingOAuth?.tokens?.refresh_token];
    for(const row of this.data.connectors){
      values.push(row.credential,row.oauth?.tokens?.access_token,row.oauth?.tokens?.refresh_token,
        row.oauth?.clientInformation?.client_secret);
    }
    return values.filter(value=>typeof value==='string'&&value.length>=8);
  }
  getKlingOAuth(key) {
    if (!['tokens','clientInformation','discoveryState'].includes(key)) throw new Error('Invalid OAuth key');
    return this.data.klingOAuth?.[key];
  }
  saveKlingOAuth(key, value) {
    if (!['tokens','clientInformation','discoveryState'].includes(key)) throw new Error('Invalid OAuth key');
    if (value !== undefined && (!value || typeof value !== 'object' || Array.isArray(value) ||
      Buffer.byteLength(JSON.stringify(value)) > 32768)) throw new Error('Invalid OAuth value');
    if (!this.data.klingOAuth) this.data.klingOAuth = {};
    if (value === undefined) delete this.data.klingOAuth[key];
    else this.data.klingOAuth[key] = value;
    this.save();
  }
  pairAgent(publicKey) {
    if (typeof publicKey !== 'string' || publicKey.length>1000 ||
      createPublicKey(publicKey).asymmetricKeyType !== 'ed25519') throw new Error('Invalid agent signing key');
    this.data.agentPublicKey=publicKey;
    this.save();
    return this.publicSettings();
  }
  identity() {
    let changed=false;
    if (!this.get('relayPrivateKey') || !this.get('relayPublicKey')) {
      const pair = newIdentity();
      this.data.secrets.relayPrivateKey = pair.privateKey;
      this.data.secrets.relayPublicKey = pair.publicKey;
      changed=true;
    }
    if(!this.get('relaySigningPrivateKey')||!this.get('relaySigningPublicKey')){
      const pair=newSigningIdentity();
      this.data.secrets.relaySigningPrivateKey=pair.privateKey;
      this.data.secrets.relaySigningPublicKey=pair.publicKey;
      changed=true;
    }
    if(changed)this.save();
    return {
      privateKey: this.get('relayPrivateKey'), publicKey: this.get('relayPublicKey'),
      signingPrivateKey:this.get('relaySigningPrivateKey'),
      signingPublicKey:this.get('relaySigningPublicKey'),
      fingerprint:combinedFingerprint(this.get('relayPublicKey'),this.get('relaySigningPublicKey'))
    };
  }
}
module.exports = { Vault, normalizeConnector, publicConnector };
