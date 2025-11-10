# Signaling Client Implementation

## Overview

SignalingClient is a WebSocket-based client for WebRTC signaling between the Teams bot and GStreamer services. It connects to the Python signaling server and handles SDP offer/answer exchange and ICE candidate forwarding.

## Implementation Status

✅ **COMPLETE** - All core functionality implemented (450+ lines)

### Features Implemented

1. **WebSocket Connection Management**
   - ClientWebSocket for WebSocket connectivity
   - Connection state tracking (IsConnected property)
   - Graceful connection/disconnection
   - Thread-safe send operations with SemaphoreSlim

2. **Message Serialization**
   - **join_call**: Includes callId, botId, timestamp
   - **offer**: Includes callId and SDP string
   - **ice_candidate**: Includes callId and candidate JSON
   - JSON serialization via Newtonsoft.Json

3. **Message Deserialization**
   - Parses incoming JSON messages
   - Dispatches to appropriate handlers
   - Robust error handling for malformed JSON

4. **Event-Driven Architecture**
   - `OnAnswerReceived` event - Fires when SDP answer arrives
   - `OnIceCandidateReceived` event - Fires when ICE candidate arrives
   - Async event handlers (Func<string, string, Task>)

5. **Automatic Reconnection**
   - Detects connection loss
   - Exponential backoff (2s, 4s, 8s, 16s, 32s)
   - Maximum 5 reconnection attempts
   - Prevents reconnection on intentional disconnect

6. **Error Handling**
   - Invalid JSON doesn't crash client
   - WebSocket errors trigger reconnection
   - Missing message fields logged but ignored
   - Continues processing after errors

7. **Resource Management**
   - Implements IDisposable pattern
   - Cancels receive loop on dispose
   - Closes WebSocket gracefully
   - Releases SemaphoreSlim

## Dependencies

### .NET Framework Classes Used

- `System.Net.WebSockets.ClientWebSocket` - WebSocket client
- `System.Threading.SemaphoreSlim` - Thread-safe send lock
- `System.Threading.CancellationTokenSource` - Cancellation support
- `Newtonsoft.Json` - JSON serialization (already in project)

**Status**: ✅ All dependencies available in .NET Framework 4.7.2

## Architecture

```
Teams Bot → WebRTCManager → SignalingClient → WebSocket → Python Signaling Server
                ↓                  ↓                              ↓
           SDP/ICE         JSON Messages                    WebSocket
```

### Message Flow

**Outbound (Bot → GStreamer):**
1. WebRTCManager generates SDP offer
2. Calls `SignalingClient.SendOfferAsync(callId, sdp)`
3. SignalingClient serializes to JSON: `{ "type": "offer", "callId": "...", "sdp": "..." }`
4. Sends via WebSocket to Python server
5. Server forwards to GStreamer client

**Inbound (GStreamer → Bot):**
1. GStreamer sends SDP answer via Python server
2. SignalingClient receives WebSocket message
3. Parses JSON and extracts `{ "type": "answer", "callId": "...", "sdp": "..." }`
4. Fires `OnAnswerReceived` event
5. WebRTCManager handles answer and sets remote description

## Integration with WebRTCManager

The WebRTCManager needs to integrate SignalingClient. Here's how to wire them up:

### Step 1: Add SignalingClient Dependency

In `WebRTCManager.cs` constructor:

```csharp
private readonly ISignalingClient signalingClient;

public WebRTCManager(
    IGraphLogger logger,
    ISignalingClient signalingClient,  // Add this
    string turnServerUrl,
    string turnUsername,
    string turnPassword)
{
    this.logger = logger;
    this.signalingClient = signalingClient;
    // ... rest of initialization

    // Wire up events
    this.signalingClient.OnAnswerReceived += this.OnAnswerReceivedAsync;
    this.signalingClient.OnIceCandidateReceived += this.OnIceCandidateReceivedAsync;
}
```

### Step 2: Uncomment TODO Markers

In `WebRTCManager.cs`, find and uncomment:

**Line ~156** (Send SDP offer):
```csharp
// TODO: Send offer via SignalingClient
await this.signalingClient.SendOfferAsync(callId, offer.sdp);
```

**Line ~332** (Send ICE candidate):
```csharp
// TODO: Send candidate via SignalingClient
await this.signalingClient.SendIceCandidateAsync(
    callId,
    JsonConvert.SerializeObject(candidate));
```

### Step 3: Update Tests

In `WebRTCManagerTests.cs`:

