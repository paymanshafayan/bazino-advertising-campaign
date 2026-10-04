"use strict";

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => Array.from(document.querySelectorAll(selector));
let settings = {},
  assets = [],
  activity = [],
  relayConnected = false,
  toastTimer,
  composerKey = null,
  composerBody = null,
  explorerPostSnapshot = null,
  explorerPostKey = null,
  access = null,
  reports = [],
  engagement = null,
  connectors = [];
const titles = {
  overview: "نمای کلی",
  intelligence: "رصد و پژوهش روزانه",
  zernio: "مرکز Zernio",
  explorer: "API Explorer",
  gateway: "گیت دسترسی آنلاین",
  media: "کتابخانه رسانه",
  kling: "Kling AI",
  relay: "ارتباط GitHub",
  settings: "تنظیمات و کلیدها",
};
const presets = {
  "zernio-profiles": {
    provider: "zernio",
    method: "GET",
    path: "/v1/profiles",
  },
  "zernio-accounts": {
    provider: "zernio",
    method: "GET",
    path: "/v1/accounts",
  },
  "zernio-posts": { provider: "zernio", method: "GET", path: "/v1/posts" },
  "zernio-analytics": { provider: "zernio", method: "GET", path: "/v1/analytics" },
  "zernio-comments": { provider: "zernio", method: "GET", path: "/v1/inbox/comments?limit=25" },
  "zernio-automations": { provider: "zernio", method: "GET", path: "/v1/comment-automations" },
  "zernio-reddit": { provider: "zernio", method: "GET", path: "/v1/reddit/search?accountId=CONNECTED_REDDIT_ID&q=gaming" },
  "flux-image": () => ({
    provider: "cloudflare",
    method: "POST",
    path: `/accounts/${settings.cloudflareAccountId || "YOUR_ACCOUNT_ID"}/ai/run/@cf/black-forest-labs/flux-1-schnell`,
    body: { prompt: "A cinematic gaming lounge campaign visual, no text" },
  }),
};
function toast(message, error = false) {
  const el = $("#toast");
  el.textContent = String(message).slice(0, 350);
  el.classList.toggle("error", error);
  el.classList.add("visible");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove("visible"), 4500);
}
function go(view) {
  if (!titles[view]) return;
  $$(".view").forEach((el) =>
    el.classList.toggle("active", el.id === `view-${view}`),
  );
  $$("[data-nav]").forEach((el) =>
    el.classList.toggle("active", el.dataset.nav === view),
  );
  $("#page-title").textContent = titles[view];
  window.scrollTo(0, 0);
}
function isConfigured(field) {
  return Boolean(settings.configured?.[field]);
}
function paintSettings() {
  $$("[data-setting]").forEach((el) => {
    const val = settings[el.dataset.setting];
    if (el.type === "checkbox") el.checked = !!val;
    else el.value = val ?? "";
  });
  $$("[data-secret]").forEach((el) => {
    const name = el.dataset.secret;
    el.value = "";
    el.placeholder = isConfigured(name)
      ? "●●●●●● ذخیره‌شده"
      : "کلید را وارد کنید";
    const parent = el.closest(".field");
    let clear = parent.querySelector(".clear-secret");
    if (isConfigured(name) && !clear) {
      clear = document.createElement("button");
      clear.type = "button";
      clear.className = "clear-secret";
      clear.textContent = "حذف کلید ذخیره‌شده";
      clear.addEventListener("click", async () => {
        if (!window.confirm("این کلید از تنظیمات محلی حذف شود؟")) return;
        try {
          settings = await window.marketing.saveSettings({
            clearSecrets: [name],
          });
          if(name==='zernioKey'){access=null;engagement=null;paintAccess();}
          paintSettings();
          toast("کلید از رایانه حذف شد.");
        } catch (e) {
          toast(e.message, true);
        }
      });
      parent.append(clear);
    } else if (!isConfigured(name) && clear) {
      clear.remove();
    }
  });
  $$("[data-config]").forEach((el) => {
    const good = isConfigured(el.dataset.config);
    el.textContent = good ? "دسترسی ثبت شده ✓" : "نیاز به تنظیم دسترسی";
    el.classList.toggle("ready", good);
  });
  $("#kling-card-status").textContent = settings.klingAuthorized
    ? "OAuth محلی ذخیره‌شده · هویت را بررسی کنید"
    : "نیاز به OAuth مرورگر";
  $("#agent-fingerprint").textContent =
    settings.agentFingerprint || "هنوز جفت نشده؛ درخواست‌ها اجرا نمی‌شوند";
  $("#read-policy").textContent = settings.autoApproveReads
    ? "خودکار پس از جفت‌سازی"
    : "تأیید هر مورد";
  $("#write-policy").textContent = settings.autoApproveWrites
    ? "خودکار (پرریسک)"
    : "تأیید هر مورد";
  $("#kling-setup-status").textContent = settings.klingAuthorized
    ? "توکن روی همین دستگاه ذخیره شده؛ برای اثبات اتصال who_am_i را اجرا کنید."
    : "OAuth و who_am_i هنوز تأیید نشده‌اند.";
}
function paintRelay(s) {
  relayConnected = !!s.connected;
  $("#relay-badge").textContent = relayConnected ? "متصل" : "قطع";
  $("#relay-badge").classList.toggle("connected", relayConnected);
  $("#sidebar-status").textContent = relayConnected
    ? "ارتباط فعال"
    : "ارتباط غیرفعال";
  $("#sidebar-dot").classList.toggle("connected", relayConnected);
  $("#top-status").textContent = relayConnected
    ? "● ارتباط امن فعال"
    : "● آماده برای تنظیم";
  $("#top-status").classList.toggle("connected", relayConnected);
  const f = s.fingerprint || settings.identityFingerprint;
  if (f) $("#relay-fingerprint").textContent = f;
}
function paintAssets() {
  const list = $("#asset-list"),
    select = $("#selected-asset");
  list.replaceChildren();
  select.replaceChildren();
  if (!assets.length) {
    const p = document.createElement("p");
    p.className = "empty";
    p.textContent = "هنوز فایلی اضافه نشده است.";
    list.append(p);
    const o = new Option("ابتدا فایل اضافه کنید", "");
    select.append(o);
    return;
  }
  for (const asset of assets) {
    const row = document.createElement("div");
    row.className = "asset-row";
    const icon = document.createElement("span");
    icon.className = "asset-icon";
    icon.textContent = asset.mime.startsWith("video/")
      ? "▶"
      : asset.mime.includes("pdf")
        ? "▣"
        : "▧";
    const meta = document.createElement("div");
    const title = document.createElement("b");
    title.textContent = asset.name;
    const small = document.createElement("small");
    small.textContent = `${(asset.bytes / 1048576).toFixed(2)} MB · ${asset.id}`;
    meta.append(title, small);
    const open = document.createElement("button");
    open.textContent = "نمایش فایل";
    open.addEventListener("click", () =>
      window.marketing.openAsset(asset.id).catch((e) => toast(e.message, true)),
    );
    row.append(icon, meta, open);
    list.append(row);
    select.append(
      new Option(`${asset.name} · ${asset.id.slice(0, 8)}`, asset.id),
    );
  }
}
function paintActivity() {
  const box = $("#activity-list");
  box.replaceChildren();
  if (!activity.length) {
    const p = document.createElement("p");
    p.className = "empty";
    p.textContent = "هنوز عملیاتی انجام نشده است.";
    box.append(p);
    return;
  }
  for (const item of activity.slice(0, 9)) {
    const row = document.createElement("div");
    row.className = `activity-row ${item.type === "error" ? "error" : ""}`;
    const icon = document.createElement("span");
    icon.className = "activity-type";
    icon.textContent =
      item.type === "error" ? "×" : item.type === "success" ? "✓" : "◦";
    const label = document.createElement("span");
    label.textContent = item.message;
    const time = document.createElement("time");
    time.textContent = new Date(item.at).toLocaleTimeString("fa-IR", {
      hour: "2-digit",
      minute: "2-digit",
    });
    row.append(icon, label, time);
    box.append(row);
  }
}
function accountOptions(selector, rows, label) {
  const select=$(selector),selected=select.value;
  select.replaceChildren(new Option(label,''));
  for(const row of rows)select.add(new Option(
    `${row.platform} · ${row.username||'account'} · ${row.accountId}`,row.accountId));
  if(rows.some(row=>row.accountId===selected))select.value=selected;
}
function paintAccess() {
  const rows=access?.accounts||[];
  const box=$('#access-accounts');box.replaceChildren();
  $('#access-status').textContent=access?
    `${rows.length} حساب بررسی شد · ${access.checkedAt} · آمار: ${access.hasAnalyticsAccess?'دسترسی حساب موجود است':'طرح/مجوز تأیید نشده'}`:
    'حساب/مجوزی هنوز تأیید نشده است؛ قبل از ساخت پست یا تعامل، بررسی زنده لازم است.';
  for(const row of rows) {
    const div=document.createElement('div');div.className='research-row';
    const name=document.createElement('b');name.textContent=`${row.platform} · ${row.username||'account'}`;
    const status=document.createElement('small');
    status.textContent=`ID: ${row.accountId} · ${row.connected?'connected':'disconnected'} · ${row.health} · ${row.canPost?'posting ready':'posting blocked'} · analytics ${row.canFetchAnalytics?'yes':'not verified'}`;
    div.append(name,status);box.append(div);
  }
  const platform=$('#post-platform').value;
  accountOptions('#post-account',rows.filter(row=>row.platform===platform&&row.connected),
    access?'حساب متصل برای این پلتفرم انتخاب نشده':'ابتدا کشف حساب‌ها را اجرا کنید');
  accountOptions('#automation-account',rows.filter(row=>row.connected&&['instagram','facebook'].includes(row.platform)),
    access?'حساب Instagram/Facebook را انتخاب کنید':'کشف حساب‌ها لازم است');
}
function loadReport(row) {
  $('#report-id').value=row.id;
  $('#report-title').value=row.title||'';
  $('#report-game').value=row.game||'';
  $('#report-category').value=row.category||'other';
  $('#report-rank').value=row.rank||0;
  const primary=row.sources?.find(source=>source.type==='original');
  const supporting=row.sources?.find(source=>source.type==='supporting');
  $('#report-primary-url').value=primary?.url||'';
  $('#report-primary-note').value=primary?.note||'';
  $('#report-support-url').value=supporting?.url||'';
  $('#report-support-note').value=supporting?.note||'';
  $('#report-facts').value=row.facts||'';
  $('#report-uncertainty').value=row.uncertainty||'';
  for(const platform of ['instagram','telegram','blog'])
    $(`#report-${platform}`).value=row.variants?.[platform]||'';
  $('#report-keyword').value=row.keyword||'';
  $('#report-promised').value=row.promised||'';
  $('#report-feedback').textContent='پرونده برای ویرایش باز شد؛ هیچ واقعیتی صرفاً با ثبت لینک تأیید نمی‌شود.';
  go('intelligence');
}
function paintReports() {
  const box=$('#editorial-list'),select=$('#post-topic'),selected=select.value;
  box.replaceChildren();select.replaceChildren(new Option('بدون پیوند به گزارش',''));
  if(!reports.length){const empty=document.createElement('p');empty.className='empty';
    empty.textContent='هنوز گزارشی ثبت نشده است.';box.append(empty);return;}
  for(const row of reports) {
    select.add(new Option(`${row.game||'بازی'} · ${row.title}`,row.id));
    const div=document.createElement('div');div.className='research-row';
    const title=document.createElement('b');title.textContent=`${row.rank}/۱۰۰ · ${row.title}`;
    const meta=document.createElement('small');
    meta.textContent=`${row.category} · ${row.evidenceState} · ${row.sources?.length||0} منبع · ${row.posts?.length||0} پست ثبت‌شده`;
    const edit=document.createElement('button');edit.type='button';edit.className='btn small outline';
    edit.textContent='بازکردن پرونده';edit.addEventListener('click',()=>loadReport(row));
    div.append(title,meta,edit);box.append(div);
  }
  if(reports.some(row=>row.id===selected))select.value=selected;
}
async function refreshReports() {
  try {
    const result=await window.marketing.run({kind:'editorial',action:'list'});
    if(result.ok){reports=result.reports||[];paintReports();}
    else $('#report-feedback').textContent=result.error||'پرونده‌ها در دسترس نیستند.';
  }catch(e){$('#report-feedback').textContent=e.message;}
}
function paintResearch(result) {
  const box=$('#research-results');box.replaceChildren();
  const errors=(result.sources||[]).filter(row=>row.error).map(row=>`${row.source}: ${row.error}`);
  $('#research-status').textContent=`${result.observedAt||''} · ${errors.length?'منابع ناموفق: '+errors.join(' | '):'خوراک‌ها خوانده شد؛ تیترها نیاز به بررسی دارند.'}`;
  let count=0;
  for(const source of result.sources||[])for(const item of source.items||[]) {
    count++;
    const div=document.createElement('div');div.className='research-row';
    const title=document.createElement('b');title.textContent=item.title;
    const meta=document.createElement('small');meta.textContent=`${item.sourceName} · ${item.publishedAt||'زمان نامشخص'} · فقط تیتر`;
    const actions=document.createElement('div');actions.className='button-pair';
    const open=document.createElement('button');open.className='btn outline small';
    open.textContent='خواندن منبع';open.addEventListener('click',()=>
      window.marketing.openWebsite(item.url).catch(e=>toast(e.message,true)));
    const note=document.createElement('button');note.className='btn outline small';
    note.textContent='ثبت سرنخ';note.addEventListener('click',()=>{
      $('#editorial-form').reset();$('#report-id').value='';$('#report-title').value=item.title;
      $('#report-support-url').value=item.url;$('#report-support-note').value='';
      $('#report-feedback').textContent='تیتر فقط سرنخ است؛ منبع اصلی را جدا پیدا کنید و هر دو مقاله را بخوانید.';
    });
    actions.append(open,note);div.append(title,meta,actions);box.append(div);
  }
  if(!count){const empty=document.createElement('p');empty.className='empty';
    empty.textContent='سرنخی در دسترس نبود؛ خطای هر منبع را در بالا بخوانید.';box.append(empty);}
}
function showResult(selector, value) {
  $(selector).textContent = JSON.stringify(value, null, 2);
}
function busy(button, task) {
  const label = button.textContent;
  button.disabled = true;
  button.textContent = "در حال انجام…";
  return Promise.resolve()
    .then(task)
    .finally(() => {
      button.disabled = false;
      button.textContent = label;
    });
}
async function runOp(op, resultSelector, button) {
  return busy(button, async () => {
    try {
      const result = await window.marketing.run(op);
      showResult(resultSelector, result);
      const socialWrite=op.kind==='api'&&op.provider==='zernio'&&
        !['GET','HEAD'].includes(op.method);
      if(result.status===207)toast('نتیجهٔ پست جزئی است؛ تک‌تک مقصدها را در Zernio بررسی کنید.',true);
      else if(result.status===202)toast('درخواست در حال پردازش است؛ تا تأیید نهایی تکرار نکنید.',true);
      else if(result.status===409)toast('تعارض/تلاش هم‌زمان؛ شناسهٔ پست و Retry-After را در پاسخ Zernio بررسی کنید.',true);
      else if (result.ok&&socialWrite)
        toast('پاسخ Zernio دریافت شد؛ وضعیت انتشار/تحویل هر مقصد را جدا بررسی کنید.');
      else if (result.ok) toast('پاسخ دریافت شد.');
      else toast(result.error || `HTTP ${result.status || "error"}`, true);
      if (op.kind === "api" && resultSelector === "#api-result")
        $("#result-status").textContent = result.status
          ? `HTTP ${result.status}`
          : "خطا";
      const info = await window.marketing.startup();
      assets = info.assets;
      paintAssets();
      if (op.kind === "zernio-media-upload" && result.publicUrl)
        $("#post-media-url").value = result.publicUrl;
      return result;
    } catch (e) {
      showResult(resultSelector, { ok: false, error: e.message });
      toast(e.message, true);
      return { ok: false, error: e.message };
    }
  });
}
function freshId() {
  if (window.crypto.randomUUID) return window.crypto.randomUUID();
  const bytes = new Uint8Array(16);
  window.crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 15) | 64;
  bytes[8] = (bytes[8] & 63) | 128;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join(
    "",
  );
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
async function makePost(mode) {
  const content = $("#post-content").value.trim(),
    mediaUrl = $("#post-media-url").value.trim(),
    mediaType = $("#post-media-type").value,
    accountId = $("#post-account").value,
    platform = $("#post-platform").value,
    publishing=mode!=='draft';
  const selected=access?.accounts?.find(row=>row.accountId===accountId&&row.platform===platform&&row.connected);
  if (!selected) {toast('پیش از ساخت پست، کشف زندهٔ حساب را اجرا و مقصد را از فهرست انتخاب کنید.',true);return;}
  if(publishing&&!selected.canPost){toast('سلامت Zernio مجوز انتشار این حساب را تأیید نکرده است.',true);return;}
  if (!content && !mediaUrl) {toast('متن یا رسانهٔ پست را مشخص کنید.',true);return;}
  if (mediaUrl) {
    try {const url=new URL(mediaUrl);
      if(url.protocol!=='https:'||url.username||url.password)throw new Error('unsafe');
    }catch{toast('URL رسانه باید HTTPS عمومی و بدون نام کاربری باشد.',true);return;}
  }
  if(publishing&&platform==='youtube'&&(!mediaUrl||mediaType!=='video')){
    toast('YouTube از مسیر Zernio به ویدئوی بارگذاری‌شده نیاز دارد.',true);return;
  }
  if(publishing&&platform==='instagram'&&!mediaUrl){
    toast('Instagram از مسیر Zernio به تصویر یا ویدئو نیاز دارد.',true);return;
  }
  let specific={};
  try {const text=$('#post-platform-options').value.trim();if(text)specific=JSON.parse(text);
    if(!specific||Array.isArray(specific)||typeof specific!=='object')throw new Error('Expected an object');
  }catch(e){toast(`JSON تنظیمات مقصد معتبر نیست: ${e.message}`,true);return;}
  if(platform==='youtube'){
    const title=$('#post-youtube-title').value.trim();
    if(publishing&&!title){toast('عنوان ویدئوی YouTube را مشخص کنید.',true);return;}
    if(title)specific.title=title;
    specific.visibility=$('#post-youtube-visibility').value;
  }
  let scheduledFor;
  if(mode==='schedule'){
    const chosen=$('#post-scheduled-for').value;
    const time=Date.parse(chosen);
    if(!chosen||!Number.isFinite(time)||time<=Date.now()+60000){
      toast('زمان‌بندی گذشته فوراً منتشر می‌شود؛ زمان آیندهٔ معتبر انتخاب کنید.',true);return;
    }
    scheduledFor=new Date(time).toISOString();
  }
  const report=reports.find(row=>row.id===$('#post-topic').value);
  if(report?.keyword&&publishing){
    toast('برای پستِ دارای کلیدواژهٔ CTA اول Draft بسازید، محتوای وعده‌داده‌شده و اتوماسیون scoped را برای همان شناسهٔ Draft آماده/آزمون کنید؛ سپس همان Draft را پس از تأیید مالک ارتقا دهید.',true);return;
  }
  if(report&&publishing){
    if(report.evidenceState!=='notes_from_multiple_domains_need_review'){
      toast('برای انتشار موضوع پژوهشی، یادداشتِ اصل خبر و یک منبع مستقل دوم لازم است.',true);return;
    }
    if(report.posts?.some(post=>post.platforms?.some(p=>p.platform===platform))){
      toast('این موضوع برای این مقصد پست/پیش‌نویس ثبت‌شده یا نتیجهٔ نامطمئن دارد؛ وضعیت و شناسه را در Zernio بررسی کنید و پست تازه را کورکورانه نسازید.',true);return;
    }
    if(!window.confirm('آیا مقالهٔ اصلی و منبع مستقل را خوانده‌اید و متن ترکی و واقعیت‌ها را تأیید می‌کنید؟'))return;
  }
  const body={
    ...(content?{content}:{}),
    ...(mediaUrl?{mediaItems:[{url:mediaUrl,type:mediaType}]}:{}),
    platforms:[{platform,accountId,
      ...(Object.keys(specific).length?{platformSpecificData:specific}:{})}],
    ...(report?{metadata:{bazinoTopicId:report.id}}:{}),
    ...(mode==='publish'?{publishNow:true}:mode==='schedule'?{scheduledFor}:{isDraft:true})
  };
  const snapshot=JSON.stringify(body);
  if(!composerKey||composerBody!==snapshot){composerKey=freshId();composerBody=snapshot;}
  const op={kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',
    body,idempotencyKey:composerKey};
  const button=mode==='draft'?'#post-draft':mode==='schedule'?'#post-schedule':'#post-publish';
  const result=await runOp(op,'#post-result',$(button));
  if(result?.ok&&result.status!==202&&result.status!==207&&
    (!publishing||['published','scheduled'].includes(result.body?.post?.status))){
    composerKey=null;composerBody=null;
    $('#post-content').value='';$('#post-media-url').value='';$('#post-youtube-title').value='';
    if(report)await refreshReports();
  }
}
function gatewayAuthFields(){
  const kind=$('#gateway-kind').value,auth=$('#gateway-auth');
  auth.querySelector('[value="oauth"]').disabled=kind!=='mcp';
  auth.querySelector('[value="header"]').disabled=kind!=='api';
  if(auth.selectedOptions[0]?.disabled)auth.value='none';
  const needsKey=['bearer','header'].includes(auth.value);
  $('#gateway-credential').disabled=!needsKey;
  if(!needsKey)$('#gateway-credential').value='';
  $('#gateway-header').disabled=auth.value!=='header';
}
function loadConnector(row){
  $('#gateway-id').value=row.id;$('#gateway-id').readOnly=true;
  $('#gateway-name').value=row.name;$('#gateway-kind').value=row.kind;
  $('#gateway-url').value=row.url;$('#gateway-auth').value=row.auth;
  gatewayAuthFields();
  $('#gateway-header').value=row.headerName||'';
  $('#gateway-agent-access').value=row.agentAccess;
  $('#gateway-credential').value='';
  $('#gateway-credential').placeholder=row.hasCredential?'●●●●●● در Vault محلی':'کلید ذخیره نشده';
  $('#gateway-oauth').disabled=!(row.kind==='mcp'&&row.auth==='oauth');
  $('#gateway-feedback').textContent=`${row.name} · ${row.kind} · کلید: ${row.hasCredential?'ذخیره‌شده':'ندارد'} · OAuth: ${row.authorized?'محلی ذخیره‌شده':'تأیید نشده'}`;
}
function paintConnectors(){
  const box=$('#gateway-list');box.replaceChildren();
  for(const kind of ['api','mcp']){
    const select=$(`#gateway-${kind}-select`),old=select.value;
    select.replaceChildren(new Option('اتصال ثبت‌شده را انتخاب کنید',''));
    for(const row of connectors.filter(c=>c.kind===kind))
      select.add(new Option(`${row.name} (${row.id})`,row.id));
    if(connectors.some(c=>c.id===old&&c.kind===kind))select.value=old;
  }
  for(const row of connectors){
    const item=document.createElement('div');item.className='research-row';
    const label=document.createElement('b');label.textContent=`${row.name} · ${row.kind.toUpperCase()}`;
    const info=document.createElement('small');
    info.textContent=`${row.url} · ایجنت: ${row.agentAccess} · ${row.hasCredential?'کلید ذخیره‌شده':'بدون کلید'} · ${row.authorized?'OAuth محلی ذخیره‌شده':''}`;
    const button=document.createElement('button');button.type='button';button.className='btn small outline';
    button.textContent='ویرایش/ورود';button.addEventListener('click',()=>loadConnector(row));
    item.append(label,info,button);box.append(item);
  }
  if(!connectors.length){const empty=document.createElement('p');empty.className='empty';
    empty.textContent='هنوز اتصالی ثبت نشده است.';box.append(empty);}
  $('#gateway-oauth').disabled=!connectors.some(c=>c.id===$('#gateway-id').value&&c.kind==='mcp'&&c.auth==='oauth');
}
function resetConnectorForm(){
  $('#gateway-register').reset();$('#gateway-id').readOnly=false;
  gatewayAuthFields();
  $('#gateway-credential').value='';$('#gateway-credential').placeholder='فقط در Vault رمزگذاری‌شدهٔ محلی';
  $('#gateway-feedback').textContent='برای اتصال جدید شناسه، نشانی عمومی HTTPS و سطح دسترسی را تعیین کنید.';
  paintConnectors();
}
async function refreshConnectors(){
  const result=await window.marketing.listConnectors();
  connectors=result.connectors||[];paintConnectors();
}
function jsonArgs(selector){
  const value=$(selector).value.trim();if(!value)return {};
  const parsed=JSON.parse(value);
  if(!parsed||Array.isArray(parsed)||typeof parsed!=='object')throw new Error('آرگومان‌ها باید یک شیء JSON باشند');
  return parsed;
}
async function init() {
  try {
    const data = await window.marketing.startup();
    settings = data.settings;
    assets = data.assets || [];
    activity = data.history || [];
    connectors = data.connectors || [];
    paintConnectors();
    gatewayAuthFields();
    paintSettings();
    paintAssets();
    paintActivity();
    paintRelay(data.relay || {});
    paintAccess();
    await refreshReports();
    window.marketing.onActivity((row) => {
      activity.unshift(row);
      paintActivity();
    });
    window.marketing.onRelay(paintRelay);
  } catch (e) {
    toast(`راه‌اندازی: ${e.message}`, true);
  }
}
$$("[data-nav]").forEach((button) =>
  button.addEventListener("click", () => go(button.dataset.nav)),
);
$$("[data-go]").forEach((button) =>
  button.addEventListener("click", () => {
    go(button.dataset.go);
    if (button.dataset.provider)
      $("#api-provider").value = button.dataset.provider;
    if (button.dataset.platform) {
      $("#post-platform").value = button.dataset.platform;
      paintAccess();
    }
  }),
);
$$("[data-url]").forEach((button) =>
  button.addEventListener("click", () =>
    window.marketing
      .openWebsite(button.dataset.url)
      .catch((e) => toast(e.message, true)),
  ),
);
$$("[data-preset]").forEach((button) =>
  button.addEventListener("click", () => {
    const value = presets[button.dataset.preset];
    const preset = typeof value === "function" ? value() : value;
    $("#api-provider").value = preset.provider;
    $("#api-method").value = preset.method;
    $("#api-path").value = preset.path;
    $("#api-body").value = preset.body
      ? JSON.stringify(preset.body, null, 2)
      : "";
    $('#api-idempotency').value='';explorerPostSnapshot=null;explorerPostKey=null;
    go("explorer");
  }),
);
$("#settings-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const save = $("#settings-form [type=submit]");
  const patch = { secrets: {} };
  $$("[data-setting]").forEach(
    (el) =>
      (patch[el.dataset.setting] =
        el.type === "checkbox" ? el.checked : el.value.trim()),
  );
  $$("[data-secret]").forEach((el) => {
    if (el.value.trim()) patch.secrets[el.dataset.secret] = el.value.trim();
  });
  if (patch.autoApproveWrites && !settings.autoApproveWrites) {
    const accepted = window.confirm(
      "هشدار: با فعال‌سازی این گزینه، ایجنت می‌تواند بدون تأیید محلی هر بار درخواست نوشتن، انتشار، حذف و تولید هزینه‌دار را اجرا کند. تنها در یک بازهٔ محدود و با مجوز صریح این کار را انجام دهید. ادامه می‌دهید؟",
    );
    if (!accepted) {
      $("#settings-form [data-setting=autoApproveWrites]").checked = false;
      return;
    }
  }
  await busy(save, async () => {
    try {
      settings = await window.marketing.saveSettings(patch);
      if(patch.secrets.zernioKey){access=null;engagement=null;paintAccess();}
      paintSettings();
      $("#settings-feedback").textContent =
        "تنظیمات روی همین رایانه رمزگذاری و ذخیره شد.";
      toast("تنظیمات ذخیره شد.");
    } catch (err) {
      toast(err.message, true);
    }
  });
});
$('#post-platform').addEventListener('change',paintAccess);
$('#refresh-access').addEventListener('click',()=>busy($('#refresh-access'),async()=>{
  try {
    const result=await window.marketing.run({kind:'zernio-access'});
    if(!result.ok)throw new Error(result.error||'Zernio access unavailable');
    access=result;engagement=null;paintAccess();
    toast('فقط شناسه‌های متصل و وضعیت سلامتِ پاسخِ واقعی نمایش داده شد.');
  }catch(e){access=null;engagement=null;paintAccess();
    $('#access-status').textContent=`دسترسی تأیید نشد: ${e.message}`;toast(e.message,true);}
}));
$('#research-scan').addEventListener('click',()=>busy($('#research-scan'),async()=>{
  try {
    const result=await window.marketing.run({kind:'research',action:'scan',sourceIds:[]});
    paintResearch(result);
    toast(result.ok?'تیترهای عمومی دریافت شدند؛ منبع‌ها را بخوانید.':'هیچ منبعی در دسترس نبود.',!result.ok);
  }catch(e){$('#research-status').textContent=e.message;toast(e.message,true);}
}));
$('#editorial-form').addEventListener('submit',e=>{
  e.preventDefault();busy($('#report-save'),async()=>{
    const report={
      ...($('#report-id').value?{id:$('#report-id').value}:{}),
      title:$('#report-title').value,game:$('#report-game').value,
      category:$('#report-category').value,rank:Number($('#report-rank').value||0),
      sources:[{type:'original',url:$('#report-primary-url').value,note:$('#report-primary-note').value},
        {type:'supporting',url:$('#report-support-url').value,note:$('#report-support-note').value}],
      facts:$('#report-facts').value,uncertainty:$('#report-uncertainty').value,
      variants:Object.fromEntries(['instagram','telegram','blog'].map(k=>[k,$(`#report-${k}`).value])),
      keyword:$('#report-keyword').value,promised:$('#report-promised').value
    };
    try {
      const result=await window.marketing.run({kind:'editorial',action:'save',report});
      if(!result.ok)throw new Error(result.error||'Report not saved');
      $('#report-id').value=result.report.id;
      await refreshReports();
      $('#report-feedback').textContent='روی همین Windows رمزگذاری شد؛ هنوز صحت مطلب و مجوز نشر تأیید نشده است.';
      toast('گزارش محلی ذخیره شد.');
    }catch(e){$('#report-feedback').textContent=e.message;toast(e.message,true);}
  });
});
$('#use-topic-copy').addEventListener('click',()=>{
  const report=reports.find(row=>row.id===$('#post-topic').value);
  const content=report?.variants?.[$('#post-platform').value];
  if(!content){toast('برای این پلتفرم متن بومی در پرونده ثبت نشده است.',true);return;}
  $('#post-content').value=content;toast('نسخهٔ پیشنهادی بارگذاری شد؛ پیش از نشر بازبینی کنید.');
});
$('#engagement-check').addEventListener('click',()=>busy($('#engagement-check'),async()=>{
  const accountId=$('#automation-account').value;
  if(!accountId){toast('حساب Instagram/Facebook متصل را انتخاب کنید.',true);return;}
  try {
    const result=await window.marketing.run({kind:'zernio-engagement-check',accountId});
    showResult('#automation-result',result);
    engagement=result.ok?result:null;
    toast(result.ok&&result.commentsReadable&&result.messagesReadable?
      'فهرست پست‌های دارای کامنت و inbox پاسخ داد؛ خواندن متن تک‌تک کامنت‌ها و ارسال DM هنوز آزموده نشده است.':
      result.error||'خواندن فهرست پست‌های دارای کامنت یا inbox برای این حساب تأیید نشد.',
      !result.ok||!result.commentsReadable||!result.messagesReadable);
  }catch(e){engagement=null;showResult('#automation-result',{ok:false,error:e.message});toast(e.message,true);}
}));
$('#automation-create').addEventListener('click',async()=>{
  const accountId=$('#automation-account').value;
  const row=access?.accounts?.find(a=>a.accountId===accountId&&a.connected&&
    ['instagram','facebook'].includes(a.platform));
  if(!row||!row.profileId||!row.canPost){toast('حساب و Profile ID سالم را ابتدا از Zernio کشف کنید.',true);return;}
  if(!engagement?.commentsReadable||!engagement.messagesReadable||engagement.accountId!==accountId){
    toast('ابتدا دسترسی خواندن کامنت و پیامِ همین حساب را بررسی کنید.',true);return;
  }
  const targetKind=$('#automation-target-kind').value,targetId=$('#automation-target-id').value.trim();
  const keyword=$('#automation-keyword').value.trim(),dm=$('#automation-dm').value.trim();
  if(!targetId || targetKind==='postId'&&!/^[a-f0-9]{24}$/i.test(targetId)||
    !keyword||!dm||!$('#automation-name').value.trim()){
    toast('شناسهٔ همان پست، نام، کلیدواژه و محتوای واقعی پیام را کامل کنید.',true);return;
  }
  if(!window.confirm('ساخت این اتوماسیون آن را در Zernio فعال می‌کند و ممکن است فوراً DM واقعی بفرستد. کلیدواژه، مقصد، متن وعده و مجوز مخاطب را جدا تأیید می‌کنید؟'))return;
  const body={profileId:row.profileId,accountId,name:$('#automation-name').value.trim(),
    trigger:'comment',[targetKind]:targetId,keywords:[keyword],matchMode:'exact',
    dmMessage:dm,alsoMatchInDms:false};
  await runOp({kind:'api',provider:'zernio',method:'POST',path:'/v1/comment-automations',body},
    '#automation-result',$('#automation-create'));
});
$('#post-draft').addEventListener('click',()=>makePost('draft'));
$('#post-schedule').addEventListener('click',()=>makePost('schedule'));
$('#post-publish').addEventListener('click',()=>makePost('publish'));
$("#run-api").addEventListener("click", async () => {
  let body;
  try {
    body = $("#api-body").value.trim()
      ? JSON.parse($("#api-body").value)
      : undefined;
  } catch (e) {
    toast(`JSON نامعتبر: ${e.message}`, true);
    return;
  }
  const provider=$('#api-provider').value,method=$('#api-method').value,path=$('#api-path').value.trim();
  const post=provider==='zernio'&&method==='POST'&&path.split('?')[0]==='/v1/posts';
  let idempotencyKey;
  if(post){
    const snapshot=JSON.stringify(body);
    idempotencyKey=$('#api-idempotency').value.trim()||freshId();
    if(explorerPostSnapshot!==null&&snapshot!==explorerPostSnapshot&&idempotencyKey===explorerPostKey){
      toast('بدنهٔ پست عوض شده است؛ ابتدا وضعیت درخواست قبلی را بررسی و سپس کلید تازه انتخاب کنید.',true);return;
    }
    $('#api-idempotency').value=idempotencyKey;
    explorerPostSnapshot=snapshot;explorerPostKey=idempotencyKey;
  }
  const op={kind:'api',provider,method,path,
    ...(body!==undefined?{body}:{}),...(post?{idempotencyKey}:{})};
  await runOp(op,'#api-result',$('#run-api'));
});
$('#gateway-kind').addEventListener('change',gatewayAuthFields);
$('#gateway-auth').addEventListener('change',gatewayAuthFields);
$('#gateway-register').addEventListener('submit',e=>{
  e.preventDefault();busy($('#gateway-save'),async()=>{
    const credential=$('#gateway-credential').value;
    const row={id:$('#gateway-id').value.trim(),name:$('#gateway-name').value.trim(),
      kind:$('#gateway-kind').value,url:$('#gateway-url').value.trim(),
      auth:$('#gateway-auth').value,headerName:$('#gateway-header').value.trim(),
      agentAccess:$('#gateway-agent-access').value,
      ...(credential?{credential}:{})};
    try{
      const result=await window.marketing.saveConnector(row);
      if(!result.ok)throw new Error(result.error||'ثبت اتصال لغو شد');
      connectors=result.connectors||[];paintConnectors();
      loadConnector(result.connector);
      toast('اتصال در Vault محلی ثبت شد؛ برای MCP/OAuth حالا ورود را آغاز کنید.');
    }catch(error){$('#gateway-feedback').textContent=error.message;toast(error.message,true);}
    finally{$('#gateway-credential').value='';}
  });
});
$('#gateway-new').addEventListener('click',resetConnectorForm);
$('#gateway-remove').addEventListener('click',()=>busy($('#gateway-remove'),async()=>{
  try{
    const result=await window.marketing.removeConnector($('#gateway-id').value.trim());
    if(!result.ok)throw new Error(result.error||'حذف اتصال لغو شد');
    connectors=result.connectors||[];resetConnectorForm();toast('اتصال و رازهای محلی آن حذف شدند.');
  }catch(error){toast(error.message,true);}
}));
$('#gateway-oauth').addEventListener('click',()=>busy($('#gateway-oauth'),async()=>{
  try{
    const result=await window.marketing.authorizeConnector($('#gateway-id').value.trim());
    if(!result.ok)throw new Error(result.error||'ورود تأیید نشد');
    await refreshConnectors();
    const row=connectors.find(c=>c.id===$('#gateway-id').value);
    if(row)loadConnector(row);
    showResult('#gateway-mcp-result',result);
    toast('OAuth در دستگاه مالک انجام شد. ابزارها را از سرور زنده بررسی کنید.');
  }catch(error){$('#gateway-feedback').textContent=error.message;toast(error.message,true);}
}));
$('#web-read').addEventListener('click',()=>runOp(
  {kind:'web-fetch',url:$('#web-url').value.trim()},'#web-result',$('#web-read')));
