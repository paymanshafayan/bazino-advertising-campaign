'use strict';

// Official remote Kling MCP: HTTPS streamable HTTP + MCP SDK OAuth (PKCE).
// No CLI, pasted cookies, API key, or Electron runtime is involved.
const http = require('node:http');
const { randomBytes } = require('node:crypto');
const { Client } = require('@modelcontextprotocol/sdk/client/index.js');
const { StreamableHTTPClientTransport } = require('@modelcontextprotocol/sdk/client/streamableHttp.js');
const { UnauthorizedError } = require('@modelcontextprotocol/sdk/client/auth.js');
const { KLING_ENDPOINT } = require('./constants.cjs');
const { validateOperation } = require('./protocol.cjs');

class KlingOAuthProvider {
  constructor({ vault, redirectUrl, openExternal, onBrowserOpen }) {
    this.vault = vault;
    this.redirectUrl = redirectUrl;
    this.openExternal = openExternal;
    this.onBrowserOpen = onBrowserOpen;
    this.expectedState = randomBytes(24).toString('base64url');
    this.redirected = false;
    this.verifier = null;
  }
  get clientMetadata() {
    return { redirect_uris:[this.redirectUrl], client_name:'Bazino Marketing Studio',
      grant_types:['authorization_code','refresh_token'], response_types:['code'],
      token_endpoint_auth_method:'none' };
  }
  state() { return this.expectedState; }
  clientInformation() { return this.vault.getKlingOAuth('clientInformation'); }
  saveClientInformation(info) { this.vault.saveKlingOAuth('clientInformation',info); }
  tokens() { return this.vault.getKlingOAuth('tokens'); }
  saveTokens(tokens) { this.vault.saveKlingOAuth('tokens',tokens); }
  saveCodeVerifier(verifier) { this.verifier = verifier; }
  codeVerifier() {
    if (!this.verifier) throw new Error('Kling OAuth verifier expired; restart authorization');
    return this.verifier;
  }
  discoveryState() { return this.vault.getKlingOAuth('discoveryState'); }
  saveDiscoveryState(state) { this.vault.saveKlingOAuth('discoveryState',state); }
  invalidateCredentials(scope) {
    if (['all','tokens'].includes(scope)) this.vault.saveKlingOAuth('tokens',undefined);
    if (['all','client'].includes(scope)) this.vault.saveKlingOAuth('clientInformation',undefined);
    if (['all','discovery'].includes(scope)) this.vault.saveKlingOAuth('discoveryState',undefined);
    if (['all','verifier'].includes(scope)) this.verifier=null;
  }
  async redirectToAuthorization(url) {
    if (!(url instanceof URL) || url.protocol!=='https:') throw new Error('Refusing an insecure Kling OAuth redirect');
    await this.openExternal(url.toString()); // Windows owner's OWN browser; never printed or committed.
    this.redirected = true;
    this.onBrowserOpen?.(); // start the callback clock AFTER launching, not during discovery
  }
}

function oauthCallback({ provider, port, timeoutMs, pathname='/kling-callback', label='Kling',
  deferTimeout=false }) {
  let resolveCode, rejectCode, timer=null, settled=false, closed=false;
  const code = new Promise((resolve,reject) => { resolveCode=resolve; rejectCode=reject; });
  code.catch(()=>{}); // opening the browser can fail before anyone awaits this promise
  function settle(error,value){
    if(settled)return;
    settled=true;clearTimeout(timer);timer=null;
    if(error)rejectCode(error);else resolveCode(value);
  }
  function startTimer(){
    if(settled||closed||timer)return;
    timer=setTimeout(()=>settle(new Error(`${label} OAuth timed out; start a fresh login`)),timeoutMs);
  }
  const server=http.createServer((req,res)=>{
    res.setHeader('Content-Type','text/html; charset=utf-8');
    res.setHeader('Cache-Control','no-store');
    res.setHeader('Referrer-Policy','no-referrer');
    const address=`127.0.0.1:${server.address()?.port}`;
    let url;
    try {url=new URL(req.url||'/',`http://${address}`);}
    catch {res.writeHead(400);res.end('<p>Invalid authorization response.</p>');return;}
    const reportedIssuer=url.searchParams.get('iss');
    const discoveredIssuer=provider.discoveryState()?.authorizationServerMetadata?.issuer;
    if(req.method!=='GET' || req.headers.host!==address || url.pathname!==pathname ||
      url.searchParams.get('state')!==provider.expectedState ||
      (reportedIssuer && reportedIssuer!==discoveredIssuer)) {
      res.writeHead(400);res.end('<p>Invalid authorization response.</p>');return;
    }
    if(settled){res.writeHead(410);res.end('<p>This login attempt has already finished.</p>');return;}
    if(url.searchParams.get('error') || !url.searchParams.get('code')) {
      res.writeHead(400);res.end('<p>Authorization was declined.</p>');
      settle(new Error(`${label} authorization was declined`));return;
    }
    res.writeHead(200);res.end(`<p>${label} authorization received. Return to Bazino Marketing Studio; you can close this tab.</p>`);
    settle(null,url.searchParams.get('code'));
  });
  if(!deferTimeout)startTimer();
  function close(reason){
    if(closed)return;
    closed=true;
    settle(reason||new Error(`${label} OAuth listener closed`));
    try{server.close();}catch{}
  }
  return {server,code,close,startTimer};
}

