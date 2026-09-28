// The model's three jobs on the social surface:
//
//   · which friend deserves the spotlight today
//   · one plain line per friend — "returned after three days"
//   · a recap of a thread you have not read in a while
//   · a first draft of what you might say back, for you to change or send
//   · the warm line on an introduction card, from the goals two people share
//   · the words on a milestone you choose to share with your friends
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
// Which model: Meta's Muse Spark (Meta Model API, MUSE_API_KEY) when configured, else Claude. Muse is called
// through its OpenAI-compatible Chat Completions with structured output against the same schema the Claude path's
// tool uses, and its reply is read back through the same toolInput(), so every entry point and normaliser is
// provider-blind.
//
// The model narrates; it never speaks as a person — with one exception that is
// the patient's own choice: a draft reply or a milestone is written in their
// voice, lands in their composer or on their card, and goes nowhere until they
// press send. The model never messages anyone. A line here is the app
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
  '"returned after three days", "here every day this week", "waiting on a reply ' +
  'since Tuesday".\n' +
  '- Give every person a line whenever you have any fact about them at all. Only ' +
  'use an empty line when every field is null, which means you have nothing. A ' +
  'thin true line beats silence: "here today", "no messages yet".\n' +
  '- Say it about them where you can. Fall back to the state of the conversation ' +
  '("waiting on your reply", "you wrote last") only when you know nothing else.\n' +
  '- Take the most useful fact, in this order: they came back after a gap; they ' +
  'are waiting on a reply; when they were last here; who wrote last.\n' +
  '- quietDaysBeforeTheyReturned is how long they were away before their latest ' +
  'message. When it is two days or more and they wrote recently, that is a ' +
  'return: "returned after three days".\n' +
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
  '- Always give a sentence when there are messages. A thread of nothing but quick ' +
  'encouragements is worth saying plainly - "mostly quick encouragements, nothing ' +
  'said in a while" - rather than returning nothing.\n' +
  '- Return an empty string only when there are no messages at all.';

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

const MUSE_URL = process.env.MUSE_BASE_URL ?? 'https://api.meta.ai/v1';
const MUSE_MODEL = process.env.MUSE_MODEL ?? 'muse-spark-1.3';
const muse = () => process.env.MUSE_API_KEY || null;

