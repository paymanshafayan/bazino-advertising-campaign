'use strict';

// Only Zernio knows which accounts are currently connected and what their tokens can do.
// Never trust an account ID supplied by the browser or the encrypted relay without a fresh read.
function accountId(account) {
  return typeof account?._id === 'string' ? account._id : typeof account?.accountId === 'string' ? account.accountId : '';
}
function profileId(account) {
  const profile = account?.profileId;
  return typeof profile === 'string' ? profile : typeof profile?._id === 'string' ? profile._id : '';
}
function normalizeAccess(accountsBody, healthBody) {
  if (!Array.isArray(accountsBody?.accounts) || !Array.isArray(healthBody?.accounts))
    throw new Error('Zernio account discovery/health returned an unexpected shape; no writes are allowed');
  const health = new Map(healthBody.accounts.map(row => [row.accountId, row]));
  const accounts = accountsBody.accounts.filter(row => accountId(row) && typeof row.platform === 'string')
    .map(row => {
      const id = accountId(row), status = health.get(id);
      return { accountId:id, platform:row.platform, profileId:profileId(row),
        username:String(row.username||row.displayName||'').slice(0,120),
        connected:row.isActive === true, health:status?.status || 'unknown',
        canPost:status?.canPost === true && status?.tokenValid === true && status?.needsReconnect !== true,
        canFetchAnalytics:accountsBody.hasAnalyticsAccess === true && status?.canFetchAnalytics === true,
        issues:Array.isArray(status?.issues) ? status.issues.map(v=>String(v).slice(0,120)).slice(0,5) : [] };
    });
  return {accounts,hasAnalyticsAccess:accountsBody.hasAnalyticsAccess === true,
    checkedAt:new Date().toISOString(),
    commentAndDmScopesVerified:false,
    note:'Account health does not prove Instagram comment/DM scopes. Test authorized inbox access separately.'};
}
function needsPublishing(body) {
  return !!body && body.isDraft !== true && (body.publishNow === true || body.scheduledFor ||
    body.queuedFromProfile || body.platforms?.some?.(p=>p?.scheduledFor));
}
function validateSchedule(body,now=Date.now()) {
  if (!needsPublishing(body) || body.publishNow === true) return;
  const dates=[body.scheduledFor,...(Array.isArray(body.platforms)?body.platforms.map(p=>p?.scheduledFor):[])].filter(Boolean);
  for(const when of dates) {
    if (typeof when !== 'string' || !/^\d{4}-\d\d-\d\dT\d\d:\d\d(?::\d\d(?:\.\d{1,3})?)?(?:Z|[+-]\d\d:\d\d)$/.test(when) ||
        !Number.isFinite(Date.parse(when)) || Date.parse(when) <= now + 60_000)
      throw new Error('Zernio publishes past scheduledFor immediately: use a future ISO time with Z/offset, at least 1 minute ahead');
  }
}
function validateTargets(body, access) {
  const targets=body?.platforms;
  if (targets === undefined && body?.isDraft === true) return;
  if (!Array.isArray(targets) || !targets.length)
    throw new Error('Select at least one account returned by live Zernio discovery');
  const used=new Set();
  for(const target of targets) {
    if(typeof target?.accountId !== 'string' || typeof target?.platform !== 'string')
      throw new Error('Each post target needs a connected Zernio platform and accountId');
    const found=access.accounts.find(a=>a.accountId===target.accountId && a.platform===target.platform && a.connected);
    if(!found)throw new Error(`Zernio has not confirmed a connected ${target.platform} account matching the supplied ID`);
    if(used.has(target.accountId))throw new Error('The same account cannot appear twice in a single post');
    used.add(target.accountId);
    if(needsPublishing(body) && !found.canPost)
      throw new Error(`Zernio health does not confirm posting access for ${target.platform}/${target.accountId}; reconnect or check permissions`);
  }
}
function assertCommentAutomation(body,access) {
  if (!body || typeof body !== 'object' || Array.isArray(body) ||
    !['instagram','facebook'].some(platform => access.accounts.some(a =>
      a.platform===platform && a.accountId===body.accountId && a.profileId===body.profileId && a.connected)))
    throw new Error('Comment-to-DM requires a live connected Instagram/Facebook account and matching Zernio profile');
  if (!body.postId && !body.platformPostId)
    throw new Error('An account-wide automatic DM is not allowed in the guided studio; target a specific post');
  if (!Array.isArray(body.keywords) || !body.keywords.length ||
    body.keywords.some(word=>typeof word!=='string'||!word.trim()||word.length>40))
    throw new Error('Use a nonempty, topic-specific keyword; never match all commenters');
  if(typeof body.dmMessage!=='string'||!body.dmMessage.trim())
    throw new Error('Prepare the promised information before activating a comment-to-DM automation');
  if(!access.accounts.find(a=>a.accountId===body.accountId)?.canPost)
    throw new Error('Zernio health does not confirm the target account is ready');
  // Even a healthy account may lack manage_comments/manage_messages scopes. The API will
  // reject missing scopes; never report a functioning DM until a real delivery is observed.
}
module.exports={normalizeAccess,validateTargets,validateSchedule,assertCommentAutomation,needsPublishing};