```csharp
private Mock<ISignalingClient> mockSignalingClient;

[TestInitialize]
public void Setup()
{
    this.logger = new TestLogger("WebRTCManagerTests");
    this.mockSignalingClient = new Mock<ISignalingClient>();
}

[TestMethod]
public async Task CreateConnectionAsync_ValidCallId_CreatesAndSendsOffer()
{
    // Arrange
    var manager = new WebRTCManager(
        this.logger,
        this.mockSignalingClient.Object,  // Pass mock
        "turn:test.com:3478",
        "user",
        "pass");

    // ... rest of test

    // Assert
    mockSignalingClient.Verify(
        s => s.SendOfferAsync("call-123", It.IsAny<string>()),
        Times.Once,
        "Should send SDP offer via signaling");
}
```

## API Reference

### Constructor

```csharp
public SignalingClient(IGraphLogger logger, string signalingUrl)
```

**Parameters:**
- `logger` - Graph logger for telemetry
- `signalingUrl` - WebSocket URL (e.g., `ws://localhost:8765`)

### Properties

**IsConnected**
```csharp
public bool IsConnected { get; }
```
Returns true if WebSocket is in Open state.

### Methods

**ConnectAsync**
```csharp
Task ConnectAsync()
```
Connects to signaling server and starts receive loop.

**DisconnectAsync**
```csharp
Task DisconnectAsync()
```
Closes WebSocket gracefully and stops receive loop.

**SendJoinCallAsync**
```csharp
Task SendJoinCallAsync(string callId)
```
Sends join_call message with callId, botId (machine name), and timestamp.

**SendOfferAsync**
```csharp
Task SendOfferAsync(string callId, string sdp)
```
Sends SDP offer for WebRTC negotiation.

**SendIceCandidateAsync**
```csharp
Task SendIceCandidateAsync(string callId, string candidateJson)
```
Sends ICE candidate as JSON string. Candidate should be in format:
```json
{
  "candidate": "candidate:...",
  "sdpMid": "0",
  "sdpMLineIndex": 0
}
```

### Events

**OnAnswerReceived**
```csharp
event Func<string, string, Task> OnAnswerReceived
```
Fired when SDP answer is received.
Parameters: `(callId, sdp)`

**OnIceCandidateReceived**
```csharp
event Func<string, string, Task> OnIceCandidateReceived
```
Fired when ICE candidate is received.
Parameters: `(callId, candidateJson)`

## Message Protocol

### Outbound Messages

**join_call**
```json
{
  "type": "join_call",
  "callId": "call-123",
  "botId": "MACHINE-NAME",
  "timestamp": 1699564800
}
```

**offer**
```json
{
  "type": "offer",
  "callId": "call-123",
  "sdp": "v=0\r\no=- 123456 2 IN IP4..."
}
```

**ice_candidate**
```json
{
  "type": "ice_candidate",
  "callId": "call-123",
  "candidate": {
    "candidate": "candidate:1 1 UDP...",
    "sdpMid": "0",
    "sdpMLineIndex": 0
  }
}
```

### Inbound Messages

**answer**
```json
{
  "type": "answer",
  "callId": "call-123",
  "sdp": "v=0\r\no=- 654321 2 IN IP4..."
}
```

**ice_candidate**
```json
{
  "type": "ice_candidate",
  "callId": "call-123",
  "candidate": {
    "candidate": "candidate:2 1 UDP...",
    "sdpMid": "0",
    "sdpMLineIndex": 0
  }
}
```

## Testing

### Test File
`PolicyRecordingBot.Tests/Signaling/SignalingClientTests.cs` (8 tests)

### Running Tests

1. **Start Python signaling server** (for integration tests):
   ```bash
   cd signaling
   python3 -m src.signaling_server --port 8765
   ```

2. **Restore NuGet packages**:
   ```bash
   nuget restore PolicyRecordingBot.sln
   ```

3. **Build solution**:
   ```bash
   msbuild PolicyRecordingBot.sln /p:Configuration=Debug /p:Platform=x64
   ```

4. **Enable tests** by removing `[Ignore]` attributes from:
   - `ConnectAsync_ValidUrl_EstablishesConnection`
   - `SendJoinCallAsync_ValidCallId_SendsJsonMessage`
   - `SendOfferAsync_ValidSdp_SendsFormattedMessage`
   - `OnMessageReceived_AnswerMessage_TriggersEvent`
   - `SendIceCandidateAsync_ValidCandidate_SendsJsonMessage`
   - `OnConnectionClosed_Unexpectedly_AttemptsReconnect`
   - `OnMessageReceived_InvalidJson_LogsErrorAndContinues`
   - `Dispose_ActiveConnection_ClosesGracefully`

5. **Run tests**:
   ```bash
   vstest.console.exe PolicyRecordingBot.Tests\bin\x64\Debug\PolicyRecordingBot.Tests.dll
   ```

### Unit vs Integration Tests

- **Unit Tests**: Mock WebSocket for message serialization tests
- **Integration Tests**: Connect to real Python signaling server

