# Teams Media Bot with WebRTC/GStreamer Integration - Implementation Plan

## Executive Summary

This document outlines a comprehensive implementation plan for extending the PolicyRecordingBot sample to create a Teams media bot that forwards audio to/from a GStreamer application via WebRTC.

**Architecture:**
```
Teams Call → C# Bot (Azure VM) → WebRTC → GStreamer Service(s)
                ↑                            ↓
                └────── Audio Return ─────────┘
```

**Key Technologies:**
- Microsoft Graph Communications SDK (Teams integration)
- WebRTC (C# using SIPSorcery or WebRTC.NET)
- COTURN (STUN/TURN server)
- Custom WebSocket signaling
- GStreamer (audio processing)

---

## Phase 1: Architecture & Foundation (Week 1-2)

### 1.1 Core Architecture Decisions

#### Audio Flow Architecture
```
Teams → Bot Audio Socket → Audio Buffer Queue → WebRTC PeerConnection → RTP → GStreamer
         (PCM 16kHz)         (Thread-safe)        (Opus codec)              (Processing)

GStreamer → RTP → WebRTC PeerConnection → Audio Injection → Teams Audio Socket
             (Opus)                          (PCM 16kHz)
```

#### Component Breakdown

**C# Bot Components:**
1. **MediaBridge** - Manages audio flow between Teams and WebRTC
2. **WebRTCManager** - Handles peer connection lifecycle
3. **SignalingClient** - WebSocket-based signaling protocol
4. **AudioConverter** - Format conversion (PCM ↔ Opus)
5. **CallController** - Orchestrates call joining/leaving

**External Components:**
1. **COTURN Server** - STUN/TURN for NAT traversal
2. **Signaling Server** - WebSocket server for signaling
3. **GStreamer Service** - Audio processing application

### 1.2 Technology Stack Selection

#### WebRTC Library for C#
**Recommendation: SIPSorcery**
- Mature, actively maintained
- Full WebRTC stack (ICE, DTLS, SRTP, SDP)
- Built-in codec support (Opus, G.711)
- Good documentation and examples

**Alternative: WebRTC.NET (Google's wrapper)**
- More complete but heavier
- Windows-focused

**Code Reference:**
```csharp
// Install via NuGet
Install-Package SIPSorcery -Version 6.0.0+
Install-Package SIPSorceryMedia.Abstractions
```

#### Audio Codec Strategy
- **Teams → GStreamer**: Convert PCM16K to Opus for bandwidth efficiency
- **GStreamer → Teams**: Accept Opus, convert to PCM16K
- **Buffer Size**: Match Teams' 640-byte frames (~20ms @ 16kHz)

### 1.3 COTURN Server Deployment

#### Cloud vs Local Decision Matrix

| Aspect | Cloud COTURN | Local COTURN |
|--------|-------------|--------------|
| **Latency** | Medium (Azure region) | Low (LAN) |
| **Reliability** | High (Azure SLA) | Medium (depends on network) |
| **Cost** | ~$50-100/mo | Free (bandwidth costs) |
| **NAT Traversal** | Excellent | May struggle with symmetric NAT |
| **Maintenance** | Higher (Azure config) | Lower (simple setup) |

**Recommendation: Start with Cloud, add Local as fallback**

**Cloud Deployment (Azure VM):**
```bash
# Ubuntu 20.04 LTS, Standard B2s
sudo apt update
sudo apt install coturn

# /etc/turnserver.conf
listening-port=3478
tls-listening-port=5349
realm=your-domain.com
server-name=turn.your-domain.com
lt-cred-mech
user=botuser:strongpassword
verbose
```

---

## Phase 2: Bot Extension (Week 2-3)

### 2.1 Project Structure

```
PolicyRecordingBot/
├── FrontEnd/
│   ├── Bot/
│   │   ├── BotMediaStream.cs         [MODIFY]
│   │   ├── CallHandler.cs            [MODIFY]
│   │   ├── Bot.cs                    [MODIFY]
│   │   └── WebRTC/                   [NEW]
│   │       ├── MediaBridge.cs
│   │       ├── WebRTCManager.cs
│   │       ├── AudioConverter.cs
│   │       └── PeerConnectionFactory.cs
│   ├── Signaling/                    [NEW]
│   │   ├── SignalingClient.cs
│   │   ├── SignalingProtocol.cs
│   │   └── Messages/
│   │       ├── JoinCallMessage.cs
│   │       ├── OfferMessage.cs
│   │       └── IceCandidateMessage.cs
│   └── Http/
│       └── Controllers/
│           └── CallControlController.cs  [NEW]
```

### 2.2 Modify BotMediaStream.cs

**Current Implementation (Line 181-187):**
```csharp
private void OnAudioMediaReceived(object sender, AudioMediaReceivedEventArgs e)
{
    this.GraphLogger.Info($"Received Audio: [Length={e.Buffer.Length}, Timestamp={e.Buffer.Timestamp}]");

    // TBD: Policy Recording bots can record the Audio here
    e.Buffer.Dispose();
}
```

**New Implementation:**
```csharp
private readonly MediaBridge mediaBridge;
private readonly object audioBufferLock = new object();

public BotMediaStream(ILocalMediaSession mediaSession, IGraphLogger logger, MediaBridge bridge)
    : base(logger)
{
    // ... existing code ...
    this.mediaBridge = bridge;
}

private void OnAudioMediaReceived(object sender, AudioMediaReceivedEventArgs e)
{
    try
    {
        // Critical: Must marshal buffer to managed memory before disposing
        byte[] audioData = new byte[e.Buffer.Length];
        Marshal.Copy(e.Buffer.Data, audioData, 0, (int)e.Buffer.Length);

        // Teams provides PCM 16kHz, 16-bit, mono
        // Buffer is typically 640 bytes = 320 samples = 20ms @ 16kHz

        this.GraphLogger.Verbose($"Audio received: {e.Buffer.Length} bytes, TS: {e.Buffer.Timestamp}");

        // Forward to WebRTC bridge (non-blocking)
        this.mediaBridge.SendAudioToWebRTC(audioData, e.Buffer.Timestamp);
    }
    catch (Exception ex)
    {
        this.GraphLogger.Error(ex, "Failed to process audio buffer");
    }
    finally
    {
        // CRITICAL: Always dispose buffer
        e.Buffer.Dispose();
    }
}

// New method: Receive audio from WebRTC to inject into Teams
public void SendAudioToTeams(byte[] pcmData)
{
    // Audio injection happens through IAudioSocket.Send()
    // Note: Current PolicyRecordingBot is receive-only
    // Need to modify MediaConfiguration to enable sending
    var audioSocket = this.mediaSession.AudioSocket;

    if (audioSocket is IAudioSendSocket sendSocket)
    {
        // Create AudioSendBuffer and send
        // This requires modifying the media session configuration
        sendSocket.Send(pcmData);
    }
}
```

**Key Implementation Notes:**
1. **Buffer Marshaling**: Must copy unmanaged buffer to managed memory immediately
2. **Thread Safety**: Audio callbacks fire on media platform threads
3. **Performance**: Keep processing minimal (< 1ms per callback)
4. **Disposal**: Buffer MUST be disposed even on exception

### 2.3 Create MediaBridge.cs

```csharp
namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Graph.Communications.Common.Telemetry;

    /// <summary>
    /// Bridges audio between Teams media streams and WebRTC peer connections.
    /// Thread-safe, handles buffering and format conversion.
    /// </summary>
    public class MediaBridge : IDisposable
    {
        private readonly IGraphLogger logger;
        private readonly WebRTCManager webRtcManager;
        private readonly AudioConverter audioConverter;

        // Bounded queue to prevent memory buildup if WebRTC is slow
        private readonly BlockingCollection<AudioFrame> teamsToWebRtcQueue;
        private readonly BlockingCollection<AudioFrame> webRtcToTeamsQueue;

        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly Task processingTask;

        private BotMediaStream botMediaStream;

        public MediaBridge(IGraphLogger logger, WebRTCManager webRtcManager)
        {
            this.logger = logger;
            this.webRtcManager = webRtcManager;
            this.audioConverter = new AudioConverter(logger);

            // Limit queue to ~1 second of audio (50 frames @ 20ms each)
            this.teamsToWebRtcQueue = new BlockingCollection<AudioFrame>(50);
            this.webRtcToTeamsQueue = new BlockingCollection<AudioFrame>(50);

            this.cancellationTokenSource = new CancellationTokenSource();

            // Start processing threads
            this.processingTask = Task.WhenAll(
                this.ProcessTeamsToWebRtcAsync(this.cancellationTokenSource.Token),
                this.ProcessWebRtcToTeamsAsync(this.cancellationTokenSource.Token)
            );

            // Subscribe to WebRTC audio
            this.webRtcManager.OnAudioReceived += this.OnWebRtcAudioReceived;
        }

        public void AttachBotMediaStream(BotMediaStream stream)
        {
            this.botMediaStream = stream;
        }

        /// <summary>
        /// Called by BotMediaStream when audio arrives from Teams.
        /// </summary>
        public void SendAudioToWebRTC(byte[] pcmData, long timestamp)
        {
            var frame = new AudioFrame
            {
                Data = pcmData,
                Timestamp = timestamp,
                Format = AudioFormat.PCM16kHz
            };

            // Non-blocking add with timeout
            if (!this.teamsToWebRtcQueue.TryAdd(frame, TimeSpan.FromMilliseconds(10)))
            {
                this.logger.Warn("Teams->WebRTC queue full, dropping frame");
            }
        }

        /// <summary>
        /// Called by WebRTCManager when audio arrives from GStreamer.
        /// </summary>
        private void OnWebRtcAudioReceived(byte[] opusData, uint timestamp)
        {
            var frame = new AudioFrame
            {
                Data = opusData,
                Timestamp = timestamp,
                Format = AudioFormat.Opus
            };

            if (!this.webRtcToTeamsQueue.TryAdd(frame, TimeSpan.FromMilliseconds(10)))
            {
                this.logger.Warn("WebRTC->Teams queue full, dropping frame");
            }
        }

        /// <summary>
        /// Processing loop: Teams PCM → Opus → WebRTC
        /// </summary>
        private async Task ProcessTeamsToWebRtcAsync(CancellationToken token)
        {
            this.logger.Info("MediaBridge: Teams->WebRTC processing started");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var frame = this.teamsToWebRtcQueue.Take(token);

                    // Convert PCM to Opus
                    var opusData = this.audioConverter.ConvertPcmToOpus(frame.Data);

                    // Send via WebRTC
                    await this.webRtcManager.SendAudioAsync(opusData);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error processing Teams->WebRTC audio");
                }
            }

            this.logger.Info("MediaBridge: Teams->WebRTC processing stopped");
        }

        /// <summary>
        /// Processing loop: WebRTC → Opus → PCM → Teams
        /// </summary>
        private async Task ProcessWebRtcToTeamsAsync(CancellationToken token)
        {
            this.logger.Info("MediaBridge: WebRTC->Teams processing started");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var frame = this.webRtcToTeamsQueue.Take(token);

                    // Convert Opus to PCM
                    var pcmData = this.audioConverter.ConvertOpusToPcm(frame.Data);

                    // Send to Teams via BotMediaStream
                    this.botMediaStream?.SendAudioToTeams(pcmData);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error processing WebRTC->Teams audio");
                }
            }

            this.logger.Info("MediaBridge: WebRTC->Teams processing stopped");
        }

        public void Dispose()
        {
            this.cancellationTokenSource.Cancel();
            this.processingTask.Wait(TimeSpan.FromSeconds(5));

            this.webRtcManager.OnAudioReceived -= this.OnWebRtcAudioReceived;

            this.teamsToWebRtcQueue.Dispose();
            this.webRtcToTeamsQueue.Dispose();
            this.audioConverter.Dispose();
            this.cancellationTokenSource.Dispose();
        }
    }

    public class AudioFrame
    {
        public byte[] Data { get; set; }
        public long Timestamp { get; set; }
        public AudioFormat Format { get; set; }
    }

    public enum AudioFormat
    {
        PCM16kHz,
        Opus
    }
}
```

### 2.4 Create WebRTCManager.cs

```csharp
namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Graph.Communications.Common.Telemetry;
    using SIPSorcery.Net;
    using SIPSorceryMedia.Abstractions;

    /// <summary>
    /// Manages WebRTC peer connections with GStreamer services.
    /// Handles ICE, DTLS, and RTP/RTCP.
    /// </summary>
    public class WebRTCManager : IDisposable
    {
        private readonly IGraphLogger logger;
        private readonly SignalingClient signalingClient;
        private readonly PeerConnectionFactory peerConnectionFactory;

        private RTCPeerConnection peerConnection;
        private string currentCallId;

        public event Action<byte[], uint> OnAudioReceived;

        public WebRTCManager(IGraphLogger logger, SignalingClient signalingClient, string turnServerUrl, string turnUsername, string turnPassword)
        {
            this.logger = logger;
            this.signalingClient = signalingClient;
            this.peerConnectionFactory = new PeerConnectionFactory(logger, turnServerUrl, turnUsername, turnPassword);

            // Subscribe to signaling events
            this.signalingClient.OnOfferReceived += this.OnOfferReceivedAsync;
            this.signalingClient.OnAnswerReceived += this.OnAnswerReceivedAsync;
            this.signalingClient.OnIceCandidateReceived += this.OnIceCandidateReceivedAsync;
        }

        /// <summary>
        /// Initiate WebRTC connection for a Teams call.
        /// </summary>
        public async Task<bool> CreateConnectionAsync(string callId)
        {
            try
            {
                this.currentCallId = callId;
                this.logger.Info($"Creating WebRTC connection for call {callId}");

                // Create peer connection
                this.peerConnection = this.peerConnectionFactory.CreatePeerConnection();

                // Add audio track
                var audioTrack = new MediaStreamTrack(
                    SDPMediaTypesEnum.audio,
                    false,
                    new[] { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.OPUS) },
                    MediaStreamStatusEnum.SendRecv
                );
                this.peerConnection.addTrack(audioTrack);

                // Setup event handlers
                this.peerConnection.onicecandidate += this.OnIceCandidate;
                this.peerConnection.onconnectionstatechange += this.OnConnectionStateChange;
                this.peerConnection.OnRtpPacketReceived += this.OnRtpPacketReceived;

                // Create offer
                var offer = this.peerConnection.createOffer(null);
                await this.peerConnection.setLocalDescription(offer);

                // Send offer via signaling
                await this.signalingClient.SendOfferAsync(callId, offer.sdp);

                return true;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"Failed to create WebRTC connection for call {callId}");
                return false;
            }
        }

        /// <summary>
        /// Send audio to GStreamer via WebRTC.
        /// </summary>
        public async Task SendAudioAsync(byte[] opusData)
        {
            if (this.peerConnection?.connectionState == RTCPeerConnectionState.connected)
            {
                // SIPSorcery will handle RTP packetization
                var audioTrack = this.peerConnection.GetSendingTracks().FirstOrDefault();
                if (audioTrack != null)
                {
                    // Send as RTP packet
                    await audioTrack.SendAudio((uint)opusData.Length, opusData);
                }
            }
        }

        private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                // Extract Opus payload
                this.OnAudioReceived?.Invoke(rtpPacket.Payload, rtpPacket.Header.Timestamp);
            }
        }

        private void OnIceCandidate(RTCIceCandidate candidate)
        {
            this.logger.Info($"ICE candidate: {candidate.candidate}");
            _ = this.signalingClient.SendIceCandidateAsync(this.currentCallId, candidate.toJSON());
        }

        private void OnConnectionStateChange(RTCPeerConnectionState state)
        {
            this.logger.Info($"WebRTC connection state: {state}");

            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
            {
                this.logger.Warn($"WebRTC connection {state}, attempting reconnect");
                // Implement reconnection logic
            }
        }

        private async Task OnOfferReceivedAsync(string callId, string sdp)
        {
            // Handle incoming offers (if GStreamer initiates)
            var offerSdp = new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp };
            await this.peerConnection.setRemoteDescription(offerSdp);

            var answer = this.peerConnection.createAnswer(null);
            await this.peerConnection.setLocalDescription(answer);
            await this.signalingClient.SendAnswerAsync(callId, answer.sdp);
        }

        private async Task OnAnswerReceivedAsync(string callId, string sdp)
        {
            var answerSdp = new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp };
            await this.peerConnection.setRemoteDescription(answerSdp);
        }

        private async Task OnIceCandidateReceivedAsync(string callId, string candidateJson)
        {
            var candidate = RTCIceCandidate.Parse(candidateJson);
            await this.peerConnection.addIceCandidate(candidate);
        }

        public void Dispose()
        {
            this.peerConnection?.close();
            this.peerConnection?.Dispose();
        }
    }
}
```

### 2.5 Enable Audio Sending (Modify Media Configuration)

**Current Configuration (Receive-only):**
The PolicyRecordingBot configures media as receive-only. We need to enable bidirectional audio.

**File:** `FrontEnd/Bot/CallHandler.cs` or where media configuration happens

**Modification Required:**
```csharp
// In the media session configuration
var mediaConfiguration = MediaPlatform.CreateMediaConfiguration(
    new AudioSocketSettings
    {
        StreamDirections = StreamDirection.Sendrecv,  // Changed from Recvonly
        SupportedAudioFormat = AudioFormat.Pcm16K,
        ReceiveUnmixedMeetingAudio = true
    },
    // ... video settings ...
);
```

**Implementation Notes:**
- This enables the bot to send audio back to Teams participants
- Requires additional Azure configuration for media endpoints
- May need firewall rules for bidirectional RTP

---

## Phase 3: Signaling Protocol (Week 3)

### 3.1 Signaling Architecture

**Protocol Choice: WebSocket (Custom Protocol)**
- Low latency for real-time signaling
- Bidirectional, full-duplex
- Easy to implement in both C# and Python/GStreamer

**Alternative Options:**
- MQTT (overkill for point-to-point)
- gRPC (more complex, better for microservices)
- REST polling (too slow)

### 3.2 Message Protocol

**Message Types:**
```json
{
  "type": "join_call",
  "callId": "19:meeting_xxxx",
  "botId": "bot-instance-1",
  "timestamp": 1699564800
}

{
  "type": "offer",
  "callId": "19:meeting_xxxx",
  "sdp": "v=0\r\no=- 123456 2 IN IP4 0.0.0.0..."
}

{
  "type": "answer",
  "callId": "19:meeting_xxxx",
  "sdp": "v=0\r\no=- 654321 2 IN IP4 0.0.0.0..."
}

{
  "type": "ice_candidate",
  "callId": "19:meeting_xxxx",
  "candidate": {
    "candidate": "candidate:1 1 UDP 2130706431 192.168.1.100 54321 typ host",
    "sdpMid": "0",
    "sdpMLineIndex": 0
  }
}

{
  "type": "leave_call",
  "callId": "19:meeting_xxxx",
  "reason": "call_ended"
}

{
  "type": "error",
  "callId": "19:meeting_xxxx",
  "error": "connection_failed",
  "message": "ICE connection timeout"
}
```

### 3.3 SignalingClient.cs Implementation

```csharp
namespace Sample.PolicyRecordingBot.FrontEnd.Signaling
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Graph.Communications.Common.Telemetry;

    public class SignalingClient : IDisposable
    {
        private readonly IGraphLogger logger;
        private readonly string signalingServerUrl;
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private Task receiveTask;

        public event Func<string, string, Task> OnOfferReceived;
        public event Func<string, string, Task> OnAnswerReceived;
        public event Func<string, string, Task> OnIceCandidateReceived;

        public SignalingClient(IGraphLogger logger, string signalingServerUrl)
        {
            this.logger = logger;
            this.signalingServerUrl = signalingServerUrl;
        }

        public async Task ConnectAsync()
        {
            this.webSocket = new ClientWebSocket();
            this.cancellationTokenSource = new CancellationTokenSource();

            await this.webSocket.ConnectAsync(new Uri(this.signalingServerUrl), this.cancellationTokenSource.Token);
            this.logger.Info($"Connected to signaling server: {this.signalingServerUrl}");

            this.receiveTask = this.ReceiveMessagesAsync(this.cancellationTokenSource.Token);
        }

        public async Task SendJoinCallAsync(string callId)
        {
            var message = new
            {
                type = "join_call",
                callId = callId,
                botId = Environment.MachineName,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await this.SendMessageAsync(message);
        }

        public async Task SendOfferAsync(string callId, string sdp)
        {
            var message = new
            {
                type = "offer",
                callId = callId,
                sdp = sdp
            };

            await this.SendMessageAsync(message);
        }

        public async Task SendAnswerAsync(string callId, string sdp)
        {
            var message = new
            {
                type = "answer",
                callId = callId,
                sdp = sdp
            };

            await this.SendMessageAsync(message);
        }

        public async Task SendIceCandidateAsync(string callId, string candidateJson)
        {
            var message = new
            {
                type = "ice_candidate",
                callId = callId,
                candidate = JsonSerializer.Deserialize<object>(candidateJson)
            };

            await this.SendMessageAsync(message);
        }

        private async Task SendMessageAsync(object message)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            await this.webSocket.SendAsync(segment, WebSocketMessageType.Text, true, this.cancellationTokenSource.Token);
            this.logger.Verbose($"Sent signaling message: {json}");
        }

        private async Task ReceiveMessagesAsync(CancellationToken token)
        {
            var buffer = new byte[4096];

            while (!token.IsCancellationRequested && this.webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await this.webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        break;
                    }

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    this.logger.Verbose($"Received signaling message: {json}");

                    await this.ProcessMessageAsync(json);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error receiving signaling message");
                }
            }
        }

        private async Task ProcessMessageAsync(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var messageType = root.GetProperty("type").GetString();
            var callId = root.GetProperty("callId").GetString();

            switch (messageType)
            {
                case "offer":
                    var offerSdp = root.GetProperty("sdp").GetString();
                    await this.OnOfferReceived?.Invoke(callId, offerSdp);
                    break;

                case "answer":
                    var answerSdp = root.GetProperty("sdp").GetString();
                    await this.OnAnswerReceived?.Invoke(callId, answerSdp);
                    break;

                case "ice_candidate":
                    var candidateJson = root.GetProperty("candidate").GetRawText();
                    await this.OnIceCandidateReceived?.Invoke(callId, candidateJson);
                    break;

                default:
                    this.logger.Warn($"Unknown signaling message type: {messageType}");
                    break;
            }
        }

        public void Dispose()
        {
            this.cancellationTokenSource?.Cancel();
            this.receiveTask?.Wait(TimeSpan.FromSeconds(5));
            this.webSocket?.Dispose();
            this.cancellationTokenSource?.Dispose();
        }
    }
}
```

### 3.4 Signaling Server (Python/Node.js)

**Simple WebSocket Server (Python with asyncio):**
```python
# signaling_server.py
import asyncio
import json
import websockets
from collections import defaultdict

# Store connections by call ID
call_connections = defaultdict(set)

async def handle_client(websocket, path):
    client_id = id(websocket)
    print(f"Client {client_id} connected")

    try:
        async for message in websocket:
            data = json.loads(message)
            msg_type = data.get("type")
            call_id = data.get("callId")

            if msg_type == "join_call":
                call_connections[call_id].add(websocket)
                print(f"Client {client_id} joined call {call_id}")

            elif call_id:
                # Forward message to other participants in the call
                for client in call_connections[call_id]:
                    if client != websocket:
                        await client.send(message)

    except websockets.exceptions.ConnectionClosed:
        print(f"Client {client_id} disconnected")
    finally:
        # Remove from all calls
        for connections in call_connections.values():
            connections.discard(websocket)

async def main():
    async with websockets.serve(handle_client, "0.0.0.0", 8765):
        print("Signaling server running on ws://0.0.0.0:8765")
        await asyncio.Future()  # Run forever

if __name__ == "__main__":
    asyncio.run(main())
```

**Deployment:**
```bash
pip install websockets
python signaling_server.py

# Or with systemd
sudo systemctl start signaling-server
```

---

## Phase 4: Call Control & Orchestration (Week 4)

### 4.1 Call Joining Mechanism

**Options for Bot Joining Calls:**

1. **Policy-Based Recording (Current Sample)**
   - Automatic join via compliance policy
   - No user interaction required
   - Requires tenant admin configuration

2. **Application-Initiated Join (Recommended)**
   - Bot receives join request via API/webhook
   - Bot calls Graph API to join meeting
   - More control, easier testing

3. **User-Invited Bot**
   - Users add bot to meeting
   - Simplest for testing
   - Limited automation

**Recommendation: Start with #3 for testing, implement #2 for production**

### 4.2 Call Control API

**Create HTTP endpoint to trigger call joining:**

```csharp
namespace Sample.PolicyRecordingBot.FrontEnd.Http.Controllers
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Graph.Communications.Common.Telemetry;
    using Sample.PolicyRecordingBot.FrontEnd.Bot;

    [ApiController]
    [Route("api/[controller]")]
    public class CallControlController : ControllerBase
    {
        private readonly Bot bot;
        private readonly IGraphLogger logger;

        public CallControlController(Bot bot, IGraphLogger logger)
        {
            this.bot = bot;
            this.logger = logger;
        }

        [HttpPost("join")]
        public async Task<IActionResult> JoinCall([FromBody] JoinCallRequest request)
        {
            try
            {
                this.logger.Info($"Received join request for meeting: {request.MeetingUrl}");

                // Join the Teams call
                var call = await this.bot.JoinCallAsync(request.MeetingUrl, request.DisplayName);

                // Notify signaling server that bot joined call
                // This allows GStreamer to know when to connect

                return Ok(new
                {
                    callId = call.Id,
                    status = "joined",
                    message = "Bot successfully joined the call"
                });
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to join call");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("leave/{callId}")]
        public async Task<IActionResult> LeaveCall(string callId)
        {
            try
            {
                await this.bot.LeaveCallAsync(callId);
                return Ok(new { status = "left" });
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"Failed to leave call {callId}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class JoinCallRequest
    {
        public string MeetingUrl { get; set; }
        public string DisplayName { get; set; } = "Media Bot";
    }
}
```

### 4.3 Modify Bot.cs for Join Capability

**Add method to join call via Graph API:**

```csharp
// In Bot.cs
public async Task<ICall> JoinCallAsync(string meetingUrl, string displayName)
{
    var tenantId = this.Configuration.GetConfigValue("Bot:TenantId");

    // Parse meeting URL to extract join info
    var joinInfo = this.ParseMeetingUrl(meetingUrl);

    var chatInfo = new ChatInfo
    {
        ThreadId = joinInfo.ThreadId,
        MessageId = joinInfo.MessageId,
    };

    var meetingInfo = new OrganizerMeetingInfo
    {
        Organizer = new IdentitySet
        {
            User = new Identity
            {
                Id = joinInfo.OrganizerId,
            },
        },
    };

    var mediaSession = this.CreateLocalMediaSession();

    var call = await this.Client.Communications.Calls
        .Request()
        .AddAsync(new Call
        {
            Direction = CallDirection.Outgoing,
            Subject = displayName,
            ChatInfo = chatInfo,
            MeetingInfo = meetingInfo,
            MediaConfig = new ServiceHostedMediaConfig
            {
                PreFetchMedia = new List<MediaInfo>
                {
                    new MediaInfo
                    {
                        Uri = "https://bot.contoso.com/audio/prompt.wav",
                        ResourceId = Guid.NewGuid().ToString(),
                    },
                },
            },
            TenantId = tenantId,
        });

    // Create CallHandler and MediaBridge
    var callHandler = new CallHandler(call);
    this.CallHandlers[call.Id] = callHandler;

    // Initialize WebRTC connection
    var webRtcManager = this.CreateWebRtcManager(call.Id);
    await webRtcManager.CreateConnectionAsync(call.Id);

    return call;
}
```

### 4.4 End-to-End Flow

```
1. External System → POST /api/callcontrol/join
   Body: { "meetingUrl": "https://teams.microsoft.com/l/meetup-join/..." }

2. Bot → Graph API → Join Teams Meeting
   Creates Call object, establishes media session

3. Bot → Signaling Server → { "type": "join_call", "callId": "..." }
   Notifies that bot is ready for WebRTC

4. GStreamer Service → Signaling Server → Listens for join_call

5. Bot ↔ GStreamer → WebRTC Negotiation (SDP offer/answer, ICE)
   Establishes peer connection

6. Teams Audio → Bot → MediaBridge → WebRTC → GStreamer
   Audio flows in real-time

7. GStreamer → WebRTC → MediaBridge → Bot → Teams Audio
   Processed audio returns to call

8. Call Ends → Bot → Signaling Server → { "type": "leave_call" }
   Cleanup and disconnect
```

---

## Phase 5: GStreamer Integration (Week 5)

### 5.1 GStreamer Service Architecture

**Component Design:**
```
WebRTC Client (Python) ↔ GStreamer Pipeline
         ↓                        ↓
    Signaling Server         Audio Processing
                                  ↓
                            Output (File/Stream/AI)
```

**Technology Stack:**
- **Language**: Python (aiortc for WebRTC, GStreamer bindings)
- **WebRTC**: aiortc library
- **GStreamer**: PyGObject bindings (gi.repository.Gst)

### 5.2 GStreamer WebRTC Client (Python)

```python
# gstreamer_client.py
import asyncio
import json
import logging
from aiortc import RTCPeerConnection, RTCSessionDescription, RTCIceCandidate
from aiortc.contrib.media import MediaPlayer, MediaRecorder
import websockets
import gi
gi.require_version('Gst', '1.0')
from gi.repository import Gst

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class GStreamerWebRTCClient:
    def __init__(self, signaling_url, stun_server, turn_server, turn_user, turn_pass):
        self.signaling_url = signaling_url
        self.pc = RTCPeerConnection(
            configuration={
                "iceServers": [
                    {"urls": stun_server},
                    {
                        "urls": turn_server,
                        "username": turn_user,
                        "credential": turn_pass
                    }
                ]
            }
        )

        self.ws = None
        self.current_call_id = None

        # GStreamer pipeline
        Gst.init(None)
        self.pipeline = None

    async def connect(self):
        """Connect to signaling server"""
        self.ws = await websockets.connect(self.signaling_url)
        logger.info(f"Connected to signaling server: {self.signaling_url}")

        # Start listening for messages
        asyncio.create_task(self.receive_signaling_messages())

    async def receive_signaling_messages(self):
        """Listen for signaling messages"""
        async for message in self.ws:
            data = json.loads(message)
            msg_type = data.get("type")

            if msg_type == "join_call":
                await self.handle_join_call(data)
            elif msg_type == "offer":
                await self.handle_offer(data)
            elif msg_type == "ice_candidate":
                await self.handle_ice_candidate(data)

    async def handle_join_call(self, data):
        """Bot joined a call, prepare to receive offer"""
        self.current_call_id = data["callId"]
        logger.info(f"Bot joined call: {self.current_call_id}")

        # Setup GStreamer pipeline
        self.setup_gstreamer_pipeline()

    async def handle_offer(self, data):
        """Receive SDP offer from bot"""
        sdp = data["sdp"]
        call_id = data["callId"]

        logger.info(f"Received offer for call {call_id}")

        # Set remote description
        await self.pc.setRemoteDescription(
            RTCSessionDescription(sdp=sdp, type="offer")
        )

        # Add audio track
        self.setup_audio_track()

        # Create answer
        answer = await self.pc.createAnswer()
        await self.pc.setLocalDescription(answer)

        # Send answer
        await self.send_signaling_message({
            "type": "answer",
            "callId": call_id,
            "sdp": self.pc.localDescription.sdp
        })

        logger.info("Sent answer")

    async def handle_ice_candidate(self, data):
        """Receive ICE candidate from bot"""
        candidate_data = data["candidate"]
        candidate = RTCIceCandidate(
            candidate=candidate_data["candidate"],
            sdpMid=candidate_data["sdpMid"],
            sdpMLineIndex=candidate_data["sdpMLineIndex"]
        )
        await self.pc.addIceCandidate(candidate)

    def setup_audio_track(self):
        """Setup WebRTC audio track with GStreamer source"""

        @self.pc.on("track")
        async def on_track(track):
            logger.info(f"Receiving {track.kind} track")

            if track.kind == "audio":
                # Receive audio from Teams bot
                while True:
                    try:
                        frame = await track.recv()
                        # Push to GStreamer pipeline
                        self.push_audio_to_gstreamer(frame)
                    except Exception as e:
                        logger.error(f"Error receiving audio: {e}")
                        break

        # Add audio track to send back
        # (GStreamer appsrc → WebRTC)
        # Implementation depends on specific GStreamer pipeline

    def setup_gstreamer_pipeline(self):
        """Create GStreamer pipeline for audio processing"""

        # Example pipeline: Receive → Process → Send back
        pipeline_str = """
            appsrc name=src format=time
            ! audio/x-opus,rate=48000,channels=1
            ! opusdec
            ! audioconvert
            ! audioresample
            ! audio/x-raw,rate=16000,channels=1,format=S16LE
            ! volume volume=1.5
            ! audioresample
            ! audio/x-raw,rate=48000
            ! opusenc
            ! appsink name=sink emit-signals=true
        """

        self.pipeline = Gst.parse_launch(pipeline_str)
        self.appsrc = self.pipeline.get_by_name("src")
        self.appsink = self.pipeline.get_by_name("sink")

        # Connect appsink to WebRTC sender
        self.appsink.connect("new-sample", self.on_gstreamer_output)

        self.pipeline.set_state(Gst.State.PLAYING)
        logger.info("GStreamer pipeline started")

    def push_audio_to_gstreamer(self, frame):
        """Push WebRTC audio frame to GStreamer"""
        # Convert aiortc frame to GStreamer buffer
        buffer = Gst.Buffer.new_wrapped(frame.data)
        self.appsrc.emit("push-buffer", buffer)

    def on_gstreamer_output(self, sink):
        """GStreamer processed audio → send via WebRTC"""
        sample = sink.emit("pull-sample")
        buffer = sample.get_buffer()

        # Extract data and send via WebRTC
        success, map_info = buffer.map(Gst.MapFlags.READ)
        if success:
            audio_data = map_info.data
            # Send to WebRTC track
            # (Requires custom implementation with aiortc)
            buffer.unmap(map_info)

        return Gst.FlowReturn.OK

    async def send_signaling_message(self, message):
        """Send message via signaling"""
        await self.ws.send(json.dumps(message))

    async def run(self):
        """Main run loop"""
        await self.connect()

        # Keep running
        await asyncio.Future()

# Main entry point
async def main():
    client = GStreamerWebRTCClient(
        signaling_url="ws://signaling-server:8765",
        stun_server="stun:stun.l.google.com:19302",
        turn_server="turn:your-turn-server:3478",
        turn_user="botuser",
        turn_pass="strongpassword"
    )

    await client.run()

if __name__ == "__main__":
    asyncio.run(main())
```

### 5.3 Example GStreamer Pipelines

**Simple Echo (for testing):**
```bash
# Receive → Send back unchanged
gst-launch-1.0 \
    appsrc ! opusdec ! opusenc ! appsink
```

**Voice Enhancement:**
```bash
# Noise reduction + echo cancellation
gst-launch-1.0 \
    appsrc ! opusdec ! audioconvert ! \
    webrtcdsp ! \  # Echo cancellation, noise suppression
    audioresample ! opusenc ! appsink
```

**Speech-to-Text Integration:**
```python
# Pipeline with Cloud Speech API
pipeline_str = """
    appsrc ! opusdec ! audioconvert !
    audio/x-raw,rate=16000,channels=1 !
    appsink name=sink
"""

# On each buffer from appsink:
def on_buffer(sink):
    sample = sink.emit("pull-sample")
    audio_data = extract_buffer_data(sample)

    # Send to Google Cloud Speech API
    transcription = speech_client.recognize(audio_data)

    # Generate TTS response
    response_audio = tts_client.synthesize(response_text)

    # Push back to WebRTC
```

---

## Phase 6: Deployment & Infrastructure (Week 6)

### 6.1 Azure VM Configuration

**VM Specifications:**
- **Size**: Standard D4s v3 (4 vCPU, 16 GB RAM)
- **OS**: Windows Server 2019 (for Teams media bot)
- **Storage**: 128 GB Premium SSD
- **Networking**: Standard Load Balancer with public IP

**Required Ports:**
```
HTTP/HTTPS:
- 443 (Bot HTTPS endpoint)
- 8445 (Media control port)

Media:
- 49152-65535 (RTP/RTCP for Teams media)

COTURN:
- 3478 (STUN/TURN)
- 5349 (TURN over TLS)
- 49152-65535 (Relay ports)

Signaling:
- 8765 (WebSocket signaling)
```

**Firewall Rules (NSG):**
```bash
# Allow HTTPS
az network nsg rule create -g MediaBot-RG --nsg-name MediaBot-NSG \
  -n AllowHTTPS --priority 100 --destination-port-ranges 443 \
  --access Allow --protocol Tcp

# Allow media ports
az network nsg rule create -g MediaBot-RG --nsg-name MediaBot-NSG \
  -n AllowMediaPorts --priority 110 --destination-port-ranges 49152-65535 \
  --access Allow --protocol Udp

# Allow signaling WebSocket
az network nsg rule create -g MediaBot-RG --nsg-name MediaBot-NSG \
  -n AllowSignaling --priority 120 --destination-port-ranges 8765 \
  --access Allow --protocol Tcp
```

### 6.2 Application Registration (Azure AD)

**Required Permissions:**
- `Calls.AccessMedia.All` (Application)
- `Calls.Initiate.All` (Application)
- `Calls.JoinGroupCall.All` (Application)

**Configuration:**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-id>",
    "ClientId": "<app-id>",
    "ClientSecret": "<secret>"
  },
  "Bot": {
    "BotBaseUrl": "https://mediabot.contoso.com",
    "PlaceCallEndpointUrl": "https://graph.microsoft.com/v1.0",
    "MediaPlatformInstanceSettings": {
      "ServiceDnsName": "mediabot.contoso.com",
      "InstancePublicIPAddress": "<public-ip>",
      "InstanceInternalIPAddress": "<private-ip>"
    }
  },
  "WebRTC": {
    "SignalingServerUrl": "ws://localhost:8765",
    "TurnServerUrl": "turn:mediabot.contoso.com:3478",
    "TurnUsername": "botuser",
    "TurnPassword": "<secure-password>"
  }
}
```

### 6.3 SSL/TLS Certificates

**Required Certificates:**
1. **Bot HTTPS** (mediabot.contoso.com)
2. **TURN TLS** (turn.mediabot.contoso.com)

**Certificate Setup:**
```bash
# Using Let's Encrypt (certbot)
certbot certonly --standalone -d mediabot.contoso.com
certbot certonly --standalone -d turn.mediabot.contoso.com