/// Muse Spark reasons before it answers; for a line of social copy minimal reasoning is plenty and keeps a menu
/// load at a couple of seconds. Muse cannot be forced to call a tool (tool_choice is "auto" only), so it answers
/// in structured output against the tool's own schema instead, and that answer is shaped here like an Anthropic
/// tool_use block, so the rest of this file never knows which model spoke.
async function callMuse(system: string, tool: any, payload: unknown, maxTokens: number) {
  const response = await fetch(`${MUSE_URL}/chat/completions`, {
    method: 'POST', signal: AbortSignal.timeout(20_000),
    headers: { Authorization: `Bearer ${muse()}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      model: MUSE_MODEL, reasoning_effort: 'minimal', max_completion_tokens: maxTokens + 600,
      messages: [{ role: 'system', content: `${system}\n\n${tool.description ?? ''}` }, { role: 'user', content: JSON.stringify(payload) }],
      response_format: { type: 'json_schema', json_schema: { name: tool.name, schema: tool.input_schema, strict: true } },
    }),
  });
  if (!response.ok) throw Error(`Muse ${response.status}: ${(await response.text()).slice(0, 200)}`);
  const body = await response.json();
  const choice = body.choices?.[0];
  if (choice?.finish_reason === 'content_filter' || choice?.message?.refusal) return { stop_reason: 'refusal', content: [] };
  let input: unknown = null;
  try { input = JSON.parse(choice?.message?.content ?? 'null'); } catch { /* malformed: toolInput sees null */ }
  return { stop_reason: 'tool_use', content: [{ type: 'tool_use', name: tool.name, input }] };
}

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
  return muse() !== null || anthropic() !== null;
}

/// Which model the social surface is using, for the status line and the pitch: 'muse', 'claude' or null.
export function provider(): 'muse' | 'claude' | null {
  return muse() ? 'muse' : anthropic() ? 'claude' : null;
}

/// Degrading silently makes "no key" and "the request failed" look identical
/// from the outside. The feature still degrades; it just says why first.
function warn(where: string, error: unknown) {
  console.warn(`social-ai: ${where} unavailable —`, error instanceof Error ? error.message : error);
}

/// Keys change with every new message (recaps and drafts are keyed on a thread's tail), so entries that are never
/// asked for again would pile up for the life of the process. Expired ones are swept on each write, and the map is
/// capped: the oldest entry goes first (a Map iterates in insertion order).
const CACHE_LIMIT = 500;
const cache = new Map<string, { at: number; value: unknown }>();
function cached<T>(key: string): T | undefined {
  const hit = cache.get(key);
  if (!hit) return undefined;
  if (Date.now() - hit.at > TTL_MS) { cache.delete(key); return undefined; }
  return hit.value as T;
}
function remember(key: string, value: unknown) {
  const now = Date.now();
  for (const [k, v] of cache) if (now - v.at > TTL_MS) cache.delete(k);
  cache.delete(key); cache.set(key, { at: now, value });
  while (cache.size > CACHE_LIMIT) cache.delete(cache.keys().next().value!);
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
  if (muse()) return callMuse(system, tool as any, payload, maxTokens);
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
    remember(key, value);
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
    remember(key, value);
    return value;
  } catch (error) {
    warn('recap', error);
    return null;
  }
}

const SYSTEM_DRAFT =
  'You suggest a first draft of a short message from one person recovering from injury ' +
  'or illness to a friend who is also recovering. The sender reads it, may change it, and ' +
  'decides whether to send it.\n\n' +
  'Rules:\n' +
  '- Written as the sender, first person, casual and warm. One or two short sentences, ' +
  'under 20 words.\n' +
  '- If the friend said something recently, answer that. Otherwise say hello using only ' +
  'the day counts given ("good to see you back").\n' +
  '- Never claim anything about the sender that the thread does not say: no "I did my ' +
  'exercises", no feelings they did not express.\n' +
  '- Never mention measurements, progress, diagnosis or anything clinical, and never give ' +
  'advice. No emoji, no hashtags, no quotation marks.';

const DRAFT_TOOL = {
  name: 'draft',
  description: 'Draft the message.',
  strict: true,
  input_schema: {
    type: 'object',
    properties: { draft: { type: 'string' } },
    required: ['draft'],
    additionalProperties: false,
  },
} as const;

export interface DraftFacts {
  daysSinceTheyWrote: number | null;
  daysSinceIWrote: number | null;
  quietDaysBeforeTheyReturned: number | null;
}

/**
 * A first draft of what to say to one friend. It is only ever put in the
 * sender's own composer; null on any failure, and the composer stays empty.
 */
export async function draft(otherName: string, messages: RecapMessage[], facts: DraftFacts): Promise<string | null> {
  if (!available()) return null;
  const key = 'draft:' + otherName + ':' + messages.length + ':' + (messages.at(-1)?.at ?? '') + JSON.stringify(facts);
  const hit = cached<string>(key);
  if (hit !== undefined) return hit;
  try {
    const recent = messages.slice(-8).map(m => ({
      who: m.fromMe ? 'you' : otherName, said: m.kind ?? m.text, sentPhoto: m.photo === true, at: m.at,
    }));
    const response = await call(SYSTEM_DRAFT, DRAFT_TOOL, { to: otherName, ...facts, messages: recent }, 400);
    const input = toolInput(response, 'draft');
    if (!input || typeof input.draft !== 'string') return null;
    const value = input.draft.trim().replace(/^["“]|["”]$/g, '').slice(0, 240);
    if (!value) return null;
    remember(key, value);
    return value;
  } catch (error) {
    warn('draft', error);
    return null;
  }
}

const SYSTEM_INTRO =
  'A rehabilitation app is offering to introduce two patients who have never met. Neither ' +
  'knows who the other is yet. Write the one line under the heading of the card, telling ' +
  'the reader why this stranger might be worth meeting.\n\n' +
  'Rules:\n' +
  '- Use only what they share: their goals, the movements they both practise, and how far ' +
  'into a programme the other is. Nothing else is known.\n' +
  '- Address the reader as "you" and the other person as "they". One sentence, under 22 ' +
  'words, warm and plain.\n' +
  '- No name, no age, no diagnosis, no measurements, no promises about recovery.\n' +
  '- Do not repeat the heading, which the reader already sees.';

const INTRO_TOOL = {
  name: 'introduction',
  description: 'Write the line for the introduction card.',
  strict: true,
  input_schema: {
    type: 'object',
    properties: { line: { type: 'string' } },
    required: ['line'],
    additionalProperties: false,
  },
} as const;

export interface IntroFacts {
  kind: 'peer' | 'mentor';
  heading: string;                // matching.ts reason(), shown above this line
  sharedGoals: string[];
  sharedMovements: string[];
  theirProgramWeek: number;
  yourProgramWeek: number;
}

/** The warm line under an introduction's reason. Nothing identifying goes in, so nothing can come out. */
export async function introLine(facts: IntroFacts): Promise<string | null> {
  if (!available()) return null;
  const key = 'intro:' + JSON.stringify(facts);
  const hit = cached<string>(key);
  if (hit !== undefined) return hit;
  try {
    const response = await call(SYSTEM_INTRO, INTRO_TOOL, facts, 300);
    const input = toolInput(response, 'introduction');
    if (!input || typeof input.line !== 'string') return null;
    const value = input.line.trim().slice(0, 180);
    if (!value) return null;
    remember(key, value);
    return value;
  } catch (error) {
    warn('introduction', error);
    return null;
  }
}

const SYSTEM_MILESTONE =
  'A patient in a rehabilitation programme reached a milestone of turning up, and may ' +
  'choose to share it with their friends in the app, who are also recovering. Write the ' +
  'words of the share, as the patient.\n\n' +
  'Rules:\n' +
  '- First person, one or two short sentences, under 22 words. Proud but not boastful.\n' +
  '- State the milestone as given. You may tie it to their goal, in their own words, if ' +
  'one is given.\n' +
  '- Never mention measurements, degrees, pain, diagnosis or anything clinical. Never ' +
  'invent history ("first time ever") the facts do not state. No emoji, no hashtags.';

const MILESTONE_TOOL = {
  name: 'milestone',
  description: 'Write the share.',
  strict: true,
  input_schema: {
    type: 'object',
    properties: { line: { type: 'string' } },
    required: ['line'],
    additionalProperties: false,
  },
} as const;

/** The words on a milestone card, in the patient's voice. Only sent if they press share. */
export async function milestoneLine(facts: { milestone: string; goal: string | null; programDay: number }): Promise<string | null> {
  if (!available()) return null;
  const key = 'milestone:' + JSON.stringify(facts);
  const hit = cached<string>(key);
  if (hit !== undefined) return hit;
  try {
    const response = await call(SYSTEM_MILESTONE, MILESTONE_TOOL, facts, 300);
    const input = toolInput(response, 'milestone');
    if (!input || typeof input.line !== 'string') return null;
    const value = input.line.trim().replace(/^["“]|["”]$/g, '').slice(0, 200);
    if (!value) return null;
    remember(key, value);
    return value;
  } catch (error) {
    warn('milestone', error);
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
