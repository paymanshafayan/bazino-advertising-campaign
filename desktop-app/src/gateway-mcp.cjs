'use strict';

// Dynamic remote Streamable HTTP MCP connectors. Never launch local stdio code
// and never initiate an OAuth browser flow from a relayed agent operation.
const {randomBytes}=require('node:crypto');
const {Client}=require('@modelcontextprotocol/sdk/client/index.js');
const {StreamableHTTPClientTransport}=require('@modelcontextprotocol/sdk/client/streamableHttp.js');
const {UnauthorizedError}=require('@modelcontextprotocol/sdk/client/auth.js');
const {oauthCallback}=require('./kling.cjs');
const {publicUrl}=require('./gateway-network.cjs');

class ConnectorOAuthProvider{
  constructor({vault,id,redirectUrl,openExternal}){
    this.vault=vault;this.id=id;this.redirectUrl=redirectUrl;this.openExternal=openExternal;
    this.expectedState=randomBytes(24).toString('base64url');
    this.redirected=false;this.verifier=null;
  }
  get clientMetadata(){
    return {redirect_uris:[this.redirectUrl],client_name:'Bazino Marketing Studio gateway',
      grant_types:['authorization_code','refresh_token'],response_types:['code'],
      token_endpoint_auth_method:'none'};
  }
  state(){return this.expectedState;}
  clientInformation(){return this.vault.getConnectorOAuth(this.id,'clientInformation');}
  saveClientInformation(v){this.vault.saveConnectorOAuth(this.id,'clientInformation',v);}
  tokens(){return this.vault.getConnectorOAuth(this.id,'tokens');}
  saveTokens(v){this.vault.saveConnectorOAuth(this.id,'tokens',v);}
  discoveryState(){return this.vault.getConnectorOAuth(this.id,'discoveryState');}
  saveDiscoveryState(v){this.vault.saveConnectorOAuth(this.id,'discoveryState',v);}
  saveCodeVerifier(v){this.verifier=v;}
  codeVerifier(){if(!this.verifier)throw new Error('MCP OAuth verifier expired');return this.verifier;}
  invalidateCredentials(scope){
    if(['all','tokens'].includes(scope))this.saveTokens(undefined);
    if(['all','client'].includes(scope))this.saveClientInformation(undefined);
    if(['all','discovery'].includes(scope))this.saveDiscoveryState(undefined);
    if(['all','verifier'].includes(scope))this.verifier=null;
  }
  async redirectToAuthorization(url){
    if(!(url instanceof URL))throw new Error('Invalid MCP OAuth authorization URL');
    // code_challenge and state in an authorization URL are expected; actual codes,
    // tokens and client secrets are not. Only the owner's browser sees this URL.
    publicUrl(url.toString(),{allowSensitiveQuery:true});
    if([...url.searchParams.keys()].some(k=>/^(?:code|access_token|refresh_token|id_token|client_secret|api[_-]?key|password|session|authorization)$/i.test(k)))
      throw new Error('Unexpected secret in MCP OAuth authorization URL');
    this.redirected=true;
    await this.openExternal(url.toString());
  }
}
class GatewayMCP{
  constructor({vault,network,openExternal,port=59678}){
    this.vault=vault;this.network=network;this.openExternal=openExternal;this.port=port;
    this.sessions=new Map();
  }
  connector(id){
    const row=this.vault.connector(id);
    if(!row||row.kind!=='mcp')throw new Error('Register this MCP server in local Settings first');
    if(row.auth==='bearer'&&!row.credential)throw new Error('Enter the MCP access token on the owner’s Windows device');
    return row;
  }
  provider(id,interactive=false){
    return new ConnectorOAuthProvider({vault:this.vault,id,
      redirectUrl:`http://127.0.0.1:${this.port}/gateway-callback`,
      openExternal:interactive?this.openExternal:async()=>{
        throw new Error('MCP OAuth expired. Reconnect locally from the owner’s Windows app.');
      }});
  }
  async connect(row,provider){
    const transport=new StreamableHTTPClientTransport(new URL(row.url),{
      ...(row.auth==='bearer'?{requestInit:{headers:{Authorization:`Bearer ${row.credential}`}}}:{}),
      ...(provider?{authProvider:provider}:{}),fetch:this.network.fetch});
    const client=new Client({name:'bazino-marketing-studio-gateway',version:'0.2.0'});
    try{await client.connect(transport);return {client,transport};}
    catch(e){await client.close().catch(()=>{});throw e;}
  }
  async session(id){
    const row=this.connector(id),cached=this.sessions.get(id);
    if(cached)return cached.client;
    if(row.auth==='oauth'&&!this.vault.getConnectorOAuth(id,'tokens'))
      throw new Error('Authorize this MCP server from the owner’s local browser first');
    const connection=await this.connect(row,row.auth==='oauth'?this.provider(id):undefined);
    this.sessions.set(id,connection);
    return connection.client;
  }
  async closeConnector(id){
    const old=this.sessions.get(id);this.sessions.delete(id);
    if(old)await old.client.close().catch(()=>{});
  }
  async close(){for(const id of [...this.sessions.keys()])await this.closeConnector(id);}
  async authorize(id,{timeoutMs=180000}={}){
    const row=this.connector(id);
    if(row.auth!=='oauth')throw new Error('This MCP connector does not use OAuth');
    await this.closeConnector(id);
    // A deliberate owner login starts a fresh OAuth grant, even when old tokens
    // (with stale scopes) exist. A failed login leaves the connector disconnected.
    if(this.vault.getConnectorOAuth(id,'tokens'))this.vault.saveConnectorOAuth(id,'tokens',undefined);
    const provider=this.provider(id,true);
    const listener=oauthCallback({provider,port:this.port,timeoutMs,
      pathname:'/gateway-callback',label:'MCP'});
    try{
      await new Promise((resolve,reject)=>{
        listener.server.once('error',reject);
        listener.server.listen(this.port,'127.0.0.1',resolve);
      });
      let connection;
      try {connection=await this.connect(row,provider);}
      catch(e){
        if(!(e instanceof UnauthorizedError)||!provider.redirected)throw e;
        const code=await listener.code;
        const authTransport=new StreamableHTTPClientTransport(new URL(row.url),{
          authProvider:provider,fetch:this.network.fetch});
        await authTransport.finishAuth(code);
        await authTransport.close();
        connection=await this.connect(row,provider);
      }
      this.sessions.set(id,connection);
      const tools=await connection.client.listTools();
      return {ok:true,connectorId:id,tools:tools.tools.slice(0,100).map(t=>t.name),
        note:'OAuth completed on this device. Tool names are not proof of read-only behavior.'};
    }catch(e){await this.closeConnector(id);throw e;}
    finally{listener.close();}
  }
  async execute(op){
    const client=await this.session(op.connectorId);
    let result;
    switch(op.action){
      case 'tools':result=await client.listTools();break;
      case 'call':{
        const tools=await client.listTools();
        if(!tools.tools.some(t=>t.name===op.tool))throw new Error('Tool is not in the live MCP tools/list response');
        result=await client.callTool({name:op.tool,arguments:op.args});break;
      }
      case 'resources':result=await client.listResources();break;
      case 'read-resource':result=await client.readResource({uri:op.uri});break;
      case 'prompts':result=await client.listPrompts();break;
      case 'get-prompt':result=await client.getPrompt({name:op.name,arguments:op.args});break;
      default:throw new Error('Unsupported MCP operation');
    }
    return {ok:!result.isError,connectorId:op.connectorId,action:op.action,
      observedAt:new Date().toISOString(),body:result};
  }
}
module.exports={GatewayMCP,ConnectorOAuthProvider};