# Convert for TURN
openssl pkcs12 -export -out turn.pfx \
  -inkey /etc/letsencrypt/live/turn.mediabot.contoso.com/privkey.pem \
  -in /etc/letsencrypt/live/turn.mediabot.contoso.com/fullchain.pem
```

### 6.4 Deployment Architecture

```
┌─────────────────────────────────────────────────────┐
│                  Azure Cloud                        │
│                                                     │
│  ┌──────────────────────────────────────┐          │
│  │  Standard D4s v3 VM                  │          │
│  │  (Windows Server 2019)               │          │
│  │                                       │          │
│  │  ┌────────────────────────┐          │          │
│  │  │  Teams Media Bot       │          │          │
│  │  │  (C# .NET)             │          │          │
│  │  │  - BotMediaStream      │          │          │
│  │  │  - MediaBridge         │          │          │
│  │  │  - WebRTCManager       │          │          │
│  │  └────────┬───────────────┘          │          │
│  │           │                           │          │
│  │  ┌────────▼───────────────┐          │          │
│  │  │  COTURN Server         │          │          │
│  │  │  (STUN/TURN)           │          │          │
│  │  └────────────────────────┘          │          │
│  │                                       │          │
│  │  ┌────────────────────────┐          │          │
│  │  │  Signaling Server      │          │          │
│  │  │  (Python WebSocket)    │          │          │
│  │  └────────────────────────┘          │          │
│  └──────────────────────────────────────┘          │
│                    ↕                                │
│              WebRTC (RTP/ICE)                       │
│                    ↕                                │
└─────────────────────────────────────────────────────┘
                     ↕
┌─────────────────────────────────────────────────────┐
│             On-Premises / Remote Location           │
│                                                     │
│  ┌────────────────────────────────────┐            │
│  │  GStreamer Service (Python)        │            │
│  │  - aiortc WebRTC client            │            │
│  │  - GStreamer pipelines             │            │
│  │  - Audio processing                │            │
│  └────────────────────────────────────┘            │
│                                                     │
└─────────────────────────────────────────────────────┘
```

---

## Phase 7: Testing & Validation (Week 7)

### 7.1 Unit Testing

**Test Cases:**

1. **Audio Buffer Handling**
   ```csharp
   [Test]
   public void TestAudioBufferMarshaling()
   {
       // Mock Teams audio buffer
       var mockBuffer = CreateMockAudioBuffer(640); // 20ms @ 16kHz

       // Test marshaling
       var audioData = MarshalBuffer(mockBuffer);

       Assert.AreEqual(640, audioData.Length);
       Assert.IsTrue(mockBuffer.IsDisposed);
   }
   ```

2. **WebRTC Connection**
   ```csharp
   [Test]
   public async Task TestWebRTCConnection()
   {
       var webRtcManager = new WebRTCManager(logger, signalingClient, turnUrl, user, pass);
       var result = await webRtcManager.CreateConnectionAsync("test-call-id");

       Assert.IsTrue(result);
       Assert.AreEqual(RTCPeerConnectionState.connected, webRtcManager.ConnectionState);
   }
   ```

3. **Signaling Protocol**
   ```python
   async def test_signaling_offer_answer():
       client = SignalingClient("ws://localhost:8765")
       await client.connect()

       await client.send_offer("call-123", test_sdp)
       answer = await client.wait_for_answer(timeout=5)

       assert answer is not None
       assert answer["callId"] == "call-123"
   ```

### 7.2 Integration Testing

**End-to-End Test Flow:**

```python
# test_e2e.py
async def test_full_audio_flow():
    # 1. Start all services
    signaling_server = await start_signaling_server()
    gstreamer_client = await start_gstreamer_client()

    # 2. Trigger bot to join call
    response = requests.post("https://mediabot.contoso.com/api/callcontrol/join", json={
        "meetingUrl": TEST_MEETING_URL
    })
    assert response.status_code == 200
    call_id = response.json()["callId"]

    # 3. Wait for WebRTC connection
    await wait_for_webrtc_connection(call_id, timeout=30)

    # 4. Inject test audio into Teams call (via another participant)
    test_audio = generate_test_tone(frequency=440, duration=5)  # 5s @ 440Hz
    await inject_audio_to_teams_call(call_id, test_audio)

    # 5. Verify audio received by GStreamer
    received_audio = await gstreamer_client.get_received_audio(timeout=10)
    assert len(received_audio) > 0

    # Verify frequency content (should detect 440Hz tone)
    detected_freq = analyze_frequency(received_audio)
    assert 435 < detected_freq < 445  # Allow 5Hz tolerance

    # 6. Send audio from GStreamer back to Teams
    response_audio = generate_test_tone(frequency=880, duration=5)
    await gstreamer_client.send_audio(response_audio)

    # 7. Verify Teams receives audio
    teams_audio = await get_audio_from_teams_call(call_id, timeout=10)
    detected_freq = analyze_frequency(teams_audio)
    assert 875 < detected_freq < 885
```

### 7.3 Performance Testing

**Metrics to Monitor:**

1. **Latency**
   - Teams → Bot → GStreamer: < 100ms target
   - Round-trip (Teams → GStreamer → Teams): < 200ms

2. **Packet Loss**
   - Target: < 1% under normal conditions
   - Acceptable: < 5% under stress

3. **CPU Usage**
   - Bot VM: < 60% average
   - Spikes during codec conversion acceptable

4. **Memory**
   - Stable (no leaks)
   - < 2GB per call instance

**Load Testing:**
```bash
# Simulate multiple concurrent calls
for i in {1..10}; do
  curl -X POST https://mediabot.contoso.com/api/callcontrol/join \
    -d '{"meetingUrl": "https://teams.microsoft.com/..."}' &
done

# Monitor resources
watch -n 1 'ps aux | grep PolicyRecordingBot'
```

---

## Phase 8: Error Handling & Resilience (Week 8)

### 8.1 Error Scenarios

**Critical Failures:**

1. **WebRTC Connection Failure**
   ```csharp
   private async Task HandleWebRTCFailure(string callId, Exception ex)
   {
       this.logger.Error(ex, $"WebRTC connection failed for call {callId}");

       // Attempt reconnection (exponential backoff)
       for (int retry = 0; retry < 3; retry++)
       {
           await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retry)));

           if (await this.webRtcManager.CreateConnectionAsync(callId))
           {
               this.logger.Info($"WebRTC reconnected on attempt {retry + 1}");
               return;
           }
       }

       // Reconnection failed, leave Teams call
       await this.bot.LeaveCallAsync(callId);
       await this.SendAlertAsync($"Bot left call {callId} due to WebRTC failure");
   }
   ```

2. **Audio Buffer Queue Overflow**
   ```csharp
   if (!this.audioQueue.TryAdd(frame, TimeSpan.FromMilliseconds(10)))
   {
       this.logger.Warn("Audio queue full, dropping frame");
       this.droppedFramesCounter.Increment();

       // Alert if drop rate > 5%
       if (this.droppedFramesCounter.GetRate() > 0.05)
       {
           await this.SendAlertAsync("High audio frame drop rate detected");
       }
   }
   ```

3. **Signaling Disconnection**
   ```csharp
   private async Task MonitorSignalingConnection()
   {
       while (!this.cancellationToken.IsCancellationRequested)
       {
           if (this.signalingClient.State != WebSocketState.Open)
           {
               this.logger.Warn("Signaling connection lost, reconnecting...");
               await this.signalingClient.ReconnectAsync();
           }

           await Task.Delay(TimeSpan.FromSeconds(5));
       }
   }
   ```

### 8.2 Monitoring & Alerting

**Application Insights Integration:**
```csharp
// Track custom metrics
this.telemetryClient.TrackMetric("AudioLatency", latencyMs);
this.telemetryClient.TrackMetric("WebRTCPacketLoss", packetLossPercent);
this.telemetryClient.TrackMetric("ActiveCalls", this.callHandlers.Count);

