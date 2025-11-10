"""
Tests for the WebSocket signaling server.

TDD Approach:
1. Write tests first (these will fail initially)
2. Implement signaling server to make tests pass
3. Refactor while keeping tests green
"""

import pytest
import json
import asyncio
from tests.conftest import TestWebSocketClient


class TestBasicSignaling:
    """Tests for basic signaling server functionality."""

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_server_accepts_connections(self, websocket_client):
        """
        TDD: Server should accept WebSocket connections.
        """
        # The fixture already connects, so if we get here, connection succeeded
        assert websocket_client.websocket is not None
        assert websocket_client.websocket.open

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_server_accepts_multiple_connections(self, two_websocket_clients):
        """
        TDD: Server should handle multiple simultaneous connections.
        """
        client1, client2 = two_websocket_clients

        assert client1.websocket.open
        assert client2.websocket.open

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_invalid_json_does_not_crash_server(self, websocket_client):
        """
        TDD: Server should handle malformed JSON gracefully.
        """
        # Send invalid JSON
        await websocket_client.websocket.send("{ this is not valid json }")

        # Server should remain connected
        await asyncio.sleep(0.1)
        assert websocket_client.websocket.open

        # Should be able to send valid message after
        valid_message = {"type": "ping"}
        await websocket_client.send_message(valid_message)
        assert websocket_client.websocket.open


class TestMessageForwarding:
    """Tests for message forwarding between clients."""

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_join_call_forwarded_to_other_clients(
        self,
        two_websocket_clients,
        sample_join_call_message,
        assert_message_structure
    ):
        """
        TDD: join_call message should be forwarded to other participants.

        Flow:
        1. Bot sends join_call
        2. GStreamer client should receive it
        """
        client1, client2 = two_websocket_clients

        # Client 1 (bot) sends join_call
        await client1.send_message(sample_join_call_message)

        # Client 2 (gstreamer) should receive it
        received = await client2.receive_message(timeout=1.0)

        assert received is not None, "Message should be forwarded"
        assert_message_structure(
            received,
            "join_call",
            ["callId", "botId", "timestamp"]
        )
        assert received["callId"] == "test-call-123"

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_offer_forwarded_to_other_clients(
        self,
        two_websocket_clients,
        sample_offer_message,
        assert_message_structure
    ):
        """
        TDD: SDP offer should be forwarded to other participants.
        """
        client1, client2 = two_websocket_clients

        # Client 1 sends offer
        await client1.send_message(sample_offer_message)

        # Client 2 should receive it
        received = await client2.receive_message(timeout=1.0)

        assert received is not None
        assert_message_structure(received, "offer", ["callId", "sdp"])
        assert received["callId"] == "test-call-123"
        assert "v=0" in received["sdp"], "SDP should be preserved"

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_answer_forwarded_to_other_clients(
        self,
        two_websocket_clients,
        sample_answer_message
    ):
        """
        TDD: SDP answer should be forwarded to other participants.
        """
        client1, client2 = two_websocket_clients

        # Client 1 sends answer
        await client1.send_message(sample_answer_message)

        # Client 2 should receive it
        received = await client2.receive_message(timeout=1.0)

        assert received is not None
        assert received["type"] == "answer"
        assert received["callId"] == "test-call-123"

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_ice_candidate_forwarded_to_other_clients(
        self,
        two_websocket_clients,
        sample_ice_candidate_message
    ):
        """
        TDD: ICE candidates should be forwarded to other participants.
        """
        client1, client2 = two_websocket_clients

        # Client 1 sends ICE candidate
        await client1.send_message(sample_ice_candidate_message)

        # Client 2 should receive it
        received = await client2.receive_message(timeout=1.0)

        assert received is not None
        assert received["type"] == "ice_candidate"
        assert "candidate" in received
        assert "sdpMid" in received["candidate"]

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_message_not_echoed_to_sender(
        self,
        two_websocket_clients,
        sample_join_call_message
    ):
        """
        TDD: Messages should NOT be echoed back to the sender.
        """
        client1, client2 = two_websocket_clients

        # Client 1 sends message
        await client1.send_message(sample_join_call_message)

        # Client 2 should receive it
        received_by_2 = await client2.receive_message(timeout=1.0)
        assert received_by_2 is not None

        # Client 1 should NOT receive it back
        received_by_1 = await client1.receive_message(timeout=0.5)
        assert received_by_1 is None, "Message should not echo to sender"


