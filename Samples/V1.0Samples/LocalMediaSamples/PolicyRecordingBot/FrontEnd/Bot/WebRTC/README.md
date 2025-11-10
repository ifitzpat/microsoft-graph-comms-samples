# WebRTC Components

This directory contains the WebRTC integration components for bridging Teams media with external GStreamer services.

## Components

### MediaBridge

**File:** `MediaBridge.cs`

The core audio routing component that bridges Teams media streams with WebRTC peer connections.

**Responsibilities:**
- Receives PCM audio from Teams (via BotMediaStream)
- Queues audio for processing
- Converts PCM 16kHz → Opus
- Sends Opus audio via WebRTC (RTP)
- Receives Opus audio from WebRTC
- Converts Opus → PCM 16kHz
- Forwards PCM audio back to Teams

**Key Features:**
- **Bounded queues** - Prevents memory buildup (default 50 frames = ~1 second)
- **Thread-safe** - Uses BlockingCollection for concurrent access
- **Async processing** - Background tasks for audio conversion
- **Drop detection** - Tracks and logs dropped frames
- **Graceful disposal** - Proper cleanup of resources

**Usage:**
```csharp
// Initialize
var webRtcManager = new WebRTCManager(...);
var mediaBridge = new MediaBridge(logger, webRtcManager);

// Attach to BotMediaStream
mediaBridge.AttachBotMediaStream(botMediaStream);

// Send audio from Teams
mediaBridge.SendAudioToWebRTC(pcmData, timestamp);

// Monitor performance
var (teamsToWebRtc, webRtcToTeams) = mediaBridge.GetQueueSizes();
int droppedFrames = mediaBridge.GetDroppedFrameCount();

// Cleanup
mediaBridge.Dispose();
```

---

### AudioConverter

**File:** `AudioConverter.cs`

Handles audio format conversion between PCM and Opus using the Concentus library.

**Audio Formats:**
- **PCM**: 16-bit, 16kHz, mono, 640 bytes/frame (20ms)
- **Opus**: 24kbps bitrate, VOIP application, SILK mode for voice

**Current Status:**
✅ **COMPLETE** - Full Opus codec implementation using Concentus v2.1.1

