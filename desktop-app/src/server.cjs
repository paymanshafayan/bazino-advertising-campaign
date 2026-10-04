'use strict';

// No Electron: this process serves a private UI to the owner's local browser.
// The Windows runner packages it with Node and a tiny PowerShell/ps2exe launcher.
const fs=require('node:fs');
const os=require('node:os');
const http=require('node:http');
const path=require('node:path');
const { pipeline }=require('node:stream/promises');
const { Transform }=require('node:stream');
const { randomBytes,randomUUID,timingSafeEqual }=require('node:crypto');
const { execFileSync,spawn }=require('node:child_process');
const { Vault,normalizeConnector,publicConnector }=require('./vault.cjs');
const { windowsStorage }=require('./windows-dpapi.cjs');
const { Assets }=require('./assets.cjs');
const { Providers }=require('./providers.cjs');
const { Kling }=require('./kling.cjs');
const { Gateway }=require('./gateway.cjs');
const { Relay }=require('./relay.cjs');
const { Research,FEEDS }=require('./research.cjs');
const { EditorialStore }=require('./editorial.cjs');
const { validateOperation,isMutating,redact }=require('./protocol.cjs');
const { BUILD_ID,createDiagnostics,failureReason }=require('./diagnostics.cjs');
const { REPO,BRANCH,PORTAL_MEDIA_STUDIO,KLING_ENDPOINT }=require('./constants.cjs');
const { HOST,PORT }=require('./server-address.cjs');

