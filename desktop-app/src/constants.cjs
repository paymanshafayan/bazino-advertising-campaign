'use strict';

const REPO = 'paymanshafayan/bazino-gamenet-portal';
const BRANCH = 'arena/01a0d4ee-bazino-gamenet-portal';
const RELAY_ROOT = 'استودیوی تبلیغات و بازاریابی/desktop-app/relay';
const PROTOCOL_VERSION = 1;

// Social publishing (including YouTube, Telegram, Instagram) goes ONLY through Zernio.
// FLUX is available in the portal by default; local Cloudflare access is optional.
const SECRET_FIELDS = Object.freeze(['githubToken', 'zernioKey', 'cloudflareToken']);
const PUBLIC_DEFAULTS = Object.freeze({
  cloudflareAccountId: '', agentPublicKey: '',
  autoApproveReads: true,
  // This option is available for an owner-approved automation window. Off by default.
  autoApproveWrites: false
});
const PROVIDERS = Object.freeze(['zernio', 'cloudflare']);
const PORTAL_MEDIA_STUDIO = 'https://bazino.pro/admin/content';
// Names published in Kling's official MCP guide. No regional CLI is installed.
const KLING_ENDPOINT = 'https://kling.ai/mcp';
const KLING_COMMANDS = Object.freeze([
  'who_am_i', 'query_membership_and_credits', 'text_to_image', 'image_to_image',
  'text_to_video', 'image_to_video', 'motion_control', 'query_tasks',
  'file_upload', 'element_create', 'element_list', 'element_get',
  'element_update', 'element_delete', 'motion_library_list'
]);
const READ_METHODS = Object.freeze(['GET', 'HEAD']);
const MAX_REQUEST_BYTES = 64 * 1024;
const MAX_RESPONSE_BYTES = 512 * 1024;

module.exports = {
  REPO, BRANCH, RELAY_ROOT, PROTOCOL_VERSION,
  SECRET_FIELDS, PUBLIC_DEFAULTS, PROVIDERS, PORTAL_MEDIA_STUDIO, KLING_ENDPOINT, KLING_COMMANDS,
  READ_METHODS, MAX_REQUEST_BYTES, MAX_RESPONSE_BYTES
};
