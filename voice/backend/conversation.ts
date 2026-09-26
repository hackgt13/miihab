/**
 * ElevenLabs Conversational AI — WebSocket bridge.
 *
 * Implements the ElevenLabs wire protocol so the controller layer
 * doesn't need to know any protocol details.
 *
 * Audio flow:
 *   Unity → sendAudio(pcm) → ElevenLabs
 *   ElevenLabs → onAudio(chunk) → Unity
 */

import { WebSocket } from "ws";

export interface ConversationOptions {
  agentId: string;
  apiKey: string;
  onAudio: (chunk: Buffer) => void;
  onTranscript: (role: "agent" | "user", text: string) => void;
  onToolCall: (
    toolCallId: string,
    toolName: string,
    params: Record<string, unknown>,
  ) => Promise<unknown>;
  onInterrupt: () => void;
  onError: (message: string) => void;
  onClose: () => void;
}

export class ElevenLabsConversation {
  private ws: WebSocket | null = null;
  private opts: ConversationOptions;
  private closed = false;

  constructor(opts: ConversationOptions) {
    this.opts = opts;
  }

  async start(): Promise<void> {
    console.log('[conv] fetching signed URL');
    const signedUrl = await this.fetchSignedUrl();
    console.log('[conv] signed URL ok — opening WS');
    this.ws = new WebSocket(signedUrl);

    this.ws.on("message", async (data: Buffer) => {
      let msg: Record<string, unknown>;
      try {
        msg = JSON.parse(data.toString()) as Record<string, unknown>;
      } catch {
        return;
      }
      console.log('[conv] message:', msg['type']);
      await this.handleMessage(msg);
    });

    this.ws.on("error", (err: Error) => {
      console.error('[conv] WS error:', err.message);
      if (!this.closed) this.opts.onError(err.message);
    });

    this.ws.on("close", (code: number, reason: Buffer) => {
      console.log('[conv] WS closed:', code, reason.toString());
      if (!this.closed) {
        this.closed = true;
        this.opts.onClose();
      }
    });

    await new Promise<void>((resolve, reject) => {
      this.ws!.once("open", () => {
        console.log('[conv] WS open');
        // Signal client readiness — required to start the conversation
        this.send({ type: 'conversation_initiation_client_data' });
        resolve();
      });
      this.ws!.once("error", reject);
    });
  }

  sendAudio(pcm: Buffer): void {
    this.send({
      type: "user_audio_chunk",
      user_audio_chunk: pcm.toString("base64"),
    });
  }

  end(): void {
    if (!this.closed) {
      this.closed = true;
      this.ws?.close();
    }
  }

  // ── private ───────────────────────────────────────────────────────────────

  private async handleMessage(msg: Record<string, unknown>): Promise<void> {
    switch (msg["type"]) {
      case "audio": {
        const b64 = (msg["audio_event"] as Record<string, unknown>)?.[
          "audio_base_64"
        ] as string;
        if (b64) this.opts.onAudio(Buffer.from(b64, "base64"));
        break;
      }
      case "agent_response": {
        const text = (msg["agent_response_event"] as Record<string, unknown>)?.[
          "agent_response"
        ] as string;
        if (text) this.opts.onTranscript("agent", text);
        break;
      }
      case "user_transcript": {
        const text = (
          msg["user_transcription_event"] as Record<string, unknown>
        )?.["user_transcript"] as string;
        if (text) this.opts.onTranscript("user", text);
        break;
      }
      case "client_tool_call": {
        const call = msg["client_tool_call"] as Record<string, unknown>;
        const toolCallId = call["tool_call_id"] as string;
        const toolName = call["tool_name"] as string;
        const parameters =
          (call["parameters"] as Record<string, unknown>) ?? {};
        try {
          const result = await this.opts.onToolCall(
            toolCallId,
            toolName,
            parameters,
          );
          this.send({
            type: "client_tool_result",
            tool_call_id: toolCallId,
            result: JSON.stringify(result),
            is_error: false,
          });
        } catch (err) {
          this.send({
            type: "client_tool_result",
            tool_call_id: toolCallId,
            result: String(err),
            is_error: true,
          });
        }
        break;
      }
      case "interruption":
        this.opts.onInterrupt();
        break;
      case "ping": {
        const eventId = (msg["ping_event"] as Record<string, unknown>)?.[
          "event_id"
        ];
        this.send({ type: "pong", event_id: eventId });
        break;
      }
      // conversation_initiation_metadata — informational, no action needed
    }
  }

  private send(msg: Record<string, unknown>): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  private async fetchSignedUrl(): Promise<string> {
    const resp = await fetch(
      `https://api.elevenlabs.io/v1/convai/conversation/get_signed_url?agent_id=${encodeURIComponent(this.opts.agentId)}`,
      { headers: { "xi-api-key": this.opts.apiKey } },
    );
    if (!resp.ok) {
      throw new Error(
        `ElevenLabs signed URL failed: ${resp.status} ${resp.statusText}`,
      );
    }
    const body = (await resp.json()) as { signed_url: string };
    return body.signed_url;
  }
}