class TestCallIsolation:
    """Tests for call isolation - messages should only go to participants in the same call."""

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_messages_isolated_by_call_id(self, test_server, test_server_port):
        """
        TDD: Messages for call A should not reach clients in call B.

        This is critical for multi-tenant scenarios.
        """
        # Create 4 clients: 2 for call-A, 2 for call-B
        call_a_client1 = TestWebSocketClient(f"ws://localhost:{test_server_port}")
        call_a_client2 = TestWebSocketClient(f"ws://localhost:{test_server_port}")
        call_b_client1 = TestWebSocketClient(f"ws://localhost:{test_server_port}")
        call_b_client2 = TestWebSocketClient(f"ws://localhost:{test_server_port}")

        try:
            await call_a_client1.connect()
            await call_a_client2.connect()
            await call_b_client1.connect()
            await call_b_client2.connect()

            # Clients join their respective calls
            await call_a_client1.send_message({
                "type": "join_call",
                "callId": "call-A",
                "botId": "bot-a"
            })
            await call_b_client1.send_message({
                "type": "join_call",
                "callId": "call-B",
                "botId": "bot-b"
            })

            # Clear join_call messages
            await call_a_client2.receive_message()
            await call_b_client2.receive_message()

            # Send offer in call A
            await call_a_client1.send_message({
                "type": "offer",
                "callId": "call-A",
                "sdp": "call-a-sdp"
            })

            # Call A client should receive it
            received_a = await call_a_client2.receive_message(timeout=1.0)
            assert received_a is not None
            assert received_a["callId"] == "call-A"

            # Call B clients should NOT receive it
            received_b1 = await call_b_client1.receive_message(timeout=0.5)
            received_b2 = await call_b_client2.receive_message(timeout=0.5)
            assert received_b1 is None
            assert received_b2 is None

        finally:
            await call_a_client1.disconnect()
            await call_a_client2.disconnect()
            await call_b_client1.disconnect()
            await call_b_client2.disconnect()


class TestErrorHandling:
    """Tests for error handling and edge cases."""

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_missing_type_field_handled_gracefully(self, websocket_client):
        """
        TDD: Messages without 'type' field should not crash server.
        """
        # Send message without 'type'
        await websocket_client.send_message({
            "callId": "test-123",
            "data": "some data"
        })

        # Server should remain responsive
        await asyncio.sleep(0.1)
        assert websocket_client.websocket.open

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_missing_call_id_handled_gracefully(self, websocket_client):
        """
        TDD: Messages without 'callId' should be rejected or handled gracefully.
        """
        # Send message without callId
        await websocket_client.send_message({
            "type": "offer",
            "sdp": "test-sdp"
        })

        # Server should remain responsive
        await asyncio.sleep(0.1)
        assert websocket_client.websocket.open

    @pytest.mark.unit
    @pytest.mark.asyncio
    async def test_client_disconnect_does_not_affect_other_clients(
        self,
        test_server,
        test_server_port
    ):
        """
        TDD: One client disconnecting should not affect other clients.
        """
        client1 = TestWebSocketClient(f"ws://localhost:{test_server_port}")
        client2 = TestWebSocketClient(f"ws://localhost:{test_server_port}")

        try:
            await client1.connect()
            await client2.connect()

            # Disconnect client1
            await client1.disconnect()
            await asyncio.sleep(0.1)

            # Client2 should still be connected and functional
            assert client2.websocket.open

            # Should be able to send message
            await client2.send_message({"type": "ping"})
            assert client2.websocket.open

        finally:
            if client2.websocket and client2.websocket.open:
                await client2.disconnect()


class TestPerformance:
    """Performance and stress tests."""

    @pytest.mark.slow
    @pytest.mark.asyncio
    async def test_handles_many_messages_quickly(
        self,
        two_websocket_clients,
        sample_offer_message
    ):
        """
        Test that server can handle high message throughput.
        """
        client1, client2 = two_websocket_clients

        # Send 100 messages rapidly
        num_messages = 100
        for i in range(num_messages):
            message = sample_offer_message.copy()
            message["callId"] = f"call-{i}"
            await client1.send_message(message)

        # Should receive most/all messages
        received_count = 0
        for _ in range(num_messages):
            msg = await client2.receive_message(timeout=2.0)
            if msg:
                received_count += 1
            else:
                break

        # Should receive at least 90% (allow for some timing issues)
        assert received_count >= num_messages * 0.9
