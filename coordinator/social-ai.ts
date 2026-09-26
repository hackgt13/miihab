// The model's three jobs on the social surface:
//
//   · which friend deserves the spotlight today
//   · one plain line per friend — "returned after three days"
//   · a recap of a thread you have not read in a while
//
// Every entry point is best-effort and returns null on any failure — no key, no
// network, a refusal, a malformed reply. Callers keep the pre-AI behaviour as
// their floor, for the same reason ENCOURAGEMENTS is a fixed set rather than
// something generated: this surface has to work on a plane.
//
// What leaves the machine: display names, day counts, unread counts. Message
// text goes only to `recap`, where summarising it is the whole point. No
// measurements ever — friends.ts keeps one patient's numbers away from another,
// and the model does not get an exception to that rule.
//
// The model narrates; it never speaks as a person. A line here is the app
// saying "returned after three days", never Maya saying anything. FriendsPanel
// still quotes real messages verbatim and this never overwrites one.
import Anthropic from '@anthropic-ai/sdk';

const MODEL = 'claude-opus-5';
/// A decline would only degrade us to the non-AI path, but the rescue is free
/// to ask for and this copy sits close enough to clinical language to warrant it.
const FALLBACK_BETA = 'server-side-fallback-2026-07-01';

/// One call per menu load would be one call per app launch. Signatures change
/// only when the underlying facts do, so an idle relaunch reuses the answer.
const TTL_MS = 10 * 60 * 1000;

export interface Candidate {
  id: string;
  displayName: string;
  /// Whole days since each event, or null when it has never happened.
  daysSinceActive: number | null;
  daysSinceTheyWrote: number | null;
  daysSinceIWrote: number | null;
  /// How long they were quiet before their most recent message. This is what
  /// makes "returned after three days" sayable; daysSinceActive alone cannot.
  quietDaysBeforeTheyReturned?: number | null;
  unread: number;
  sample: boolean;
}

export interface ActivityLine {
  id: string;
  /// A fragment with no name and no full stop, or '' for nothing worth saying.
  /// The UI composes "<name> <line>".
  line: string;
}
export interface Spotlight {
  /// Who to surface. Always one of the ids passed in.
  choose: string;
  /// An array rather than a map because Unity's JsonUtility cannot deserialise
  /// an object with dynamic keys.
  activity: ActivityLine[];
}

const SYSTEM_SPOTLIGHT =
  'You help a rehabilitation app choose which friend to surface on a patient\'s home ' +
  'screen, and narrate each friend\'s recent activity in plain language.\n\n' +
  'Rules:\n' +
  '- Use only the signals given. Never infer a mood, a feeling or a reason.\n' +
  '- Never write words a person is supposed to have said. You are the app narrating, ' +
  'not the friend speaking.\n' +
  '- Never mention progress, recovery, effort, or anything clinical. You cannot see ' +
  'measurements and must not guess at them.\n' +
  '- An activity line is a short factual fragment, no name and no final full stop: ' +
  '"returned after three days", "has been here every day this week", "sent you ' +
  'something yesterday".\n' +
  '- quietDaysBeforeTheyReturned is how long they were away before their latest ' +
  'message. When it is two days or more and they wrote recently, that is a ' +
  'return: "returned after three days".\n' +
  '- If a person has no signal worth narrating, give them an empty line.\n' +
  '- Prefer to spotlight someone waiting on a reply, or who has just come back after ' +
  'being away.';

const SPOTLIGHT_TOOL = {
  name: 'spotlight',
  description: 'Choose who to surface and narrate each person\'s recent activity.',
  strict: true,
  input_schema: {
    type: 'object',
    properties: {
      choose: { type: 'string', description: 'The id of the person to spotlight.' },
      activity: {
        type: 'array',
        items: {
          type: 'object',
          properties: {
            id: { type: 'string' },
            line: { type: 'string', description: 'Fragment with no name, no full stop, or "".' },
          },
          required: ['id', 'line'],
          additionalProperties: false,
        },
      },
    },
    required: ['choose', 'activity'],
    additionalProperties: false,
  },
} as const;

const SYSTEM_RECAP =
  'You summarise a short message thread between two people recovering from injury or ' +
  'illness, for the one about to reopen it.\n\n' +
  'Rules:\n' +
  '- One sentence, under 20 words, addressed to the reader as "you".\n' +
  '- Say only what is in the messages. Never add advice, encouragement or ' +
  'interpretation, and never mention progress or anything clinical.\n' +
  '- Do not quote. The thread itself is right there; you are the line above it.\n' +
  '- sentPhoto tells you a photo was attached. Never say someone sent one unless ' +
  'that flag is set, whatever the wording suggests.\n' +
  '- If there is nothing worth recapping, return an empty string.';

const RECAP_TOOL = {
  name: 'recap',
  description: 'Summarise the thread in one sentence.',
  strict: true,
  input_schema: {
    type: 'object',
    properties: { recap: { type: 'string' } },
    required: ['recap'],
    additionalProperties: false,
  },
} as const;

