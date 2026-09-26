using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Kinesthetic.UI.Remote
{
    /// A WebSocket client for the `/ui` channel. Golf's LatestSocket keeps only the newest message in each
    /// direction, which is right for state that supersedes itself and wrong for patches: a patch is meant
    /// against the rev before it, and a press that is coalesced with the next one is a press lost. So both
    /// directions here are queues, drained in order, and nothing is skipped.
    ///
    /// Network work stays off the main thread; the owner drains `TryReceive` from Update. The send loop
    /// sleeps on a semaphore that `Send` signals, rather than polling. It reconnects a second after any
    /// failure. What was queued for a socket that died is dropped, because both ends start over on
    /// reconnect — the relay tells the host to resync, the client asks for one, and presses live in the
    /// client's own outbox with their own retry.
    public sealed class UiSocket : IDisposable
    {
        const int Chunk = 64 * 1024;
        const int MaxIncoming = 4 * 1024 * 1024;

        readonly CancellationTokenSource cancel = new();
        readonly ConcurrentQueue<string> outgoing = new(), incoming = new();
        readonly SemaphoreSlim wake = new(0);

        public volatile bool connected;

        /// Counts up on each successful connect, so an owner can notice a reconnect and reset.
        public volatile int generation;

        public UiSocket(string url) : this(() => url) { }
        /// `url` is asked before every attempt (QuestHostConfig.Url: over the USB cable or the network).
        public UiSocket(Func<string> url) { _ = Task.Run(() => Run(url)); }

        public void Send(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            outgoing.Enqueue(text);
            wake.Release();
        }

        public bool TryReceive(out string text) => incoming.TryDequeue(out text);

        async Task Run(Func<string> url)
        {
            // One waiter on the semaphore, kept across connections: a wait that a closing socket left
            // pending must not swallow the signal meant for the next one.
            Task woke = null;
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    // A dropped-packet address holds a connect for minutes on device; give up after 3 s.
                    var connecting = ws.ConnectAsync(new Uri(url()), cancel.Token);
                    if (await Task.WhenAny(connecting, Task.Delay(3000, cancel.Token)).ConfigureAwait(false) != connecting)
                    {
                        ws.Abort(); _ = connecting.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                        throw new TimeoutException("no answer in 3 s");
                    }
                    await connecting.ConfigureAwait(false);
                    while (outgoing.TryDequeue(out _)) { }
                    generation++;
                    connected = true;

                    var receive = Task.Run(() => Receive(ws));
                    while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested && !receive.IsCompleted)
                    {
                        woke ??= wake.WaitAsync(cancel.Token);
                        await Task.WhenAny(woke, receive).ConfigureAwait(false);
                        if (woke.IsCompleted) woke = null;
                        while (outgoing.TryDequeue(out var text))
                            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                    }
                    try { await receive.ConfigureAwait(false); }
                    catch (Exception e) when (!(e is OperationCanceledException)) { Debug.Log("Remote UI socket receive ended: " + e.Message); }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { if (connected) Debug.Log("Remote UI socket: " + e.Message); }
                connected = false;
                try { await Task.Delay(1000, cancel.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }

        async Task Receive(ClientWebSocket ws)
        {
            var buffer = new byte[Chunk];
            while (ws.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxIncoming)
                    {
                        // Nothing legitimate is this big; the connection is dropped and rebuilt rather than trusted.
                        Debug.LogError("Remote UI socket: a message over 4 MiB was refused; reconnecting.");
                        throw new InvalidDataException("message over 4 MiB");
                    }
                } while (!result.EndOfMessage);
                incoming.Enqueue(Encoding.UTF8.GetString(message.ToArray()));
            }
        }

        public void Dispose() => cancel.Cancel();
    }
}