**Implementation Details:**
- Uses Concentus library (pure C# Opus implementation)
- Encoder configured for optimal voice quality:
  - Bitrate: 24 kbps
  - Complexity: 10 (maximum quality)
  - Signal type: VOICE
  - Mode: SILK-only (optimized for voice)
- Decoder supports Forward Error Correction (FEC)
- Error handling for invalid Opus frames

**Usage:**
```csharp
var converter = new AudioConverter(logger);

// Teams → WebRTC: Convert PCM to Opus
byte[] pcmData = new byte[640]; // 20ms @ 16kHz mono
byte[] opusData = converter.ConvertPcmToOpus(pcmData);

// WebRTC → Teams: Convert Opus to PCM
byte[] receivedOpus = new byte[120]; // Typical Opus frame
byte[] decodedPcm = converter.ConvertOpusToPcm(receivedOpus);

// Cleanup
converter.Dispose();
```

**Dependencies:**
- Concentus v2.1.1 (pure C# Opus codec)
- No native dependencies required

---

### Interfaces

#### IWebRTCManager

**File:** `IWebRTCManager.cs`

Interface for WebRTC peer connection management (to be implemented).

**Methods:**
- `CreateConnectionAsync(callId)` - Establish WebRTC connection
- `SendAudioAsync(opusData)` - Send RTP audio packets
- `GetConnectionState()` - Get current state
- `GetActiveConnectionCount()` - Connection count

**Events:**
- `OnAudioReceived` - Fired when RTP audio is received

**Implementation:** See `WebRTCManager.cs` (to be created)

#### IAudioConverter

**File:** `IAudioConverter.cs`

Interface for audio format conversion.

**Methods:**
- `ConvertPcmToOpus(pcmData)` - PCM → Opus
- `ConvertOpusToPcm(opusData)` - Opus → PCM

**Implemented by:** `AudioConverter.cs`

---

### Data Structures

#### AudioFrame

**File:** `AudioFrame.cs`

Represents an audio frame with metadata.

**Properties:**
- `Data` (byte[]) - Audio data
- `Timestamp` (long) - Frame timestamp
- `Format` (AudioFormat) - PCM16kHz or Opus

**Usage:**
```csharp
var frame = new AudioFrame
{
    Data = pcmData,
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    Format = AudioFormat.PCM16kHz
};
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Teams Call                           │
│                       ↓↑                                │
│                 BotMediaStream                          │
│           (receives/sends PCM audio)                    │
└─────────────────────────────────────────────────────────┘
                       ↓↑
┌─────────────────────────────────────────────────────────┐
│                  MediaBridge                            │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Teams → WebRTC Queue (BlockingCollection)       │ │
│  │  - Bounded to 50 frames (~1 second)              │ │
│  │  - Background processing task                    │ │
│  └───────────────────────────────────────────────────┘ │
│                       ↓                                 │
│  ┌───────────────────────────────────────────────────┐ │
│  │  AudioConverter                                   │ │
│  │  - PCM 16kHz → Opus                               │ │
│  └───────────────────────────────────────────────────┘ │
│                       ↓                                 │
│  ┌───────────────────────────────────────────────────┐ │
│  │  WebRTC Manager (IWebRTCManager)                  │ │
│  │  - Sends RTP packets                              │ │
│  └───────────────────────────────────────────────────┘ │
│                       ↓                                 │
│  ┌───────────────────────────────────────────────────┐ │
│  │  WebRTC → Teams Queue (BlockingCollection)       │ │
│  │  - Receives Opus from RTP                         │ │
│  │  - Converts Opus → PCM                            │ │
│  │  - Forwards to BotMediaStream                     │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                       ↓↑
┌─────────────────────────────────────────────────────────┐
│              WebRTC Peer Connection                     │
│              (to GStreamer Service)                     │
└─────────────────────────────────────────────────────────┘
```

---

## Tests

**Location:** `PolicyRecordingBot.Tests/WebRTC/MediaBridgeTests.cs`

**Enabled Tests (5):**
- ✅ `Constructor_ValidDependencies_InitializesSuccessfully`
- ✅ `SendAudioToWebRTC_ValidPcmData_EnqueuesFrame`
- ✅ `SendAudioToWebRTC_NullData_ThrowsArgumentNullException`
- ✅ `SendAudioToWebRTC_QueueFull_DropsFrameAndLogsWarning`
- ✅ `Dispose_WithActiveProcessing_StopsAndCleansUp`

**Pending Tests (2):**
- ⏳ `OnWebRtcAudioReceived_ValidOpusData_ForwardsToTeams` (requires BotMediaStream.SendAudioToTeams)
- ⏳ `EndToEnd_AudioFlow_ConvertsAndTransmits` (integration test)

**Run tests:**
```bash
dotnet test --filter "FullyQualifiedName~MediaBridgeTests"
```

---

## Next Steps

1. ✅ **Opus codec support** - COMPLETED
   - Concentus v2.1.1 integrated
   - Full encoding/decoding implemented

2. ✅ **WebRTCManager** - COMPLETED
   - Implemented using SIPSorcery v6.0.12
   - Handles ICE, DTLS, SRTP, peer connections
   - See `WEBRTC_IMPLEMENTATION.md` for details

3. ✅ **SignalingClient** - COMPLETED
   - WebSocket connection to Python signaling server
   - SDP offer/answer exchange
   - ICE candidate forwarding
   - See `../Signaling/SIGNALING_CLIENT_IMPLEMENTATION.md`

4. **Modify BotMediaStream:** (NEXT PHASE)
   - Add `SendAudioToTeams(byte[] pcmData)` method
   - Integrate MediaBridge initialization in CallHandler
   - Forward received audio from Teams to MediaBridge
   - Wire up WebRTC→Teams audio path

5. **Integration Testing:**
   - Start Python signaling server
   - Test end-to-end WebRTC negotiation
   - Verify bidirectional audio flow
   - Remove `[Ignore]` from integration tests

---

## Performance Considerations

### Queue Sizing

Default queue size: **50 frames** = ~1 second buffer

**Increase queue size** if you see frequent drops:
```csharp
var bridge = new MediaBridge(logger, webRtcManager, null, maxQueueSize: 100);
```

**Decrease queue size** for lower latency (at risk of more drops):
```csharp
var bridge = new MediaBridge(logger, webRtcManager, null, maxQueueSize: 25); // ~500ms
```

### Monitoring

Monitor these metrics for health:
- **Dropped frames:** `mediaBridge.GetDroppedFrameCount()`
- **Queue depths:** `mediaBridge.GetQueueSizes()`
- **Log warnings:** Watch for "queue full" messages

Alert if:
- Dropped frames > 1% of total
- Queue depth consistently > 80% of max
- Sustained high latency

---

## References

- [SIPSorcery WebRTC](https://github.com/sipsorcery-org/sipsorcery)
- [Concentus Opus Codec](https://github.com/lostromb/concentus)
- [Teams Graph Communications SDK](https://github.com/microsoftgraph/microsoft-graph-comms-samples)
- [WebRTC Specification](https://www.w3.org/TR/webrtc/)
