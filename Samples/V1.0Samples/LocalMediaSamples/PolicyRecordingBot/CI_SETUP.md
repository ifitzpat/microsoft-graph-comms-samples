# CI/CD Setup for Teams Media Bot

This document describes the Continuous Integration (CI) setup for the Teams Media Bot project, including workflows, monitoring tools, and test-driven development practices.

## Overview

The CI system is designed to:
- Build and test the C# Teams bot automatically
- Run code quality checks
- Test the Python signaling server
- Provide real-time monitoring of build status
- Support test-driven development (TDD) workflows

## Table of Contents

1. [CI Workflows](#ci-workflows)
2. [Monitoring Tools](#monitoring-tools)
3. [Test-Driven Development](#test-driven-development)
4. [Setting Up Locally](#setting-up-locally)
5. [Troubleshooting](#troubleshooting)

---

## CI Workflows

### Main Workflow: `teams-media-bot-ci.yml`

**Location:** `.github/workflows/teams-media-bot-ci.yml`

**Triggers:**
- Push to `main` or `claude/**` branches
- Pull requests
- Manual trigger via `workflow_dispatch`

**Jobs:**

#### 1. Build and Test Bot (`build-and-test-bot`)
- **Platform:** Windows (required for .NET Framework dependencies)
- **Steps:**
  - Checkout code
  - Setup .NET SDK 6.0
  - Restore NuGet packages (with caching)
  - Build solution in Release mode
  - Run unit tests (if test project exists)
  - Upload test results and build artifacts

**Artifacts produced:**
- `teams-media-bot-build` - Compiled binaries
- `test-results-bot` - Test results (.trx format)

#### 2. Code Quality (`code-quality`)
- **Platform:** Ubuntu
- **Steps:**
  - Run `dotnet format` to verify code style
  - Security scan with Trivy
  - Upload results to GitHub Security tab

#### 3. Test Signaling Server (`test-signaling-server`)
- **Platform:** Ubuntu
- **Steps:**
  - Setup Python 3.11
  - Install test dependencies (pytest, websockets, etc.)
  - Run pytest with coverage reporting
  - Upload coverage reports

**Artifacts produced:**
- `signaling-server-coverage` - HTML and XML coverage reports

#### 4. Integration Tests (`integration-tests`)
- **Platform:** Ubuntu
- **Status:** Placeholder for future implementation
- **Plans:**
  - End-to-end audio flow tests
  - WebRTC connection establishment
  - Teams ↔ Signaling ↔ GStreamer integration

#### 5. CI Summary (`ci-summary`)
- Aggregates results from all jobs
- Provides clear pass/fail status
- Always runs (even if earlier jobs fail)

### Concurrency Control

The workflow uses GitHub's concurrency groups to:
- Cancel in-progress runs when new commits are pushed
- Prevent resource waste
- Speed up feedback cycles

```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.head_ref && github.ref || github.run_id }}
  cancel-in-progress: true
```

---

## Monitoring Tools

### 1. Quick Status Check: `check-ci.sh`

**Location:** `scripts/check-ci.sh`

**Usage:**
```bash
# Check default branch
./scripts/check-ci.sh

# Check specific branch
./scripts/check-ci.sh claude/my-feature-branch
```

**Features:**
- Shows last 5 workflow runs
- Color-coded status (✅ success, ❌ failure, ⏳ in progress)
- Displays failed job details
- Summary statistics
- Exit codes:
  - `0` - Latest run succeeded
  - `1` - Latest run failed
  - `2` - Latest run in progress
  - `3` - Unknown status

**Example output:**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Teams Media Bot - CI Status Check
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ SUCCESS - Teams Media Bot CI
   Run ID: 12345678
   Created: 2025-11-10T04:30:00Z
   URL: https://github.com/user/repo/actions/runs/12345678

Summary (last 5 runs):
  ✅ Success: 4
  ❌ Failure: 1
  ⏳ In Progress: 0

✅ Latest run succeeded!
```

### 2. Live Monitor: `monitor-ci.sh`

**Location:** `scripts/ci/monitor-ci.sh`

**Requirements:**
- `gh` (GitHub CLI)
- `jq` (JSON processor)

**Installation:**
```bash
# Install GitHub CLI
curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | sudo dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg
echo "deb [signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | sudo tee /etc/apt/sources.list.d/github-cli.list
sudo apt update
sudo apt install gh

# Install jq
sudo apt install jq

# Authenticate
gh auth login
```

**Usage:**
```bash
# One-time status check
./scripts/ci/monitor-ci.sh

# Watch mode (auto-refresh every 30s)
./scripts/ci/monitor-ci.sh --watch

# Watch with custom interval
./scripts/ci/monitor-ci.sh --watch --interval 60

# Monitor specific branch
./scripts/ci/monitor-ci.sh --branch main --watch
```

**Features:**
- Real-time job status with emoji indicators
- Commit information
- Individual job progress
- Auto-refresh in watch mode
- Artifact download prompt on completion
- Clear, readable output

**Example output:**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Teams Media Bot CI Status
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Branch:     claude/teams-media-bot-plan-011CUy6344qyYqi3aUaekED9
Run:        #42 (1234567890)
Commit:     a1b2c3d - Add WebRTC integration
Created:    2025-11-10T04:30:00Z
Updated:    2025-11-10T04:35:00Z
Status:     ⟳ IN PROGRESS
URL:        https://github.com/user/repo/actions/runs/1234567890

Jobs:
  ✓ Build Teams Media Bot: completed success
  ⟳ Code Quality Analysis: in_progress
  - Test Signaling Server: queued
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Refreshing in 30 seconds... (Ctrl+C to stop)
```

---

## Test-Driven Development

### Philosophy

The CI system is designed to support TDD workflows:
1. **Write failing tests first** (RED)
2. **Implement code to pass tests** (GREEN)
3. **Refactor while keeping tests green** (REFACTOR)
4. **Commit and push** - CI validates
5. **Repeat**

### Test Project Structure

#### C# Bot Tests

**Location:** `PolicyRecordingBot.Tests/` (to be created)

**Setup:**
```bash
cd Samples/V1.0Samples/LocalMediaSamples/PolicyRecordingBot
dotnet new mstest -n PolicyRecordingBot.Tests
cd PolicyRecordingBot.Tests

# Add references
dotnet add reference ../FrontEnd/PolicyRecordingBot.FrontEnd.csproj

# Add test packages
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package MSTest.TestAdapter
dotnet add package MSTest.TestFramework
```

**Example Test:**
```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using FluentAssertions;
using Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC;

namespace PolicyRecordingBot.Tests.WebRTC
{
    [TestClass]
    public class MediaBridgeTests
    {
        [TestMethod]
        public void SendAudioToWebRTC_ValidBuffer_EnqueuesFrame()
        {
            // Arrange
            var mockLogger = new Mock<IGraphLogger>();
            var mockWebRtcManager = new Mock<WebRTCManager>();
            var bridge = new MediaBridge(mockLogger.Object, mockWebRtcManager.Object);

            var testPcmData = new byte[640]; // 20ms @ 16kHz

            // Act
            bridge.SendAudioToWebRTC(testPcmData, 12345);

            // Assert
            mockWebRtcManager.Verify(
                x => x.SendAudioAsync(It.IsAny<byte[]>()),
                Times.Once
            );
        }

        [TestMethod]
        public void SendAudioToWebRTC_NullBuffer_ThrowsArgumentNullException()
        {
            // Arrange
            var mockLogger = new Mock<IGraphLogger>();
            var mockWebRtcManager = new Mock<WebRTCManager>();
            var bridge = new MediaBridge(mockLogger.Object, mockWebRtcManager.Object);

            // Act & Assert
            Action act = () => bridge.SendAudioToWebRTC(null, 12345);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
```

#### Python Signaling Server Tests

**Location:** `signaling/tests/` (to be created)

**Setup:**
```bash
cd Samples/V1.0Samples/LocalMediaSamples/PolicyRecordingBot
mkdir -p signaling/tests

# Install test dependencies
pip install pytest pytest-asyncio pytest-cov websockets
```

**Example Test:**
```python
# signaling/tests/test_signaling_server.py
import pytest
import asyncio
import json
import websockets
from signaling_server import SignalingServer

@pytest.mark.asyncio
async def test_join_call_message_forwarding():
    """Test that join_call messages are forwarded to other participants"""

    # Start test server
    server = SignalingServer(port=8766)
    await server.start()

    try:
        # Connect two clients
        async with websockets.connect("ws://localhost:8766") as bot_ws:
            async with websockets.connect("ws://localhost:8766") as gst_ws:

                # Bot sends join_call
                join_msg = {
                    "type": "join_call",
                    "callId": "test-call-123",
                    "botId": "bot-1"
                }
                await bot_ws.send(json.dumps(join_msg))

                # GStreamer should receive it
                message = await asyncio.wait_for(gst_ws.recv(), timeout=1.0)
                data = json.loads(message)

                assert data["type"] == "join_call"
                assert data["callId"] == "test-call-123"

    finally:
        await server.stop()

@pytest.mark.asyncio
async def test_ice_candidate_exchange():
    """Test ICE candidate message exchange"""
    # Test implementation
    pass
```

**Running tests:**
```bash
# Run with coverage
cd signaling
pytest tests/ -v --cov=. --cov-report=html --cov-report=term

# View coverage report
open htmlcov/index.html
```

### Local TDD Workflow

#### For C# Development:

```bash
# 1. Write failing test
vim PolicyRecordingBot.Tests/WebRTC/MediaBridgeTests.cs

# 2. Run tests (should FAIL)
dotnet test --verbosity normal

# 3. Implement code
vim FrontEnd/Bot/WebRTC/MediaBridge.cs

# 4. Run tests again (should PASS)
dotnet test --verbosity normal

# 5. Commit
git add .
git commit -m "feat: implement MediaBridge audio queueing"

# 6. Push - CI runs automatically
git push origin claude/my-feature-branch

# 7. Monitor CI
./scripts/check-ci.sh
```

#### For Python Development:

```bash
# 1. Write failing test
vim signaling/tests/test_signaling_server.py

# 2. Run tests (should FAIL)
cd signaling && pytest tests/ -v

# 3. Implement code
vim signaling/signaling_server.py

# 4. Run tests (should PASS)
pytest tests/ -v

# 5. Commit and push
git add . && git commit -m "feat: add ICE candidate forwarding"
git push

# 6. Monitor CI
./scripts/ci/monitor-ci.sh --watch
```

### CI Integration with TDD

The CI workflow automatically:
1. Detects test projects
2. Runs all tests
3. Generates coverage reports
4. Uploads artifacts for inspection
5. Fails the build if tests fail

**This enforces TDD discipline:**
- ❌ Cannot merge PRs with failing tests
- ✅ Code coverage visibility
- 📊 Test trends over time

---

## Setting Up Locally

### Prerequisites

**For C# Bot Development:**
- .NET SDK 6.0 or later
- Visual Studio 2022 or VS Code with C# extension
- Git

**For Python Signaling Development:**
- Python 3.11+
- pip

**For CI Monitoring:**
- GitHub CLI (`gh`)
- `jq` JSON processor
- `curl`

### Initial Setup

```bash
# 1. Clone repository
git clone https://github.com/ifitzpat/microsoft-graph-comms-samples.git
cd microsoft-graph-comms-samples

# 2. Checkout your branch
git checkout -b claude/my-feature-branch

# 3. Setup C# environment
cd Samples/V1.0Samples/LocalMediaSamples/PolicyRecordingBot
dotnet restore

# 4. Setup Python environment (if working on signaling)
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt  # Create this file with dependencies

# 5. Make CI scripts executable
chmod +x scripts/check-ci.sh scripts/ci/monitor-ci.sh

# 6. Test CI monitoring
./scripts/check-ci.sh
```

### Running Locally

**C# Bot:**
```bash
cd Samples/V1.0Samples/LocalMediaSamples/PolicyRecordingBot

# Build
dotnet build --configuration Release

# Run tests
dotnet test --verbosity normal

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

**Python Signaling Server:**
```bash
cd signaling

# Run tests
pytest tests/ -v

# With coverage
pytest tests/ -v --cov=. --cov-report=html

# Start server
python signaling_server.py
```

---

## Troubleshooting

### CI Build Failures

**Problem:** Build fails with "NuGet package not found"

**Solution:**
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore again
dotnet restore --force
```

**Problem:** Tests fail locally but pass in CI (or vice versa)

**Solution:**
- Check .NET SDK versions match
- Verify test dependencies are consistent
- Check environment variables
- Review test isolation (tests should not depend on each other)

### Monitoring Script Issues

**Problem:** `./scripts/check-ci.sh` returns "Failed to fetch workflow runs"

**Solution:**
1. Check internet connection
2. Verify repository name is correct
3. Ensure branch exists
4. Check GitHub API rate limits

**Problem:** `monitor-ci.sh` says "Not authenticated"

**Solution:**
```bash
gh auth login
# Follow prompts to authenticate
```

**Problem:** `monitor-ci.sh` can't find workflow

**Solution:**
- Ensure workflow file exists in `.github/workflows/`
- Check workflow name matches exactly
- Verify workflow has run at least once

### Test Failures

**Problem:** Test project not found

**Solution:**
```bash
# Create test project
cd Samples/V1.0Samples/LocalMediaSamples/PolicyRecordingBot
dotnet new mstest -n PolicyRecordingBot.Tests

# Add references
cd PolicyRecordingBot.Tests
dotnet add reference ../FrontEnd/PolicyRecordingBot.FrontEnd.csproj
```

**Problem:** Python tests fail with import errors

**Solution:**
```bash
# Ensure you're in virtual environment
source venv/bin/activate

# Install in development mode
pip install -e .

# Or add path
export PYTHONPATH="${PYTHONPATH}:$(pwd)"
```

---

## GStreamer Integration Reference

The llama.cpp reference implementation (cloned during setup) provides excellent examples of:
- GStreamer plugin architecture
- Test pipelines
- Build systems (Meson)
- CI integration for native code

**Key files to reference:**
- `/tmp/llama-cpp-ref/tools/gstreamer/gstllama.c` - Plugin implementation
- `/tmp/llama-cpp-ref/tools/gstreamer/README.md` - Architecture docs
- `/tmp/llama-cpp-ref/tools/gstreamer/test-pipeline.sh` - Test examples
- `/tmp/llama-cpp-ref/.github/workflows/build-ffi.yml` - CI for native builds

**Applying to Teams Bot:**
When implementing the GStreamer WebRTC client (Phase 5), use these patterns for:
- Buffer handling
- Signal emissions
- Property management
- Error handling
- Test coverage

---

## Next Steps

1. **Create test projects:**
   ```bash
   ./scripts/create-test-projects.sh  # To be created
   ```

2. **Write first tests:**
   - Start with MediaBridge tests
   - Add SignalingClient tests
   - Build incrementally

3. **Enable branch protection:**
   - Require CI to pass before merge
   - Require code review
   - Require up-to-date branches

4. **Set up notifications:**
   - GitHub Actions status badges
   - Slack/Discord webhooks
   - Email on failure

5. **Add more CI jobs:**
   - Integration tests
   - Performance benchmarks
   - Docker image builds
   - Documentation generation

---

## Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [MSTest Framework](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest)
- [pytest Documentation](https://docs.pytest.org/)
- [GitHub CLI Manual](https://cli.github.com/manual/)
- [Test-Driven Development Guide](https://martinfowler.com/bliki/TestDrivenDevelopment.html)

---

## Summary

The CI system provides:
- ✅ Automated testing on every push
- 📊 Code quality metrics
- 🔍 Security scanning
- 📈 Coverage reporting
- 🛠️ Easy monitoring tools
- 🎯 TDD-friendly workflow

**Use the monitoring scripts regularly:**
```bash
# Quick check before committing
./scripts/check-ci.sh

# Watch while implementing features
./scripts/ci/monitor-ci.sh --watch
```

This ensures high code quality and prevents regressions as the Teams Media Bot project evolves!
