# PolicyRecordingBot Tests

This directory contains comprehensive tests for the Teams Media Bot with WebRTC/GStreamer integration.

## Structure

```
PolicyRecordingBot.Tests/
├── PolicyRecordingBot.Tests.csproj    # Test project file
├── Properties/
│   └── AssemblyInfo.cs                # Assembly metadata
├── Helpers/
│   ├── TestLogger.cs                  # Test logger for capturing log events
│   └── MockMediaFactory.cs            # Factory for creating mock media buffers
├── Bot/
│   ├── BotMediaStreamTests.cs         # Tests for audio/video media handling
│   └── CallHandlerTests.cs            # Tests for call lifecycle management
├── WebRTC/
│   ├── MediaBridgeTests.cs            # TDD tests for MediaBridge (to be implemented)
│   ├── WebRTCManagerTests.cs          # TDD tests for WebRTCManager (to be implemented)
│   └── AudioConverterTests.cs         # Tests for PCM ↔ Opus conversion (future)
└── Signaling/
    └── SignalingClientTests.cs        # TDD tests for WebSocket signaling client

```

## Test-Driven Development (TDD) Workflow

This test suite is designed to support TDD. Many tests are marked with `[Ignore("TDD: ...")]` because the components don't exist yet.

### TDD Process:

1. **RED** - Tests are written first and fail
2. **GREEN** - Implement code to make tests pass
3. **REFACTOR** - Improve code while keeping tests green

### Example Workflow:

```bash
# 1. Remove [Ignore] attribute from a test
# Edit: WebRTC/MediaBridgeTests.cs
#   Remove: [Ignore("TDD: Implement MediaBridge class")]

# 2. Run test (will fail - RED)
dotnet test --filter "FullyQualifiedName~MediaBridgeTests.Constructor_ValidDependencies_InitializesSuccessfully"

# 3. Implement MediaBridge class
# Create: FrontEnd/Bot/WebRTC/MediaBridge.cs

# 4. Run test again (should pass - GREEN)
dotnet test --filter "FullyQualifiedName~MediaBridgeTests"

# 5. Refactor and ensure tests stay green
dotnet test --filter "FullyQualifiedName~MediaBridgeTests"
```

## Running Tests

### Prerequisites

```bash
# Install .NET SDK 6.0+
dotnet --version

# Restore dependencies
cd PolicyRecordingBot.Tests
dotnet restore
```

### Run All Tests

```bash
dotnet test
```

### Run Specific Test Class

```bash
# Run only MediaBridge tests
dotnet test --filter "FullyQualifiedName~MediaBridgeTests"

# Run only BotMediaStream tests
dotnet test --filter "FullyQualifiedName~BotMediaStreamTests"
```

### Run Tests with Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"

# View coverage report
# Report will be in: TestResults/{guid}/coverage.cobertura.xml
```

### Run Tests in Watch Mode (Auto-rerun on changes)

```bash
dotnet watch test
```

## Test Helpers

### TestLogger

Captures log events for assertions in tests:

```csharp
[TestMethod]
public void SomeTest()
{
    var logger = new TestLogger("MyTest");

    // ... perform actions that log

    // Assert
    logger.HasErrors().Should().BeFalse();
    var warnings = logger.GetMessages(TraceLevel.Warning);
    warnings.Should().Contain(msg => msg.Contains("expected warning"));
}
```

### MockMediaFactory

Generates test audio data and media buffers:

```csharp
// Generate 20ms of test audio (440Hz tone)
byte[] testAudio = MockMediaFactory.GenerateTestAudioData(640, frequency: 440);

// Verify frequency detection
double detectedFreq = MockMediaFactory.DetectFrequency(testAudio);
detectedFreq.Should().BeInRange(435, 445); // ±5Hz tolerance
```

## Test Categories

Tests are organized by component:

### 1. Bot Tests (`Bot/`)
- **BotMediaStreamTests**: Audio/video media handling
- **CallHandlerTests**: Call lifecycle and participant management

### 2. WebRTC Tests (`WebRTC/`)
- **MediaBridgeTests** (TDD): Audio bridging between Teams and WebRTC
- **WebRTCManagerTests** (TDD): WebRTC peer connection management
- **AudioConverterTests** (Future): Format conversion

### 3. Signaling Tests (`Signaling/`)
- **SignalingClientTests** (TDD): WebSocket signaling protocol

## Current Status

| Component | Test File | Status | Tests |
|-----------|-----------|--------|-------|
| BotMediaStream | BotMediaStreamTests.cs | ⚠️ Templates | 4 ignored |
| CallHandler | CallHandlerTests.cs | ⚠️ Templates | 2 ignored |
| **MediaBridge** | MediaBridgeTests.cs | 🔴 TDD | 7 failing |
| **WebRTCManager** | WebRTCManagerTests.cs | 🔴 TDD | 8 failing |
| **SignalingClient** | SignalingClientTests.cs | 🔴 TDD | 8 failing |

**Legend:**
- 🔴 TDD - Tests written, implementation pending
- ⚠️ Templates - Test templates, needs mocks
- ✅ Passing - All tests passing

## Integration with CI

These tests are automatically run by GitHub Actions on every push:

- Workflow: `.github/workflows/teams-media-bot-ci.yml`
- Job: `build-and-test-bot`
- Runs on: Windows (for .NET Framework compatibility)

View results:
```bash
./scripts/check-ci.sh
```

## Next Steps

1. **Start with MediaBridge** (most critical component)
   - Remove `[Ignore]` from `MediaBridgeTests.Constructor_ValidDependencies_InitializesSuccessfully`
   - Implement `FrontEnd/Bot/WebRTC/MediaBridge.cs`
   - Make tests pass one by one

2. **Implement AudioConverter**
   - PCM 16kHz → Opus conversion
   - Opus → PCM 16kHz conversion
   - Use Concentus.Opus library

3. **Implement WebRTCManager**
   - SIPSorcery integration
   - Peer connection management
   - RTP packet handling

4. **Implement SignalingClient**
   - WebSocket client
   - Message serialization
   - Event handlers

## Tips for TDD

### Good Test Practices

✅ **DO:**
- Write tests before implementation
- Test one thing per test
- Use descriptive test names
- Use AAA pattern (Arrange, Act, Assert)
- Mock external dependencies
- Use FluentAssertions for readable assertions

❌ **DON'T:**
- Test implementation details
- Write tests that depend on other tests
- Leave tests ignored long-term
- Skip error case testing

### Example Test Structure

```csharp
[TestMethod]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - Set up test conditions
    var mockDependency = new Mock<IDependency>();
    var sut = new SystemUnderTest(mockDependency.Object);
    var testData = CreateTestData();

    // Act - Execute the method being tested
    var result = sut.MethodName(testData);

    // Assert - Verify expected outcomes
    result.Should().NotBeNull();
    mockDependency.Verify(
        d => d.ExpectedMethod(It.IsAny<int>()),
        Times.Once
    );
}
```

## Resources

- [MSTest Documentation](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest)
- [Moq Documentation](https://github.com/moq/moq4)
- [FluentAssertions](https://fluentassertions.com/)
- [TDD Best Practices](https://martinfowler.com/bliki/TestDrivenDevelopment.html)

## Questions?

See the main [CI_SETUP.md](../CI_SETUP.md) for comprehensive testing documentation.