// Track custom events
this.telemetryClient.TrackEvent("CallJoined", new Dictionary<string, string>
{
    { "callId", call.Id },
    { "participants", call.Participants.Count.ToString() }
});
```

**Health Check Endpoint:**
```csharp
[HttpGet("health")]
public IActionResult HealthCheck()
{
    var health = new
    {
        status = "healthy",
        activeCalls = this.bot.GetActiveCallCount(),
        signalingConnected = this.signalingClient.IsConnected,
        webRtcConnections = this.webRtcManager.GetActiveConnectionCount(),
        uptimeSeconds = (DateTime.UtcNow - this.startTime).TotalSeconds
    };

    return Ok(health);
}
```

---

## Phase 9: Security Considerations

### 9.1 Authentication & Authorization

**Bot Authentication:**
- Client credentials flow (OAuth 2.0)
- Rotate secrets regularly (Azure Key Vault)

**Signaling Authentication:**
```python
# Add JWT authentication to signaling server
async def handle_client(websocket, path):
    # Verify JWT token
    token = await websocket.recv()
    try:
        payload = jwt.decode(token, SECRET_KEY, algorithms=["HS256"])
        client_id = payload["client_id"]
    except jwt.InvalidTokenError:
        await websocket.close(1008, "Invalid token")
        return

    # Proceed with authenticated session
    await handle_authenticated_client(websocket, client_id)