class Kling {
  constructor({ vault, openExternal, port=59677, endpoint=KLING_ENDPOINT, fetcher=globalThis.fetch }) {
    if(endpoint!==KLING_ENDPOINT) throw new Error('Only the official Kling MCP endpoint is allowed');
    this.vault=vault;this.openExternal=openExternal;this.port=port;this.fetcher=fetcher;
    this.client=null;this.transport=null;this.whoAmI=null;this.activeCallback=null;
  }
  async close() {
    const callback=this.activeCallback;
    this.activeCallback=null;
    callback?.close(new Error('Kling login was interrupted; start a fresh login'));
    const old=this.client;
    this.client=null;this.transport=null;this.whoAmI=null;
    if(old)await old.close().catch(()=>{});
  }
  async connect(provider) {
    const transport=new StreamableHTTPClientTransport(new URL(KLING_ENDPOINT),{authProvider:provider,fetch:this.fetcher});
    const client=new Client({name:'bazino-marketing-studio',version:'0.2.0'});
    try {
      await client.connect(transport);
      this.client=client;this.transport=transport;
      return client;
    } catch(e) {
      await client.close().catch(()=>{});
      throw e;
    }
  }
  async authorize({timeoutMs=600000}={}) {
    if(this.activeCallback)throw new Error('Kling login is already in progress; finish or restart it');
    await this.close();
    const redirectUrl=`http://127.0.0.1:${this.port}/kling-callback`;
    let listener;
    const provider=new KlingOAuthProvider({vault:this.vault,redirectUrl,
      openExternal:this.openExternal,onBrowserOpen:()=>listener.startTimer()});
    listener=oauthCallback({provider,port:this.port,timeoutMs,deferTimeout:true});
    this.activeCallback=listener;
    try {
      await new Promise((resolve,reject)=>{
        listener.server.once('error',reject);
        listener.server.listen(this.port,'127.0.0.1',resolve);
      });
      try {
        await this.connect(provider); // stored OAuth token may work without another login
      } catch(e) {
        if(!(e instanceof UnauthorizedError) || !provider.redirected) throw e;
        const code=await listener.code; // one-time code is consumed locally, never exposed to Git
        if(this.activeCallback!==listener)throw new Error('Kling login was cancelled');
        const transport=new StreamableHTTPClientTransport(new URL(KLING_ENDPOINT),{authProvider:provider,fetch:this.fetcher});
        try{await transport.finishAuth(code);} // SDK discovers metadata, PKCE, token endpoint
        finally{await transport.close().catch(()=>{});}
        if(this.activeCallback!==listener)throw new Error('Kling login was cancelled');
        await this.connect(provider);
      }
      if(this.activeCallback!==listener)throw new Error('Kling login was cancelled');
      return {ok:true,endpoint:KLING_ENDPOINT,identity:await this.identity(),
        message:'Kling MCP OAuth verified by who_am_i on this device'};
    } catch(e) {
      if(this.activeCallback===listener)await this.close();
      throw e;
    } finally {
      if(this.activeCallback===listener)this.activeCallback=null;
      listener.close();
    }
  }
  async ensureConnected() {
    if(this.client) return;
    if(!this.vault.getKlingOAuth('tokens')) throw new Error('Kling MCP is not authorized. Connect from the Windows app first.');
    // A remote/agent request must NEVER open a browser or ask for login behind the owner's back.
    const provider=new KlingOAuthProvider({vault:this.vault,
      redirectUrl:`http://127.0.0.1:${this.port}/kling-callback`,
      openExternal:async()=>{throw new Error('Kling OAuth expired. Reconnect from the Windows app.');}});
    try {await this.connect(provider);} catch(e) {
      // A saved token proves neither reachability nor a working OAuth session.
      // Do not expose SDK errors (which may contain callback URLs or tokens).
      const code=String(e?.cause?.code||e?.code||'');
      if(/fetch failed|network/i.test(String(e?.message||''))||
        /^(?:EAI_AGAIN|ENOTFOUND|ECONNRESET|ENETUNREACH|ETIMEDOUT|UND_ERR_CONNECT_TIMEOUT)$/.test(code))
        throw new Error('Kling MCP is unreachable from this Windows app. Check VPN, proxy and firewall before retrying OAuth.');
      throw new Error('Kling session is not active. Reconnect from the Windows app.');
    }
  }
  async identity() {
    await this.ensureConnected();
    const result=await this.client.callTool({name:'who_am_i',arguments:{}});
    if(result.isError) throw new Error('Kling who_am_i returned an error; OAuth not verified');
    this.whoAmI=result;
    return result;
  }
  async listTools() {
    if(!this.whoAmI) await this.identity(); // official guide: who_am_i first
    const result=await this.client.listTools();
    return {ok:true,endpoint:KLING_ENDPOINT,tools:result.tools.map(tool=>({
      name:tool.name,description:tool.description,inputSchema:tool.inputSchema
    }))};
  }
  async logout() {
    await this.close();
    this.vault.saveKlingOAuth('tokens',undefined);
    return {ok:true,message:'Local Kling OAuth tokens removed. Revoke server access in your Kling account if needed.'};
  }
  async execute(input) {
    const op=validateOperation(input);
    if(op.kind!=='kling')throw new Error('Expected Kling MCP operation');
    if(op.command==='who_am_i') return {ok:true,provider:'kling',body:await this.identity()};
    if(!this.whoAmI) await this.identity();
    const reply=await this.client.callTool({name:op.command,arguments:op.args});
    return {ok:!reply.isError,provider:'kling',command:op.command,body:reply,
      observedAt:new Date().toISOString()};
  }
}
module.exports={Kling,KlingOAuthProvider,oauthCallback};
