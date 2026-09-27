// Sharing a milestone outside the app, through Meta (the Graph API platform's Share Dialog).
//
// The patient already chooses to share a milestone with their friends in MiiHab (milestones.ts). This is the same
// choice, one step further: Facebook's own share window opens with the card, and there the patient picks Facebook or
// Messenger, the audience, and whether to post at all. Nothing is posted by the app, and no token is held.
//
// Facebook can only share a public link, so the card is a public page (share-page/, on GitHub Pages) and the words
// travel in its query string: the milestone line the patient saw on the card and its fact — never a measurement,
// a diagnosis or a name. Without META_APP_ID there is no Facebook button, and nothing else changes.

const DIALOG = 'https://www.facebook.com/dialog/share';

export interface ShareConfig { appId: string | null; pageUrl: string }

export function shareConfig(env: NodeJS.ProcessEnv = process.env): ShareConfig {
  const appId = env.META_APP_ID?.trim();
  return {
    appId: appId && /^\d{5,20}$/.test(appId) ? appId : null,
    pageUrl: env.META_SHARE_URL?.trim() || 'https://hackgt13.github.io/miihab-share/',
  };
}

/** The public card for a milestone: the share page with the line and the fact in its query. */
export function cardUrl(pageUrl: string, line: string, fact: string): string {
  const url = new URL(pageUrl);
  url.searchParams.set('t', line.slice(0, 240));
  url.searchParams.set('f', fact.slice(0, 60));
  return url.toString();
}

/** Facebook's share window for that card, or '' when no Meta app is configured. */
export function facebookShareUrl(config: ShareConfig, line: string, fact: string): string {
  if (!config.appId) return '';
  const dialog = new URL(DIALOG);
  dialog.searchParams.set('app_id', config.appId);
  dialog.searchParams.set('display', 'page');
  dialog.searchParams.set('href', cardUrl(config.pageUrl, line, fact));
  dialog.searchParams.set('hashtag', '#MiiHab');
  return dialog.toString();
}