```

### 9.2 Media Encryption

**DTLS-SRTP (WebRTC):**
- Enabled by default in SIPSorcery
- Verify certificate fingerprints in SDP

**Teams Media:**
- Encrypted by Teams platform
- Bot receives decrypted media

### 9.3 Network Security

**Firewall Rules:**
- Whitelist only necessary IPs
- Restrict admin ports (SSH, RDP) to specific IPs

**DDoS Protection:**
- Azure DDoS Protection Standard
- Rate limiting on HTTP endpoints

---

## Phase 10: Production Readiness Checklist

### 10.1 Deployment Checklist

- [ ] Azure VM provisioned and configured
- [ ] SSL certificates installed and valid
- [ ] Bot registered in Azure AD with correct permissions
- [ ] COTURN server deployed and tested
- [ ] Signaling server running with monitoring
- [ ] Application Insights configured
- [ ] Health check endpoints functional
- [ ] Logging configured (centralized)
- [ ] Backup and disaster recovery plan
- [ ] Documentation complete

### 10.2 Testing Checklist

- [ ] Unit tests passing (>80% coverage)
- [ ] Integration tests passing
- [ ] End-to-end audio flow tested
- [ ] Latency measurements within acceptable range
- [ ] Load testing completed (10+ concurrent calls)
- [ ] Failover scenarios tested
- [ ] Security scan completed (no critical vulnerabilities)

### 10.3 Operational Checklist

- [ ] Monitoring dashboards created
- [ ] Alerting rules configured
- [ ] On-call rotation established
- [ ] Runbook for common issues
- [ ] Scaling strategy defined
- [ ] Cost monitoring enabled

---

## Appendix A: Configuration Examples

### appsettings.json (Complete)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-app-id",
    "ClientSecret": "your-client-secret"
  },
  "Bot": {
    "BotBaseUrl": "https://mediabot.contoso.com",
    "PlaceCallEndpointUrl": "https://graph.microsoft.com/v1.0",
    "MediaPlatformInstanceSettings": {
      "ServiceDnsName": "mediabot.contoso.com",
      "InstancePublicIPAddress": "20.x.x.x",
      "InstanceInternalIPAddress": "10.0.0.4",
      "MediaPlatformPort": 8445,
      "MediaPlatformRtpPortStart": 49152,
      "MediaPlatformRtpPortCount": 16384
    }
  },
  "WebRTC": {
    "SignalingServerUrl": "wss://signaling.contoso.com:8765",
    "StunServerUrl": "stun:stun.l.google.com:19302",
    "TurnServerUrl": "turn:mediabot.contoso.com:3478",
    "TurnUsername": "botuser",
    "TurnPassword": "secure-password-here",
    "TurnTlsPort": 5349
  },
  "ApplicationInsights": {
    "InstrumentationKey": "your-instrumentation-key"
  }
}
```

