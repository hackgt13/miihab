// Sharing to Facebook carries the card's words and nothing else, and is off without a Meta app.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { shareConfig, cardUrl, facebookShareUrl } from './share.ts';

test('no Meta app, no Facebook button', () => {
  assert.equal(facebookShareUrl(shareConfig({}), 'I showed up 7 days in a row.', '7 days in a row'), '');
  assert.equal(shareConfig({ META_APP_ID: 'not-an-id' }).appId, null);
});

test('the share window opens Facebook with the public card, the line and the fact only', () => {
  const config = shareConfig({ META_APP_ID: '1313986181801600', META_SHARE_URL: 'https://example.github.io/miihab-share/' });
  const dialog = new URL(facebookShareUrl(config, 'I showed up 7 days in a row & kept going.', '7 days in a row'));
  assert.equal(dialog.origin + dialog.pathname, 'https://www.facebook.com/dialog/share');
  assert.equal(dialog.searchParams.get('app_id'), '1313986181801600');
  assert.equal(dialog.searchParams.get('hashtag'), '#MiiHab');
  const card = new URL(dialog.searchParams.get('href')!);
  assert.equal(card.origin + card.pathname, 'https://example.github.io/miihab-share/');
  assert.deepEqual([...card.searchParams.keys()], ['t', 'f']);
  assert.equal(card.searchParams.get('t'), 'I showed up 7 days in a row & kept going.');
});

test('a runaway line is clipped to what the card shows', () => {
  const card = new URL(cardUrl('https://example.github.io/x/', 'x'.repeat(1000), 'y'.repeat(100)));
  assert.equal(card.searchParams.get('t')!.length, 240);
  assert.equal(card.searchParams.get('f')!.length, 60);
});