For CI/CD, unit tests can run without server. Integration tests require server.

## Usage Example

```csharp
using Sample.PolicyRecordingBot.FrontEnd.Signaling;

// Create client
var signalingClient = new SignalingClient(
    logger,
    "ws://signaling-server.contoso.com:8765");

// Wire up events
signalingClient.OnAnswerReceived += async (callId, sdp) =>
{
    logger.Info($"Received answer for {callId}");
    await webRtcManager.OnAnswerReceivedAsync(callId, sdp);
};

signalingClient.OnIceCandidateReceived += async (callId, candidateJson) =>
{
    logger.Info($"Received ICE candidate for {callId}");
    var candidate = JsonConvert.DeserializeObject<RTCIceCandidateInit>(candidateJson);
    await webRtcManager.OnIceCandidateReceivedAsync(callId, candidate);
};

// Connect
await signalingClient.ConnectAsync();

// Join call
await signalingClient.SendJoinCallAsync("call-123");

// Send offer (after WebRTC creates it)
await signalingClient.SendOfferAsync("call-123", sdpOffer);

// Send ICE candidate
await signalingClient.SendIceCandidateAsync("call-123", candidateJson);

// Disconnect when done
await signalingClient.DisconnectAsync();
signalingClient.Dispose();
```

## Configuration

### Signaling Server URL

The signaling server URL should be configurable via app settings:

```xml
<!-- In Web.config or App.config -->
<appSettings>
  <add key="SignalingServerUrl" value="ws://signaling.contoso.com:8765" />
</appSettings>
```

```csharp
string signalingUrl = ConfigurationManager.AppSettings["SignalingServerUrl"];
var signalingClient = new SignalingClient(logger, signalingUrl);
```

### For Local Development

```csharp
string signalingUrl = "ws://localhost:8765";
```

### For Production

```csharp
string signalingUrl = "wss://signaling.yourdomain.com:443";  // Use WSS (secure)
```

**Note**: For production, use `wss://` (WebSocket Secure) instead of `ws://`.

## Troubleshooting

### "Not connected to signaling server"
- Call `ConnectAsync()` before sending messages
- Check `IsConnected` property
- Verify signaling server is running and reachable

### "Failed to connect to signaling server"
- Check signaling server URL is correct
- Ensure Python signaling server is running:
  ```bash
  python3 -m src.signaling_server --port 8765
  ```
- Check firewall allows WebSocket connections
- Verify network connectivity

### Connection keeps disconnecting
- Check Python server logs for errors
- Verify server is handling messages correctly
- Check for network stability issues
- Review reconnection logs for patterns

### "Failed to parse message JSON"
- Check Python server is sending valid JSON
- Verify message format matches protocol
- Review Python server logs for serialization errors

### Events not firing
- Ensure event handlers are wired up before connecting
- Check async event handlers don't throw exceptions
- Verify message type matches expected ("answer", "ice_candidate")

## Performance Considerations

- **Thread Safety**: Send operations are serialized with SemaphoreSlim
- **Reconnection**: Exponential backoff prevents connection storms
- **Memory**: Receive buffer is 8KB (adjustable if needed)
- **Async**: All I/O operations are async to prevent blocking

## Security Considerations

For production deployments:

1. **Use WSS (WebSocket Secure)**
   ```csharp
   string signalingUrl = "wss://signaling.yourdomain.com:443";
   ```

2. **Certificate Validation**
   - Ensure SSL/TLS certificates are valid
   - Use proper certificate chain validation

3. **Authentication**
   - Consider adding authentication token to WebSocket headers
   - Implement server-side authentication in Python server

4. **Message Validation**
   - Client validates incoming message structure
   - Server should validate all incoming messages
   - Sanitize callId and other user inputs

## Next Steps

1. **Wire SignalingClient into WebRTCManager**
   - Add ISignalingClient parameter to constructor
   - Uncomment TODO markers in WebRTCManager
   - Update WebRTCManager tests with SignalingClient mock

2. **Integration Testing**
   - Start Python signaling server
   - Run end-to-end WebRTC negotiation tests
   - Verify SDP and ICE exchange works correctly

3. **Add Opus Codec Support**
   - Install Concentus.Opus NuGet package
   - Implement AudioConverter encoding/decoding
   - Test audio quality

4. **Deploy and Test**
   - Deploy Python signaling server to cloud
   - Deploy Teams bot
   - Test with real GStreamer service
   - Verify audio flows correctly

## References

- [WebSocket Protocol (RFC 6455)](https://datatracker.ietf.org/doc/html/rfc6455)
- [ClientWebSocket Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket)
- [Newtonsoft.Json Documentation](https://www.newtonsoft.com/json/help/html/Introduction.htm)
- [WebRTC Signaling Overview](https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API/Signaling_and_video_calling)