$('#gateway-api-run').addEventListener('click',()=>{
  let body;try{body=$('#gateway-api-body').value.trim()?JSON.parse($('#gateway-api-body').value):undefined;}
  catch(error){toast(`JSON نامعتبر: ${error.message}`,true);return;}
  runOp({kind:'gateway-api',connectorId:$('#gateway-api-select').value,
    method:$('#gateway-api-method').value,path:$('#gateway-api-path').value.trim(),
    ...(body!==undefined?{body}:{})},'#gateway-api-result',$('#gateway-api-run'));
});
$('#gateway-mcp-run').addEventListener('click',()=>{
  try{
    const action=$('#gateway-mcp-action').value,
      name=$('#gateway-mcp-name').value.trim();
    const op={kind:'gateway-mcp',connectorId:$('#gateway-mcp-select').value,action};
    if(action==='call'){op.tool=name;op.args=jsonArgs('#gateway-mcp-args');}
    if(action==='read-resource')op.uri=name;
    if(action==='get-prompt'){op.name=name;op.args=jsonArgs('#gateway-mcp-args');}
    runOp(op,'#gateway-mcp-result',$('#gateway-mcp-run'));
  }catch(error){toast(error.message,true);}
});
$("#import-media").addEventListener("click", async () => {
  await busy($("#import-media"), async () => {
    try {
      const result = await window.marketing.importAsset();
      if (result) {
        assets.unshift(result);
        paintAssets();
        toast(`رسانه اضافه شد: ${result.name}`);
      }
    } catch (e) {
      toast(e.message, true);
    }
  });
});
$("#upload-zernio").addEventListener("click", () =>
  runOp(
    { kind: "zernio-media-upload", assetId: $("#selected-asset").value },
    "#media-result",
    $("#upload-zernio"),
  ),
);
$("#run-kling").addEventListener("click", () => {
  let args;
  try {
    args = JSON.parse($("#kling-args").value);
    if (!args || typeof args !== "object" || Array.isArray(args))
      throw new Error("پارامترهای MCP باید شیء JSON باشند");
  } catch (e) {
    toast(e.message, true);
    return;
  }
  runOp(
    { kind: "kling", command: $("#kling-command").value, args },
    "#kling-result",
    $("#run-kling"),
  );
});
for (const [selector, fn] of [
  ["#kling-login", "authorizeKling"],
  ["#kling-identity", "klingIdentity"],
  ["#kling-tools", "klingTools"],
  ["#kling-logout", "logoutKling"],
]) {
  $(selector).addEventListener("click", () =>
    busy($(selector), async () => {
      try {
        const answer = await window.marketing[fn]();
        if (answer.identity || answer.body || answer.tools) {
          showResult("#kling-result", answer.identity || answer.body || answer.tools);
        }
        settings = (await window.marketing.startup()).settings;
        paintSettings();
        $("#kling-setup-status").textContent = answer.ok
          ? (answer.message || (fn === "klingIdentity" ? "who_am_i پاسخ داد؛ هویت و مدل‌ها را در پایین ببینید." : "پاسخ دریافت شد."))
          : answer.error || "اتصال تأیید نشد.";
        toast(answer.message || (answer.ok ? "پاسخ واقعی دریافت شد." : answer.error || "خطا"), !answer.ok);
      } catch (e) {
        $("#kling-setup-status").textContent = "احراز هویت هنوز تأیید نشده است: " + e.message;
        toast(e.message, true);
      }
    }),
  );
}
$("#relay-connect").addEventListener("click", () =>
  busy($("#relay-connect"), async () => {
    const result = await window.marketing.connectRelay();
    paintRelay(result);
    $("#relay-feedback").textContent = result.connected
      ? "اتصال امن برقرار شد. اثر انگشت را با ایجنت مقایسه کنید."
      : result.error;
    toast(
      result.connected ? "ارتباط GitHub برقرار شد." : result.error,
      !result.connected,
    );
  }),
);
$("#relay-disconnect").addEventListener("click", async () => {
  const result = await window.marketing.disconnectRelay();
  paintRelay(result);
  $("#relay-feedback").textContent = "ارتباط قطع شد.";
  toast("اتصال قطع شد.");
});
$("#relay-pair").addEventListener("click", () =>
  busy($("#relay-pair"), async () => {
    try {
      const result = await window.marketing.pairAgent();
      if (result.paired) {
        settings = result.settings;
        paintSettings();
        toast("کلید امضای ایجنت تأیید شد.");
      } else toast("جفت‌سازی لغو شد.");
    } catch (e) {
      toast(e.message, true);
    }
  }),
);
$("#app-stop").addEventListener("click", async () => {
  if (!window.confirm("ارتباط با GitHub قطع و برنامه بسته شود؟")) return;
  try {
    await window.marketing.stopApp();
    document.body.replaceChildren();
    document.body.textContent = "برنامه بسته شد. برای ادامه، BazinoMarketing.exe را دوباره اجرا کنید.";
  } catch (e) { toast(e.message, true); }
});
init();