let client: Anthropic | null = null;
function anthropic(): Anthropic | null {
  // An unset key is the normal case for a local install, not an error.
  if (!process.env.ANTHROPIC_API_KEY && !process.env.ANTHROPIC_AUTH_TOKEN) return null;
  if (!client) client = new Anthropic();
  return client;
}

/// Whether the AI path is configured at all. Routes use this to answer quickly
/// instead of constructing a request that cannot be sent.
export function available(): boolean {
  return anthropic() !== null;
}

/// Degrading silently makes "no key" and "the request failed" look identical
/// from the outside. The feature still degrades; it just says why first.
function warn(where: string, error: unknown) {
  console.warn(`social-ai: ${where} unavailable —`, error instanceof Error ? error.message : error);
}

const cache = new Map<string, { at: number; value: unknown }>();
function cached<T>(key: string): T | undefined {
  const hit = cache.get(key);
  if (!hit) return undefined;
  if (Date.now() - hit.at > TTL_MS) { cache.delete(key); return undefined; }
  return hit.value as T;
}

/// Pull the one tool call out of a response, or null if the model refused,
/// stopped early, or answered in prose instead.
function toolInput(response: any, name: string): any | null {
  if (response?.stop_reason === 'refusal') return null;
  for (const block of response?.content ?? []) {
    if (block.type === 'tool_use' && block.name === name) return block.input;
  }
  return null;
}

async function call(system: string, tool: unknown, payload: unknown, maxTokens: number) {
  const api = anthropic();
  if (!api) return null;
  return await api.beta.messages.create({
    model: MODEL,
    max_tokens: maxTokens,
    betas: [FALLBACK_BETA],
    fallbacks: 'default',
    system,
    tools: [tool as any],
    tool_choice: { type: 'tool', name: (tool as any).name },
    messages: [{ role: 'user', content: JSON.stringify(payload) }],
  } as any);
}


/**
 * Turn whatever the model returned into something the UI can trust: a real id,
 * one line per person at most, nothing about someone who was not asked about.
 * Exported for tests — this is the part most likely to be wrong, and it is the
 * part that needs no network to check.
 */
export function normaliseSpotlight(input: any, people: Candidate[]): Spotlight {
  const ids = new Set(people.map(p => p.id));
  // A hallucinated id would spotlight nobody, so keep the narration and fall
  // back to the first candidate for the choice.
  const choose = ids.has(input?.choose) ? input.choose : people[0].id;

  const seen = new Set<string>();
  const activity: ActivityLine[] = [];
  for (const entry of input?.activity ?? []) {
    if (!entry || !ids.has(entry.id) || seen.has(entry.id)) continue;
    if (typeof entry.line !== 'string') continue;
    seen.add(entry.id);
    activity.push({ id: entry.id, line: entry.line.trim().slice(0, 80) });
  }
  return { choose, activity };
}

/**
 * Rank the candidates and narrate each one. Returns null whenever the model
 * cannot be reached or does not answer usefully, which the caller reads as
 * "keep doing what you did before".
 */
export async function spotlight(people: Candidate[]): Promise<Spotlight | null> {
  if (!available() || people.length === 0) return null;

  const key = 'spotlight:' + JSON.stringify(people);
  const hit = cached<Spotlight>(key);
  if (hit) return hit;

  try {
    // Metadata only. Ranking does not need anybody's words, so none are sent.
    const response = await call(SYSTEM_SPOTLIGHT, SPOTLIGHT_TOOL, { people }, 2000);
    const input = toolInput(response, 'spotlight');
    if (!input) return null;

    const value = normaliseSpotlight(input, people);
    cache.set(key, { at: Date.now(), value });
    return value;
  } catch (error) {
    warn('spotlight', error);
    return null;
  }
}

export interface RecapMessage {
  fromMe: boolean;
  kind: string | null;
  text: string;
  /// Passed as a fact so the summary never has to infer one from a caption.
  photo?: boolean;
  at: string;
}

/**
 * One line describing what a thread has been about. Returns null on any
 * failure and '' when there is nothing worth saying.
 */
export async function recap(otherName: string, messages: RecapMessage[]): Promise<string | null> {
  if (!available() || messages.length < 3) return null;

  // Keyed on the tail: appending a message invalidates, reopening does not.
  const key = 'recap:' + otherName + ':' + messages.length + ':' + messages[messages.length - 1].at;
  const hit = cached<string>(key);
  if (hit !== undefined) return hit;

  try {
    const recent = messages.slice(-20).map(m => ({
      who: m.fromMe ? 'you' : otherName,
      said: m.kind ?? m.text,
      sentPhoto: m.photo === true,
      at: m.at,
    }));
    const response = await call(SYSTEM_RECAP, RECAP_TOOL, { with: otherName, messages: recent }, 1000);
    const input = toolInput(response, 'recap');
    if (!input || typeof input.recap !== 'string') return null;

    const value = input.recap.trim().slice(0, 160);
    cache.set(key, { at: Date.now(), value });
    return value;
  } catch (error) {
    warn('recap', error);
    return null;
  }
}

/// Whole days between an ISO timestamp and now, or null if absent/unparseable.
export function daysSince(iso: string | null | undefined): number | null {
  if (!iso) return null;
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return null;
  return Math.max(0, Math.floor((Date.now() - then) / 86400000));
}
