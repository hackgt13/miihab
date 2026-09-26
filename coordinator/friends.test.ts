import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { FriendStore, properName } from './friends.ts';

const dir = () => mkdtempSync(resolve(tmpdir(), 'friends-'));

test('a typed name is shown with each word capitalised', () => {
  assert.equal(properName('  maya '), 'Maya');
  assert.equal(properName('de la cruz'), 'De La Cruz');
  assert.equal(properName("tomás o'neil-inês"), "Tomás O'Neil-Inês");
  assert.equal(properName('MAYA'), 'MAYA');
  assert.equal(properName(undefined), '');
});

test('every way a name enters the graph capitalises it', () => {
  const store = new FriendStore(dir());
  assert.equal(store.setName('keijay').displayName, 'Keijay');
  assert.equal(store.accept(store.invite(), 'arun').displayName, 'Arun');
  assert.equal(store.meet({ id: 'p9', displayName: 'wen', mii: 1 }).displayName, 'Wen');
  assert.equal(store.accept(store.invite()).displayName, 'A friend');
});
