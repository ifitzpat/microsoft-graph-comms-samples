"""
Pytest fixtures for signaling server tests.

This module provides reusable test fixtures for:
- Test WebSocket clients
- Mock signaling servers
- Test data generators
"""

import asyncio
import json
import pytest
import websockets
from typing import AsyncGenerator, List, Dict, Any


@pytest.fixture
def event_loop():
    """Create event loop for async tests."""
    loop = asyncio.get_event_loop_policy().new_event_loop()
    yield loop
    loop.close()


@pytest.fixture
async def test_server_port() -> int:
    """Provides a free port for test server."""
    return 8766  # Different from production (8765)


@pytest.fixture
def sample_join_call_message() -> Dict[str, Any]:
    """Sample join_call message for testing."""
    return {
        "type": "join_call",
        "callId": "test-call-123",
        "botId": "test-bot-1",
        "timestamp": 1699564800
    }


@pytest.fixture
def sample_offer_message() -> Dict[str, Any]:
    """Sample SDP offer message for testing."""
    return {
        "type": "offer",
        "callId": "test-call-123",
        "sdp": "v=0\r\no=- 123456 2 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n"
    }


@pytest.fixture
def sample_answer_message() -> Dict[str, Any]:
    """Sample SDP answer message for testing."""
    return {
        "type": "answer",
        "callId": "test-call-123",
        "sdp": "v=0\r\no=- 654321 2 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n"
    }


@pytest.fixture
def sample_ice_candidate_message() -> Dict[str, Any]:
    """Sample ICE candidate message for testing."""
    return {
        "type": "ice_candidate",
        "callId": "test-call-123",
        "candidate": {
            "candidate": "candidate:1 1 UDP 2130706431 192.168.1.100 54321 typ host",
            "sdpMid": "0",
            "sdpMLineIndex": 0
        }
    }


class TestWebSocketClient:
    """Helper class for creating test WebSocket clients."""

    def __init__(self, uri: str):
        self.uri = uri
        self.websocket = None
        self.received_messages: List[Dict[str, Any]] = []

    async def connect(self):
        """Connect to WebSocket server."""
        self.websocket = await websockets.connect(self.uri)

    async def disconnect(self):
        """Disconnect from WebSocket server."""
        if self.websocket:
            await self.websocket.close()

    async def send_message(self, message: Dict[str, Any]):
        """Send JSON message to server."""
        await self.websocket.send(json.dumps(message))

    async def receive_message(self, timeout: float = 1.0) -> Dict[str, Any]:
        """Receive and parse JSON message from server."""
        try:
            message = await asyncio.wait_for(
                self.websocket.recv(),
                timeout=timeout
            )
            parsed = json.loads(message)
            self.received_messages.append(parsed)
            return parsed
        except asyncio.TimeoutError:
            return None

    async def receive_all_messages(self, timeout: float = 0.5) -> List[Dict[str, Any]]:
        """Receive all pending messages."""
        messages = []
        while True:
            msg = await self.receive_message(timeout=timeout)
            if msg is None:
                break
            messages.append(msg)
        return messages


@pytest.fixture
async def websocket_client(test_server_port) -> AsyncGenerator[TestWebSocketClient, None]:
    """Provides a test WebSocket client."""
    client = TestWebSocketClient(f"ws://localhost:{test_server_port}")
    await client.connect()
    yield client
    await client.disconnect()


@pytest.fixture
async def two_websocket_clients(test_server_port) -> AsyncGenerator[tuple, None]:
    """Provides two connected WebSocket clients for multi-client tests."""
    client1 = TestWebSocketClient(f"ws://localhost:{test_server_port}")
    client2 = TestWebSocketClient(f"ws://localhost:{test_server_port}")

    await client1.connect()
    await client2.connect()

    yield (client1, client2)

    await client1.disconnect()
    await client2.disconnect()


@pytest.fixture
def assert_message_structure():
    """Fixture that provides a helper to validate message structure."""

    def _assert_structure(message: Dict[str, Any], expected_type: str, required_fields: List[str]):
        """
        Assert that a message has the correct structure.

        Args:
            message: The message to validate
            expected_type: Expected value of 'type' field
            required_fields: List of required field names
        """
        assert "type" in message, "Message missing 'type' field"
        assert message["type"] == expected_type, f"Expected type '{expected_type}', got '{message['type']}'"

        for field in required_fields:
            assert field in message, f"Message missing required field '{field}'"

    return _assert_structure
