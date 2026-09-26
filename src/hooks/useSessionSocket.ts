import { useEffect, useRef, useState } from 'react'

export type SocketStatus = 'connecting' | 'open' | 'closed' | 'error'

export interface RawMessage {
  ts: number
  data: unknown
}

interface UseSessionSocketOptions {
  url: string
  enabled?: boolean
}

export function useSessionSocket({ url, enabled = true }: UseSessionSocketOptions) {
  const [status, setStatus] = useState<SocketStatus>('closed')
  const [messages, setMessages] = useState<RawMessage[]>([])
  const [lastMessage, setLastMessage] = useState<RawMessage | null>(null)
  const ws = useRef<WebSocket | null>(null)

  useEffect(() => {
    if (!enabled) return

    setStatus('connecting')

    try {
      ws.current = new WebSocket(url)
    } catch {
      setStatus('error')
      return
    }

    ws.current.onopen = () => setStatus('open')

    ws.current.onmessage = (event) => {
      try {
        const parsed = JSON.parse(event.data as string) as unknown
        const msg: RawMessage = { ts: Date.now(), data: parsed }
        setLastMessage(msg)
        setMessages(prev => [...prev.slice(-199), msg]) // keep last 200
      } catch {
        // non-JSON frame — ignore
      }
    }

    ws.current.onerror = () => setStatus('error')
    ws.current.onclose = () => setStatus('closed')

    return () => {
      ws.current?.close()
    }
  }, [url, enabled])

  function send(data: unknown) {
    if (ws.current?.readyState === WebSocket.OPEN) {
      ws.current.send(JSON.stringify(data))
    }
  }

  return { status, messages, lastMessage, send }
}