---

## Appendix B: Key Files to Create/Modify

### New Files
1. `FrontEnd/Bot/WebRTC/MediaBridge.cs` (300 lines)
2. `FrontEnd/Bot/WebRTC/WebRTCManager.cs` (400 lines)
3. `FrontEnd/Bot/WebRTC/AudioConverter.cs` (150 lines)
4. `FrontEnd/Bot/WebRTC/PeerConnectionFactory.cs` (100 lines)
5. `FrontEnd/Signaling/SignalingClient.cs` (250 lines)
6. `FrontEnd/Signaling/SignalingProtocol.cs` (50 lines)
7. `FrontEnd/Http/Controllers/CallControlController.cs` (150 lines)

### Files to Modify
1. `FrontEnd/Bot/BotMediaStream.cs` - Add WebRTC forwarding (~50 lines added)
2. `FrontEnd/Bot/CallHandler.cs` - Initialize WebRTC components (~30 lines added)
3. `FrontEnd/Bot/Bot.cs` - Add JoinCall method (~100 lines added)
4. `FrontEnd/Service.cs` - Wire up new components (~20 lines added)
5. `WorkerRole/AzureConfiguration.cs` - Add WebRTC config (~10 lines added)

**Total Estimated Code:** ~1,500-2,000 lines

---

