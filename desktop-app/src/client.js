/* Private local-browser RPC. All URLs are RELATIVE; secrets are never in the address bar. */
'use strict';
(() => {
  const session=document.querySelector('meta[name="bazino-session"]')?.content;
  if (!session) throw new Error('Local session missing. Open the Windows application, not this HTML file.');
  const headers={'Content-Type':'application/json','X-Bazino-Session':session};
  // A browser tab is the lifetime of this local app. Closing its last tab should
  // release the server port; a reload gets a short grace period to reconnect.
  const tabId=globalThis.crypto?.randomUUID?.()||`${Date.now()}-${Math.random().toString(16).slice(2)}`;
  let cursor=0,activityCallback=null,relayCallback=null,polling=false,closing=false;
  window.addEventListener('pagehide',event=>{
    if(event.persisted||closing)return;
    closing=true;
    // keepalive survives a normal browser tab/window close. It is same-origin and
    // still requires the session header; never put the session in a URL or cookie.
    fetch('/api/dispatch',{method:'POST',headers,credentials:'same-origin',keepalive:true,
      body:JSON.stringify({action:'ui:leave',arg:{tabId}})}).catch(()=>{});
  });
  async function call(action,arg){
    const response=await fetch('/api/dispatch',{method:'POST',headers,
      credentials:'same-origin',body:JSON.stringify({action,arg})});
    const body=await response.json();
    if(!response.ok)throw new Error(body.error||'Local request failed');
    if(action==='startup')cursor=Math.max(cursor,body.lastSeq||0);
    return body;
  }
  async function events(){
    if(closing||polling||(!activityCallback&&!relayCallback))return;
    polling=true;
    try{
      const answer=await call('events',cursor);
      cursor=answer.lastSeq;
      for(const item of answer.rows)activityCallback?.(item);
      relayCallback?.(answer.relay);
    }catch{ /* another try after reconnection; never log secrets */ }
    finally{polling=false;}
  }
  setInterval(events,2500);
  async function importAsset(){
    return new Promise((resolve,reject)=>{
      const input=document.createElement('input');
      input.type='file';input.accept='.png,.jpg,.jpeg,.webp,.mp4,.mov,.m4v,.pdf';
      input.style.display='none';document.body.append(input);
      input.addEventListener('change',async()=>{
        const file=input.files?.[0];input.remove();
        if(!file){resolve(null);return;}
        try{
          const res=await fetch('/api/assets/import',{method:'POST',credentials:'same-origin',
            headers:{'Content-Type':'application/octet-stream','X-Bazino-Session':session,
              'X-Bazino-Filename':encodeURIComponent(file.name)},body:file});
          const json=await res.json();
          if(!res.ok)throw new Error(json.error||'Upload failed');
          resolve(json);
        }catch(e){reject(e);}
      },{once:true});
      // Cancellation varies across browsers. Resolve if the picker is dismissed.
      input.addEventListener('cancel',()=>{input.remove();resolve(null);},{once:true});
      input.click();
    });
  }
  window.marketing=Object.freeze({
    startup:()=>call('startup',{tabId}),saveSettings:p=>call('settings:save',p),
    listConnectors:()=>call('gateway:list'),saveConnector:p=>call('gateway:save',p),
    removeConnector:id=>call('gateway:remove',id),
    authorizeConnector:id=>call('gateway:authorize',id),
    run:p=>call('operation:run',p),connectRelay:()=>call('relay:connect'),
    disconnectRelay:()=>call('relay:disconnect'),pairAgent:()=>call('relay:pair'),
    importAsset,openAsset:id=>call('assets:open',id),
    authorizeKling:()=>call('kling:authorize'),klingIdentity:()=>call('kling:identity'),
    klingTools:()=>call('kling:tools'),logoutKling:()=>call('kling:logout'),
    openWebsite:url=>call('external:open',url),stopApp:async()=>{
      const result=await call('app:stop');closing=true;return result;
    },
    onActivity:cb=>{activityCallback=cb;events();},
    onRelay:cb=>{relayCallback=cb;events();}
  });
})();
