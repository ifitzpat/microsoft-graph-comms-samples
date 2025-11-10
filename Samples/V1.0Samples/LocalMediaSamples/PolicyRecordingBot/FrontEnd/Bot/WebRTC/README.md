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

Handles audio format conversion between PCM and Opus.

**Audio Formats:**
- **PCM**: 16-bit, 16kHz, mono, 640 bytes/frame (20ms)
- **Opus**: Variable bitrate, optimized for VoIP

**Current Status:**
⚠️ **Placeholder Implementation** - Currently passes audio through without conversion.

**TODO:** Add Concentus.Opus NuGet package for actual encoding/decoding:
```xml
<PackageReference Include="Concentus.Opus" Version="2.0.0" />
```

**Full Implementation:**
```csharp
using Concentus.Structs;

private OpusEncoder encoder;
private OpusDecoder decoder;

public AudioConverter(IGraphLogger logger)
{
    this.encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.Voip);
    this.decoder = new OpusDecoder(SampleRate, Channels);
}

public byte[] ConvertPcmToOpus(byte[] pcmData)
{
    short[] pcmSamples = new short[pcmData.Length / 2];
    Buffer.BlockCopy(pcmData, 0, pcmSamples, 0, pcmData.Length);

    byte[] opusData = new byte[4000];
    int encodedLength = this.encoder.Encode(
        pcmSamples, 0, FrameSizeSamples,
        opusData, 0, opusData.Length);

    byte[] result = new byte[encodedLength];
    Buffer.BlockCopy(opusData, 0, result, 0, encodedLength);
    return result;
}
```

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

1. **Add Opus codec support:**
   ```bash
   dotnet add package Concentus.Opus --version 2.0.0
   ```
   Then implement actual encoding/decoding in AudioConverter.cs

2. **Implement WebRTCManager:**
   - Create `WebRTCManager.cs` implementing `IWebRTCManager`
   - Use SIPSorcery library for WebRTC stack
   - Handle ICE, DTLS, SRTP

3. **Implement SignalingClient:**
   - WebSocket connection to signaling server
   - SDP offer/answer exchange
   - ICE candidate forwarding

4. **Modify BotMediaStream:**
   - Add `SendAudioToTeams(byte[] pcmData)` method
   - Integrate MediaBridge initialization
   - Forward received audio to MediaBridge

5. **Enable remaining tests:**
   - Remove `[Ignore]` from WebRTC→Teams audio test
   - Run integration test end-to-end

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