## Appendix C: Dependencies (NuGet Packages)

```xml
<PackageReference Include="SIPSorcery" Version="6.1.3" />
<PackageReference Include="SIPSorceryMedia.Abstractions" Version="6.0.4" />
<PackageReference Include="SIPSorceryMedia.Windows" Version="6.0.4" />
<PackageReference Include="Concentus.Opus" Version="2.0.0" />
<PackageReference Include="System.Net.WebSockets.Client" Version="4.3.2" />
```

---

## Appendix D: Estimated Timeline

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| 1. Architecture & Foundation | 1-2 weeks | - |
| 2. Bot Extension | 1-2 weeks | Phase 1 |
| 3. Signaling Protocol | 1 week | Phase 1 |
| 4. Call Control | 1 week | Phase 2, 3 |
| 5. GStreamer Integration | 1-2 weeks | Phase 2, 3 |
| 6. Deployment | 1 week | Phase 1-5 |
| 7. Testing | 1 week | Phase 1-6 |
| 8. Error Handling | 1 week | Phase 1-7 |
| 9. Security | Ongoing | All phases |
| 10. Production | 1 week | All phases |

**Total: 8-12 weeks** (depending on team size and complexity)

---

## Appendix E: Troubleshooting Guide

### Common Issues

**1. No Audio Received from Teams**
- Check: Is bot in "Established" call state?
- Check: Is audio socket subscribed to participants?
- Check: Are buffers being disposed properly?
- Check: Is MediaBridge initialized?

