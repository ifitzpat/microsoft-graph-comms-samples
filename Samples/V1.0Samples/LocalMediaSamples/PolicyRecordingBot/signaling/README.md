# Signaling Server

WebSocket-based signaling server for WebRTC negotiation between Teams Media Bot and GStreamer services.

## Purpose

This server facilitates WebRTC signaling by:
- Forwarding SDP offers/answers between bot and GStreamer
- Exchanging ICE candidates
- Managing call sessions
- Isolating messages by call ID

## Architecture

```
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│  Teams Bot   │ ──ws──→ │   Signaling  │ ──ws──→ │  GStreamer   │
│  (C#)        │ ←──ws── │   Server     │ ←──ws── │  (Python)    │
└──────────────┘         └──────────────┘         └──────────────┘
                              (Python)
```

## Message Protocol

### Message Types

1. **join_call** - Bot joins a Teams call
2. **offer** - SDP offer for WebRTC negotiation
3. **answer** - SDP answer for WebRTC negotiation
4. **ice_candidate** - ICE candidate for NAT traversal
5. **leave_call** - Bot leaves a call

### Message Format

All messages are JSON with this structure:

```json
{
  "type": "message_type",
  "callId": "unique-call-id",
  ... additional fields ...
}
```

See [PROTOCOL.md](docs/PROTOCOL.md) for detailed message specifications.

## Development Setup

### Prerequisites

- Python 3.11+
- pip

### Installation

```bash
cd signaling

# Create virtual environment
python3 -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt

# Install development dependencies
pip install -r requirements-dev.txt
```

## Running Tests

### Quick Start

```bash
# Run all tests
pytest

# Run with coverage
pytest --cov=src --cov-report=html

# Watch mode (auto-rerun on changes)
pytest-watch
```

### Test Categories

Run specific test categories:

```bash
# Unit tests only (fast)
pytest -m unit

# Integration tests (may need server running)
pytest -m integration

# WebSocket tests
pytest -m websocket

# Skip slow tests
pytest -m "not slow"
```

### Test Output

```bash
# Verbose output
pytest -v

# Show print statements
pytest -s

# Stop on first failure
pytest -x

# Run specific test
pytest tests/test_signaling_server.py::TestBasicSignaling::test_server_accepts_connections
```

### Coverage Reports

After running `pytest --cov`:

```bash
# View HTML report
open htmlcov/index.html

# View in terminal
pytest --cov=src --cov-report=term-missing
```

## Test Structure

```
tests/
├── __init__.py
├── conftest.py                    # Pytest fixtures and helpers
└── test_signaling_server.py       # Main test suite
```

### Test Classes

1. **TestBasicSignaling** - Connection handling
2. **TestMessageForwarding** - Message routing
3. **TestCallIsolation** - Multi-tenant isolation
4. **TestErrorHandling** - Error scenarios
5. **TestPerformance** - Stress and performance tests

## TDD Workflow

**Current Status:** Tests are written, server implementation pending

### Implementation Steps:

1. **Create server** (`src/signaling_server.py`)
   ```python
   # Implement basic WebSocket server
   async def handler(websocket, path):
       # Handle connections
       pass
   ```

2. **Remove test skip marker**
   ```python
   # In test_signaling_server.py, remove:
   pytestmark = pytest.mark.skip(...)
   ```

3. **Run tests (RED)**
   ```bash
   pytest tests/test_signaling_server.py
   ```

4. **Implement features until tests pass (GREEN)**

5. **Refactor** while keeping tests green

### Example TDD Cycle:

```bash
# 1. Run tests (should fail)
pytest tests/test_signaling_server.py::TestBasicSignaling -v

# 2. Implement minimal code to pass
vim src/signaling_server.py

# 3. Run tests again
pytest tests/test_signaling_server.py::TestBasicSignaling -v

# 4. Repeat until all tests pass
```

## Test Fixtures

### Available Fixtures (from `conftest.py`)

- `websocket_client` - Single connected WebSocket client
- `two_websocket_clients` - Tuple of two connected clients
- `test_server_port` - Port number for test server (8766)
- `sample_join_call_message` - Example join_call message
- `sample_offer_message` - Example SDP offer
- `sample_answer_message` - Example SDP answer
- `sample_ice_candidate_message` - Example ICE candidate
- `assert_message_structure` - Helper to validate message format

### Using Fixtures

```python
@pytest.mark.asyncio
async def test_example(websocket_client, sample_offer_message):
    # websocket_client is already connected
    await websocket_client.send_message(sample_offer_message)
    response = await websocket_client.receive_message()
    assert response is not None
```

## Integration with CI

Tests run automatically via GitHub Actions:

- Workflow: `.github/workflows/teams-media-bot-ci.yml`
- Job: `test-signaling-server`
- Triggers: On push, pull request

View CI status:
```bash
# From repository root
./scripts/check-ci.sh
```

## Code Quality

### Linting

```bash
# Check style
flake8 src/ tests/

# Auto-format
black src/ tests/
```

### Type Checking

```bash
# Run mypy
mypy src/
```

## Production Deployment

Once tests pass and implementation is complete:

```bash
# Start server
python src/signaling_server.py

# Or with specific port
python src/signaling_server.py --port 8765
```

See [DEPLOYMENT.md](docs/DEPLOYMENT.md) for production setup (to be created).

## Troubleshooting

### Tests Hang or Timeout

- Check if test server port (8766) is available
- Ensure no firewall blocking localhost connections
- Try increasing timeout in `pytest.ini`

### Import Errors

- Ensure virtual environment is activated
- Install dependencies: `pip install -r requirements.txt`
- Add project root to PYTHONPATH: `export PYTHONPATH="${PYTHONPATH}:$(pwd)"`

### WebSocket Connection Refused

- Test server must be running before tests
- Or tests should start their own test server (recommended)
- Check test fixtures in `conftest.py`

## Resources

- [pytest Documentation](https://docs.pytest.org/)
- [pytest-asyncio](https://github.com/pytest-dev/pytest-asyncio)
- [websockets Library](https://websockets.readthedocs.io/)
- [WebRTC Signaling](https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API/Signaling_and_video_calling)

## Next Steps

1. ✅ Test suite complete
2. ⬜ Implement `src/signaling_server.py`
3. ⬜ Make all tests pass
4. ⬜ Add monitoring and logging
5. ⬜ Production deployment documentation

## Questions?

See main [CI_SETUP.md](../CI_SETUP.md) for comprehensive testing documentation.