const MAX_ASSET=512*1024*1024;
const ALLOWED_EXT=new Set(['.png','.jpg','.jpeg','.webp','.mp4','.mov','.m4v','.pdf']);
function urlSafe(raw){
  const url=new URL(raw);
  if(url.href===PORTAL_MEDIA_STUDIO)return url.href; // portal admin login, NOT a vault-value read
  const researchHosts=FEEDS.map(feed=>feed.host);
  if(url.protocol!=='https:' || !['kling.ai','github.com','docs.zernio.com',
    ...researchHosts,...researchHosts.map(host=>`www.${host}`),'www.reddit.com','www.youtube.com'].includes(url.hostname) ||
    url.username || url.password || /(?:code|token|secret|key|auth)/i.test(url.search))
    throw new Error('Only approved tool pages without credential parameters can be opened');
  return url.href;
}
function pwsh(script,input,timeout=120000) {
  const encoded=Buffer.from(script,'utf16le').toString('base64');
  return execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-STA','-EncodedCommand',encoded],{
    input,encoding:'utf8',windowsHide:true,timeout,maxBuffer:128*1024,stdio:['pipe','pipe','pipe']
  });
}
function openBrowser(url) {
  if(!url.startsWith('https://') && !url.startsWith(`http://${HOST}:${PORT}/`))
    throw new Error('Unexpected browser URL');
  const script=`$u=[Console]::In.ReadToEnd();Start-Process -FilePath $u`;
  pwsh(script,url,15000);
}
function messageBox({title,message}) {
  if(process.platform!=='win32')throw new Error('A Windows desktop confirmation is required');
  const script=String.raw`
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$p = [Console]::In.ReadToEnd() | ConvertFrom-Json
$r = [System.Windows.MessageBox]::Show($p.message,$p.title,'YesNo','Warning','No')
[Console]::Out.Write([string]$r)
`;
  try {return pwsh(script,JSON.stringify({title,message}),125000).trim()==='Yes';}
  catch {return false;} // timeout/closed window means deny
}
function operationPreview(op,vault) {
  const endpoint=['gateway-api','gateway-mcp'].includes(op.kind)?vault.connector(op.connectorId)?.url:undefined;
  const p=op.kind==='api'?{tool:op.provider,method:op.method,path:op.path,body:op.body}:
    op.kind==='gateway-api'?{tool:'Registered API',connector:op.connectorId,endpoint,
      method:op.method,path:op.path,body:op.body}:
    op.kind==='gateway-mcp'?{tool:'Registered MCP',connector:op.connectorId,endpoint,
      action:op.action,name:op.tool||op.name,uri:op.uri,args:op.args}:
    op.kind==='kling'?{tool:'Kling MCP',command:op.command,args:op.args}:
    op.kind==='editorial'?{tool:'Local research report',title:op.report?.title,game:op.report?.game,
      sources:op.report?.sources?.length,action:op.action}:
    {tool:op.kind,...op};
  return JSON.stringify(redact(p,0,vault.allSecrets()));
}
function safeError(e,vault) {
  let text=redact(String(e?.message||e),0,vault.allSecrets());
  // OAuth and provider errors can contain a sensitive query string; never return one to UI/log.
  text=text.replace(/https?:\/\/[^\s"']+/g,'[url-redacted]');
  return text.slice(0,300);
}
function textResponse(res,status,body,kind='application/json; charset=utf-8') {
  res.writeHead(status,{'Content-Type':kind,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff',
    'X-Bazino-App':'marketing-studio','Referrer-Policy':'no-referrer','X-Frame-Options':'DENY',
    'Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'none'"});
  res.end(kind.startsWith('application/json')?JSON.stringify(body):body);
}
function readJSON(req,limit=128*1024) {
  return new Promise((resolve,reject)=>{
    let size=0,buf='';
    req.on('data',chunk=>{size+=chunk.length;if(size>limit){reject(new Error('Request too large'));req.destroy();}
      else buf+=chunk.toString('utf8');});
    req.on('end',()=>{try{resolve(JSON.parse(buf));}catch{reject(new Error('Invalid JSON'));}});
    req.on('error',reject);
  });
}
function authorized(req,token,port) {
  const header=req.headers['x-bazino-session'];
  if(typeof header!=='string')return false;
  const candidate=Buffer.from(header),expected=Buffer.from(token);
  return candidate.length===expected.length && timingSafeEqual(candidate,expected) &&
    req.headers.origin===`http://${HOST}:${port}` &&
    req.headers['sec-fetch-site']!=='cross-site';
}
async function uploadAsset(req,assets,root) {
  if(req.headers['content-type']!=='application/octet-stream')throw new Error('Expected a media file');
  const declared=Number(req.headers['content-length']);
  if(!Number.isSafeInteger(declared)||declared<1||declared>MAX_ASSET)throw new Error('Unsupported asset size');
  let display;
  try {display=decodeURIComponent(req.headers['x-bazino-filename']||'');}catch{throw new Error('Invalid filename');}
  display=path.basename(display);
  const ext=path.extname(display).toLowerCase();
  if(!display||display.length>200||!ALLOWED_EXT.has(ext))throw new Error('Unsupported asset type');
  const dir=path.join(root,'uploads');fs.mkdirSync(dir,{recursive:true,mode:0o700});
  const tmp=path.join(dir,`${randomUUID()}${ext}`);
  let bytes=0;
  try {
    await pipeline(req,new Transform({transform(chunk,_encoding,next){
      bytes+=chunk.length;
      if(bytes>MAX_ASSET||bytes>declared)next(new Error('Asset size mismatch'));else next(null,chunk);
    }}),fs.createWriteStream(tmp,{flags:'wx',mode:0o600}));
    if(bytes!==declared)throw new Error('Incomplete upload');
    return assets.import(tmp,display);
  }finally{try{fs.unlinkSync(tmp);}catch{}}
}
function createService({baseDir,storage=windowsStorage,approval=messageBox,launch=openBrowser,
  klingFactory=options=>new Kling(options),providersFactory=options=>new Providers(options),
  gatewayFactory=options=>new Gateway(options),researchFactory=options=>new Research(options),
  onStop,port=PORT,tabCloseGraceMs=5000}={}) {
  if(!baseDir)throw new Error('Local data directory required');
  const vault=new Vault(path.join(baseDir,'vault.json'),storage);vault.load();
  const assets=new Assets(path.join(baseDir,'assets'));
  const providers=providersFactory({vault,assets});
  const research=researchFactory({});
  const editorial=new EditorialStore(path.join(baseDir,'editorial.json'),storage);
  const kling=klingFactory({vault,assets,openExternal:launch});
  const gateway=gatewayFactory({vault,openExternal:launch});
  let relay;
  const session=randomBytes(32).toString('base64url');
  const history=[];
  const diagnostics=createDiagnostics();
  const startedAt=new Date().toISOString();
  let lastSeq=0;
  function event(message,type='info'){
    const row={seq:++lastSeq,at:new Date().toISOString(),message:String(message).slice(0,120),type};
    history.unshift(row);if(history.length>100)history.pop();
  }
  async function approve(op,origin){
    const preview=operationPreview(op,vault);
    if(preview.length>1700) {
      event('An operation was declined: confirmation preview too long','error');return false;
    }
    return approval({title:'Bazino: تأیید همین عملیات',message:
      `${origin==='relay'?'درخواست ایجنت از GitHub':'درخواست از مرورگر محلی'}\n\n${preview}\n\n`+
      'انتشار/تولید/ارسال/هزینه فقط با تصمیم شما. آیا دقیقاً این درخواست را تأیید می‌کنید؟'});
  }
  async function execute(input,origin='local'){
    const op=validateOperation(input);
    if(origin==='relay'&&['gateway-api','gateway-mcp'].includes(op.kind)){
      const row=vault.connector(op.connectorId);
      if(!row||row.kind!==(op.kind==='gateway-api'?'api':'mcp')||
        row.agentAccess==='none'||isMutating(op)&&row.agentAccess!=='approved-writes'){
        diagnostics.add('operation','OP_DECLINED',{kind:op.kind,reason:'DECLINED'});
        return {ok:false,error:'CONNECTOR_NOT_SHARED_WITH_AGENT'};
      }
    }
    // Saving a local research note via the browser is not an external action. A remote
    // agent's write still needs the native Windows confirmation in the relay.
    if(origin==='local'&&isMutating(op)&&op.kind!=='editorial'){
      const before=['gateway-api','gateway-mcp'].includes(op.kind)?vault.connector(op.connectorId):null;
      if(!(await approve(op,origin))){
        diagnostics.add('operation','OP_DECLINED',{kind:op.kind,reason:'DECLINED'});
        return {ok:false,error:'DECLINED_BY_OWNER'};
      }
      if(before&&vault.connector(op.connectorId)!==before){
        diagnostics.add('operation','OP_DECLINED',{kind:op.kind,reason:'DECLINED'});
        return {ok:false,error:'CONNECTOR_CHANGED_DURING_APPROVAL'};
      }
    }
    event(`${origin} ${op.kind==='api'?op.provider+' '+op.method:op.kind}`,'work');
    diagnostics.add('operation','OP_STARTED',{kind:op.kind});
    try {
      let result;
      if(op.kind==='api')result=await providers.api(op);
      else if(op.kind==='zernio-media-upload')result=await providers.uploadZernioMedia(op);
      else if(op.kind==='zernio-access')result={ok:true,...await providers.access()};
      else if(op.kind==='zernio-engagement-check')result=await providers.engagementAccess(op.accountId);
      else if(op.kind==='research')result=await research.scan(op.sourceIds);
      else if(op.kind==='editorial')result=op.action==='list'?
        {ok:true,reports:editorial.list()}:{ok:true,report:editorial.upsert(op.report)};
      else if(op.kind==='gateway-list')result={ok:true,connectors:vault.listConnectors()
        .filter(row=>origin!=='relay'||row.agentAccess!=='none')};
      else if(op.kind==='web-fetch')result=await gateway.readPage(op);
      else if(op.kind==='gateway-api')result=await gateway.request(op);
      else if(op.kind==='gateway-mcp')result=await gateway.executeMcp(op);
      else result=await kling.execute(op);
      if(op.kind==='api'&&result.ok&&op.provider==='zernio'&&op.method==='POST'&&
        op.path.split('?')[0]==='/v1/posts'&&op.body?.metadata?.bazinoTopicId&&result.body?.post?._id) {
        try {editorial.recordPost(op.body.metadata.bazinoTopicId,result.body.post,op.body.platforms);}
        catch {event('Post accepted; local tracking failed — inspect Zernio before retry','error');
          result.trackingWarning='Post response received, but local report could not be updated';}
      }
      event(`${op.kind} ${result.ok?'completed':'provider error'}`,result.ok?'success':'error');
      const failedFeeds=op.kind==='research'&&Array.isArray(result.sources)?
        result.sources.filter(source=>source.error):[];
      const partial=Boolean(result.ok&&(result.status===207||failedFeeds.length));
      const feedError=failedFeeds.find(source=>/fetch failed|network|timeout/i.test(String(source.error)))?.error;
      const reason=failedFeeds.length?(feedError?'NETWORK':'OTHER'):
        result.ok?undefined:failureReason({message:result.error});
      diagnostics.add('operation',result.ok?(partial?'OP_PARTIAL':'OP_DONE'):'OP_FAILED',
        {kind:op.kind,status:result.status,reason});
      return redact(result,0,vault.allSecrets());
    }catch(e){
      event(`${op.kind} failed`,'error');
      diagnostics.add('operation','OP_FAILED',{kind:op.kind,reason:failureReason(e)});
      return {ok:false,error:safeError(e,vault),
      ...(op.kind==='api'&&op.path.split('?')[0]==='/v1/posts'&&op.idempotencyKey?
        {idempotencyKey:op.idempotencyKey}:{} )};}
  }
  relay=new Relay({vault,service:op=>execute(op,'relay'),approve:op=>approve(op,'relay'),
    statePath:path.join(baseDir,'relay-state.json')});
  relay.on('error',e=>{
    event('GitHub relay error; check connection','error');
    diagnostics.add('relay','RELAY_FAILED',{reason:failureReason(e)});
  });
  relay.on('activity',a=>event(`Remote ${a.status||'request'}`,'info'));
  relay.on('status',s=>{
    event(s.connected?'GitHub relay connected':'GitHub relay disconnected');
    diagnostics.add('relay',s.connected?'RELAY_CONNECTED':'RELAY_DISCONNECTED');
  });
  const uiTabs=new Set();
  let tabCloseTimer=null,stopTimer=null,stopping=false,closePromise=null;
  function tabKey(arg){
    const id=arg?.tabId;
    if(typeof id!=='string'||!/^[a-zA-Z0-9_-]{8,100}$/.test(id))
      throw new Error('Invalid local browser tab');
    return id;
  }
  function cancelTabClose(){if(tabCloseTimer){clearTimeout(tabCloseTimer);tabCloseTimer=null;}}
  function scheduleTabStop(){
    cancelTabClose();
    tabCloseTimer=setTimeout(()=>{
      tabCloseTimer=null;
      if(uiTabs.size)return;
      // Some browsers navigate the same tab to Kling's login. Keep its loopback
      // listener alive until the pending OAuth flow ends, then release the port.
      if(kling.activeCallback){scheduleTabStop();return;}
      requestStop(0);
    },tabCloseGraceMs);
  }
  function requestStop(delayMs){
    if(!onStop||stopping)return;
    stopping=true;
    stopTimer=setTimeout(()=>{
      stopTimer=null;
      Promise.resolve().then(onStop).catch(()=>{console.error('Local server shutdown failed');});
    },delayMs);
  }
  async function dispatch(action,arg){
    switch(action){
      case 'startup':{
        if(arg?.tabId){uiTabs.add(tabKey(arg));cancelTabClose();}
        return {settings:vault.publicSettings(),assets:assets.list(),history,lastSeq,
          buildId:BUILD_ID,connectors:vault.listConnectors(),
          researchFeeds:FEEDS.map(({id,name,region})=>({id,name,region})),
          editorialCount:editorial.list().length,
          relay:{connected:relay.connected},repo:REPO,branch:BRANCH,endpoint:KLING_ENDPOINT};
      }
      case 'ui:leave':{
        const id=tabKey(arg),wasOpen=uiTabs.delete(id);
        if(wasOpen&&uiTabs.size===0&&onStop&&!stopping)scheduleTabStop();
        return {ok:true};
      }
      case 'settings:save':return vault.update(arg);
      case 'gateway:list':return {ok:true,connectors:vault.listConnectors()};
      case 'gateway:save':{
        const candidate=normalizeConnector(arg,vault.connector(arg?.id));
        const preview=publicConnector(candidate);
        if(!(await approval({title:'ثبت دسترسی اینترنتیِ جدید',message:
          `سرویس ${preview.name} (${preview.kind})\n${preview.url}\nروش ورود: ${preview.auth}\n`+
          `دسترسی ایجنت: ${preview.agentAccess}\n\n`+
          'راز فقط در Vault محلی Windows می‌ماند. در صورت فعال کردن دسترسی ایجنت، درخواست‌های خواندنی ممکن است خودکار اجرا شوند؛ ارسال/هزینه/MCP tool call تأیید جدا می‌خواهد. آیا این مقصد دقیق را ثبت می‌کنید؟'})))
          return {ok:false,error:'DECLINED_BY_OWNER'};
        await gateway.changed(candidate.id);
        return {ok:true,connector:vault.saveConnector(arg),connectors:vault.listConnectors()};
      }
      case 'gateway:remove':{
        const row=vault.connector(arg);
        if(!row)throw new Error('Unknown connector');
        if(!(await approval({title:'حذف اتصال',message:
          `آیا ${row.name} (${row.url}) و کلید/OAuth محلیِ همین اتصال حذف شوند؟` })))
          return {ok:false,error:'DECLINED_BY_OWNER'};
        await gateway.changed(arg);vault.removeConnector(arg);
        return {ok:true,connectors:vault.listConnectors()};
      }
      case 'gateway:authorize':{
        const row=vault.connector(arg);
        if(!row||row.kind!=='mcp'||row.auth!=='oauth')throw new Error('Register an OAuth MCP server first');
        if(!(await approval({title:'OAuth سرویس MCP',message:
          `ورود در مرورگر خودتان برای MCP ثبت‌شده:\n${row.url}\n\n`+
          'سرور OAuth ممکن است دامنهٔ ورود جدا داشته باشد؛ آدرس مرورگر و مجوزها را خودتان بررسی کنید. آیا ورود آغاز شود؟'})))
          return {ok:false,error:'DECLINED_BY_OWNER'};
        return redact(await gateway.authorizeMcp(arg),0,vault.allSecrets());
      }
      case 'operation:run':return execute(arg);
      case 'relay:connect':return relay.connect();
      case 'relay:disconnect':relay.disconnect();return {connected:false};
      case 'relay:pair':{
        const candidate=await relay.candidateAgent();
        const yes=await approval({title:'تأیید اثر انگشت ایجنت',
          message:`اثر انگشت زیر را فقط با خروجی مستقیم agent-client.cjs pair مقایسه کنید:\n\n${candidate.fingerprint}\n\nاگر دقیقاً برابرند، تأیید کنید.`});
        if(!yes)return {paired:false};
        const settings=vault.pairAgent(candidate.publicKey);
        if(relay.connected)await relay.poll();
        return {paired:true,fingerprint:candidate.fingerprint,settings};
      }
      case 'assets:open':{
        const target=assets.find(arg).path;
        if(process.platform==='win32')spawn('explorer.exe',[`/select,${target}`],{detached:true,windowsHide:true}).unref();
        return true;
      }
      case 'kling:authorize':return redact(await kling.authorize(),0,vault.allSecrets());
      case 'kling:identity':return {ok:true,body:redact(await kling.identity(),0,vault.allSecrets())};
      case 'kling:tools':return kling.listTools();
      case 'kling:logout':{
        if(!(await approval({title:'قطع اتصال Kling',message:'توکن‌های OAuth برنامه از همین رایانه حذف شوند؟'})))
          return {ok:false,error:'DECLINED_BY_OWNER'};
        return kling.logout();
      }
      case 'external:open':launch(urlSafe(arg));return {ok:true};
      case 'app:stop':{
        if(!onStop)throw new Error('Stop is unavailable in this process');
        cancelTabClose();requestStop(250);return {ok:true};
      }
      case 'events':return {rows:history.filter(row=>row.seq>(Number(arg)||0)).reverse(),lastSeq,
        relay:{connected:relay.connected},settings:vault.publicSettings()};
      case 'diagnostics':return {buildId:BUILD_ID,startedAt,
        relayConnected:relay.connected,rows:diagnostics.list()};
      default:throw new Error('Unknown action');
    }
  }
  let server;
  function handler(req,res) {
    const actualPort=server.address()?.port??port;
    const address=`${HOST}:${actualPort}`;
    if(req.headers.host!==address){textResponse(res,403,{error:'Host rejected'});return;}
    let pathname;
    try {pathname=new URL(req.url||'/',`http://${address}`).pathname;}
    catch {textResponse(res,400,{error:'Invalid path'});return;}
    if(req.method==='GET'&&['/','/ui.js','/style.css','/client.js'].includes(pathname)){
      const file=pathname==='/'?'index.html':pathname.slice(1);
      const type=file.endsWith('.js')?'text/javascript; charset=utf-8':file.endsWith('.css')?
        'text/css; charset=utf-8':'text/html; charset=utf-8';
      let body=fs.readFileSync(path.join(__dirname,file),'utf8');
      if(file==='index.html')body=body.replace('</head>',`<meta name="bazino-session" content="${session}"></head>`);
      textResponse(res,200,body,type);return;
    }
    if(req.method!=='POST'||!authorized(req,session,actualPort)){
      textResponse(res,403,{error:'Local session required'});return;
    }
    if(pathname==='/api/assets/import'){
      uploadAsset(req,assets,baseDir).then(result=>{
        diagnostics.add('asset','ASSET_IMPORT_DONE');
        textResponse(res,200,result);
      }).catch(e=>{
          diagnostics.add('asset','ASSET_IMPORT_FAILED',{reason:failureReason(e)});
          if(!res.destroyed)textResponse(res,400,{error:safeError(e,vault)});
        });
      return;
    }
    if(pathname!=='/api/dispatch'||req.headers['content-type']!=='application/json'){
      textResponse(res,404,{error:'Not found'});return;
    }
    let attemptedAction;
    readJSON(req).then(({action,arg})=>{
      attemptedAction=action;
      return dispatch(action,arg);
    }).then(result=>{
      if(!['startup','events','diagnostics','ui:leave','operation:run'].includes(attemptedAction)){
        diagnostics.add('server',result?.ok===false?'REQUEST_FAILED':'REQUEST_DONE',{
          action:attemptedAction,
          reason:result?.ok===false?failureReason({message:result.error}):undefined
        });
      }
      if(!res.destroyed)textResponse(res,200,result);
    }).catch(e=>{
        // Only an allowlisted action label and a fixed failure category reach the
        // copyable log. Never store arg, session, URL, raw error or stack here.
        diagnostics.add('server','REQUEST_FAILED',
          {action:attemptedAction,reason:failureReason(e)});
        if(!res.destroyed)textResponse(res,400,{error:safeError(e,vault)});
      });
  }
  server=http.createServer(handler);
  return {server,dispatch,handler,vault,assets,relay,kling,gateway,session,
    listen:()=>new Promise((resolve,reject)=>{
      server.once('error',reject);
      server.listen(port,HOST,()=>{
        diagnostics.add('server','SERVER_READY');
        resolve();
      });
    }),
    close:()=>{
      if(closePromise)return closePromise;
      stopping=true;cancelTabClose();clearTimeout(stopTimer);
      closePromise=(async()=>{
        // Stop accepting requests first. An in-progress OAuth/long poll must not
        // retain this port indefinitely after the owner closes the application.
        const closed=server.listening?new Promise((resolve,reject)=>{
          const forceTimer=setTimeout(()=>server.closeAllConnections?.(),3000);
          server.close(error=>{
            clearTimeout(forceTimer);
            if(error)reject(error);else resolve();
          });
          server.closeIdleConnections?.();
        }):Promise.resolve();
        relay.disconnect();
        await Promise.allSettled([kling.close(),gateway.close()]);
        await closed;
      })();
      return closePromise;
    }};
}
function monitorLauncher(args,onMissing,{probe=pid=>process.kill(pid,0),intervalMs=2000}={}){
  const flag=args.find(arg=>/^--bazino-launcher-pid=[1-9][0-9]*$/.test(arg));
  if(!flag)return null; // npm start / developer mode has no Windows launcher.
  const pid=Number(flag.split('=')[1]);
  if(!Number.isSafeInteger(pid))throw new Error('Invalid Windows launcher PID');
  const timer=setInterval(()=>{
    try{probe(pid);}
    catch(e){
      if(e.code==='ESRCH'){
        clearInterval(timer);
        onMissing(); // the owner closed the launcher; release the server port
      }
    }
  },intervalMs);
  timer.unref();
  return timer;
}
async function main(){
  if(process.argv.includes('--self-test')){
    if(PORT<1024||!fs.existsSync(path.join(__dirname,'index.html'))||!KLING_ENDPOINT.startsWith('https://'))
      throw new Error('App packaging self-test failed');
    process.stdout.write('Bazino marketing app: self-test OK\n');return;
  }
  if(process.platform!=='win32')throw new Error('This credential-protected app runs on the owner’s Windows computer.');
  const root=path.join(process.env.APPDATA||os.homedir(),'BazinoMarketingBrowser');
  let stopping;
  const stop=()=>{
    if(stopping)return stopping;
    stopping=service.close().then(()=>process.exit(0)).catch(()=>{
      console.error('Could not close local studio cleanly');process.exit(1);
    });
    return stopping;
  };
  const service=createService({baseDir:root,onStop:stop});
  await service.listen();
  monitorLauncher(process.argv.slice(2),stop);
  try{openBrowser(`http://${HOST}:${PORT}/`);}
  catch(e){await service.close();throw e;}
  process.stdout.write(`Bazino Marketing Studio listening only on ${HOST}:${PORT}\n`);
  process.on('SIGINT',stop);
  process.on('SIGTERM',stop);
}
if(require.main===module)main().catch(e=>{console.error(e.message);process.exitCode=1;});
module.exports={createService,authorized,uploadAsset,operationPreview,monitorLauncher,PORT,HOST};