**2. WebRTC Connection Fails**
- Check: Is TURN server reachable?
- Check: Are firewall ports open (3478, 49152-65535)?
- Check: Is signaling server connected?
- Check: Check SDP offer/answer compatibility

**3. High Latency**
- Check: Network latency between bot and GStreamer
- Check: Audio queue sizes (may be buffering too much)
- Check: CPU usage (codec conversion bottleneck)
- Check: GStreamer pipeline complexity

**4. Audio Distortion**
- Check: Sample rate conversions (16kHz ↔ 48kHz)
- Check: Buffer sizes (should be multiples of 20ms)
- Check: Opus encoding bitrate
- Check: Network packet loss

---

## Summary

This implementation plan provides a comprehensive roadmap for extending the PolicyRecordingBot to bridge Teams media with GStreamer via WebRTC. The key technical challenges are:

1. **Real-time media handling** - Requires careful buffer management and thread safety
2. **WebRTC integration** - SIPSorcery provides the necessary C# implementation
3. **Signaling coordination** - Custom WebSocket protocol keeps it simple and flexible
4. **Audio format conversion** - PCM ↔ Opus conversion with minimal latency
5. **Deployment complexity** - Multiple services (bot, COTURN, signaling, GStreamer)

The phased approach allows for incremental development and testing. Start with Phase 1-3 to establish the core architecture, then iterate on Phases 4-5 for end-to-end functionality.

**Next Steps:**
1. Review this plan with your team
2. Set up development environment
3. Start with Phase 1: Deploy COTURN and signaling server
4. Begin coding Phase 2: Extend BotMediaStream with MediaBridge

Good luck with your implementation!
