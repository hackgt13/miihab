import { useState, useEffect } from 'react'
import { PrivacyPreview } from './PrivacyPreview'

interface Message {
  role: 'physician' | 'agent'
  text: string
  evidence?: string[]
  replayRef?: string
  missing?: string[]
  resource?: { label: string; type: string }
}

const INITIAL: Message[] = [
  {
    role: 'agent',
    text: "Session 14 handoff received. ROM improved 16° from baseline but trunk deviation reached 11° in the final reps - above the 8° limit. I've surfaced the replay moments. What would you like to review?",
    replayRef: '4:58 - trunk lean, rep 7',
  },
]

const CANNED: Record<string, Message> = {
  'Is this real arm improvement or compensation?': {
    role: 'agent',
    text: 'The ROM gain appears genuine - the trajectory is consistent across 14 sessions and not step-wise. However, trunk lateral flexion is now masking the true glenohumeral ceiling. The rep 7 replay shows the pattern most clearly.',
    evidence: [
      'Ludewig et al., JOSPT 2009 - scapular substitution in impingement; similar trunk-compensation signature.',
      'Kibler & McMullen, JAAOS 2003 - scapular dyskinesis as a proximal driver of apparent ROM gain.',
    ],
    replayRef: '4:58 - trunk lean, rep 7',
    missing: ['Passive ROM not recorded this session', 'Contralateral baseline not established'],
  },
  'Should I progress the target range?': {
    role: 'agent',
    text: 'Progressing target range while trunk deviation is rising may reinforce the compensation pattern. Standard guidance suggests resolving the movement fault first. A Plan v2 with the same ROM target but a tighter trunk-deviation constraint (≤ 6°) and a cue emphasis change is drafted below.',
    evidence: ['Kibler & McMullen, JAAOS 2003'],
    resource: { label: 'MSL Request - spasticity adjunct resources', type: 'MSL' },
  },
}

interface ExpertChatProps {
  preloadedContext?: string | null
  patientId: string
  conditionCategory: string
}

interface PrivacyState {
  resourceType: string
  question: string
}

export function ExpertChat({ preloadedContext, patientId, conditionCategory }: ExpertChatProps) {
  const [messages, setMessages] = useState<Message[]>(INITIAL)
  const [input, setInput]       = useState('')
  const [privacy, setPrivacy]   = useState<PrivacyState | null>(null)

  useEffect(() => {
    if (preloadedContext) setInput(preloadedContext)
  }, [preloadedContext])

  function send() {
    const q = input.trim()
    if (!q) return
    const physician: Message = { role: 'physician', text: q }
    const reply = CANNED[q] ?? {
      role: 'agent' as const,
      text: "I don't have a sourced answer for that yet. Add it to the missing-information list for the next session?",
    }
    setMessages(prev => [...prev, physician, reply])
    setInput('')
  }

  function openResource(type: string, label: string) {
    setPrivacy({ resourceType: `${type} - ${label}`, question: label })
  }

  return (
    <>
      <div className="bg-white border border-[#DDE2E8] rounded-md shadow-sm flex flex-col">
        <div className="px-4 py-3 border-b border-[#DDE2E8]">
          <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em]">Expert agent</p>
        </div>

        {/* Suggested prompts */}
        <div className="px-4 py-2 border-b border-[#DDE2E8] flex gap-2 flex-wrap">
          {Object.keys(CANNED).map(q => (
            <button
              key={q}
              onClick={() => setInput(q)}
              className="text-[10px] text-[#5A6472] border border-[#DDE2E8] rounded-sm px-2 py-1 hover:border-[#1666C0]/50 hover:text-[#1666C0] transition-colors"
            >
              {q}
            </button>
          ))}
        </div>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto px-4 py-3 flex flex-col gap-3 max-h-80">
          {messages.map((m, i) => (
            <div key={i} className={`flex flex-col gap-1.5 ${m.role === 'physician' ? 'items-end' : 'items-start'}`}>
              <p className={`text-[9px] uppercase tracking-widest ${m.role === 'physician' ? 'text-[#1666C0]' : 'text-[#0D9488]'}`}>
                {m.role === 'physician' ? 'You' : 'Expert Agent'}
              </p>
              <div className={`max-w-[85%] px-3 py-2 text-xs leading-relaxed rounded-md ${
                m.role === 'physician'
                  ? 'bg-[#1666C0]/10 border border-[#1666C0]/20 text-[#1A1D23]'
                  : 'bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23]'
              }`}>
                <p>{m.text}</p>
                {m.replayRef && (
                  <button className="mt-1.5 text-[10px] text-[#1666C0] hover:text-[#1255A3] transition-colors">
                    ▶ {m.replayRef}
                  </button>
                )}
                {m.evidence && m.evidence.length > 0 && (
                  <div className="mt-2 pt-2 border-t border-[#DDE2E8] flex flex-col gap-1">
                    {m.evidence.map((e, j) => (
                      <p key={j} className="text-[10px] text-[#5A6472] italic">{e}</p>
                    ))}
                  </div>
                )}
                {m.missing && m.missing.length > 0 && (
                  <div className="mt-2 pt-2 border-t border-[#DDE2E8]">
                    <p className="text-[9px] text-[#C67C1A] uppercase tracking-widest mb-1">Missing data</p>
                    {m.missing.map((s, j) => (
                      <p key={j} className="text-[10px] text-[#5A6472]">· {s}</p>
                    ))}
                  </div>
                )}
                {m.resource && (
                  <div className="mt-2 pt-2 border-t border-[#DDE2E8]">
                    <button
                      onClick={() => openResource(m.resource!.type, m.resource!.label)}
                      className="text-[10px] text-[#0D9488] border border-[#0D9488]/30 rounded-sm px-2 py-1 hover:bg-[#0D9488]/10 transition-colors"
                    >
                      <span className="text-[#C67C1A] mr-1">Simulated</span>
                      {m.resource.type} - {m.resource.label}
                    </button>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>

        {/* Input */}
        <div className="px-4 py-3 border-t border-[#DDE2E8] flex gap-2">
          <input
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && send()}
            placeholder="Ask about this session…"
            className="flex-1 bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] placeholder-[#5A6472] text-xs px-3 py-2 rounded-sm focus:outline-none focus:border-[#1666C0] transition-colors"
          />
          <button
            onClick={send}
            className="px-3 py-2 bg-[#1666C0] text-white text-xs font-semibold rounded-sm hover:bg-[#1255A3] transition-colors"
          >
            Ask
          </button>
        </div>
      </div>

      {privacy && (
        <PrivacyPreview
          resourceType={privacy.resourceType}
          question={privacy.question}
          conditionCategory={conditionCategory}
          patientId={patientId}
          onSend={() => setPrivacy(null)}
          onCancel={() => setPrivacy(null)}
        />
      )}
    </>
  )
}
