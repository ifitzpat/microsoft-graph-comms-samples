#!/usr/bin/env python3
"""
WebSocket Signaling Server for Teams Media Bot WebRTC Negotiation.

This server acts as a signaling intermediary between:
- Teams Media Bot (C#) - WebSocket client
- GStreamer Service (Python) - WebSocket client

Message Flow:
1. Bot joins Teams call → sends join_call message
2. GStreamer receives join_call → prepares to connect
3. Bot creates WebRTC offer → sends offer message
4. GStreamer receives offer → sends answer message
5. Both exchange ICE candidates for NAT traversal
6. WebRTC peer connection established (direct audio flow)

Architecture:
- Async WebSocket server (websockets library)
- In-memory connection tracking by callId
- Simple message forwarding (no state management)
- Multi-tenant: Messages isolated by callId
"""

import asyncio
import json
import logging
import sys
from collections import defaultdict
from typing import Dict, Set
import websockets
from websockets.server import WebSocketServerProtocol

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout)
    ]
)

logger = logging.getLogger(__name__)


class SignalingServer:
    """
    WebSocket signaling server for WebRTC negotiation.

    Maintains a mapping of callId -> set of connected clients.
    Forwards messages to all clients in the same call except the sender.
    """

    def __init__(self, host: str = "0.0.0.0", port: int = 8765):
        """
        Initialize signaling server.

        Args:
            host: Host address to bind to
            port: Port to listen on
        """
        self.host = host
        self.port = port

        # Mapping: callId -> set of WebSocket connections (actual participants)
        self.call_connections: Dict[str, Set[WebSocketServerProtocol]] = defaultdict(set)

        # Mapping: WebSocket -> set of callIds client has joined
        self.client_calls: Dict[WebSocketServerProtocol, Set[str]] = defaultdict(set)

        # Mapping: callId -> set of WebSocket connections that received join_call (subscribed)
        self.call_subscriptions: Dict[str, Set[WebSocketServerProtocol]] = defaultdict(set)

        # Set of all connected WebSocket clients (tracked immediately on connection)
        self.all_clients: Set[WebSocketServerProtocol] = set()

        # Statistics
        self.total_connections = 0
        self.total_messages = 0

    async def handle_client(self, websocket: WebSocketServerProtocol, path: str):
        """
        Handle a client WebSocket connection.

        Args:
            websocket: WebSocket connection
            path: Request path (unused)
        """
        client_id = id(websocket)
        remote_address = websocket.remote_address

        self.total_connections += 1

        # Track this client immediately upon connection
        self.all_clients.add(websocket)

        logger.info(f"Client {client_id} connected from {remote_address} (total connections: {self.total_connections})")

        try:
            async for message in websocket:
                await self.handle_message(websocket, message)

        except websockets.exceptions.ConnectionClosed as e:
            logger.info(f"Client {client_id} connection closed: {e.code} {e.reason}")

        except Exception as e:
            logger.error(f"Error handling client {client_id}: {e}", exc_info=True)

        finally:
            # Cleanup: Remove client from all calls
            await self.cleanup_client(websocket)
            logger.info(f"Client {client_id} disconnected")

    async def handle_message(self, sender: WebSocketServerProtocol, message: str):
        """
        Handle a message from a client.

        Args:
            sender: WebSocket that sent the message
            message: Raw message string (should be JSON)
        """
        self.total_messages += 1
        sender_id = id(sender)

        try:
            # Parse JSON
            data = json.loads(message)

            # Validate message structure
            if not isinstance(data, dict):
                logger.warning(f"Client {sender_id} sent non-object message: {type(data)}")
                return

            message_type = data.get("type")
            call_id = data.get("callId")

            # Log received message
            logger.info(f"Client {sender_id} → {message_type} for call {call_id}")

            # Validate required fields
            if not message_type:
                logger.warning(f"Client {sender_id} sent message without 'type' field")
                return

            if not call_id:
                logger.warning(f"Client {sender_id} sent message without 'callId' field")
                return

            # Auto-join client to call if not already joined
            # This allows clients to send offer/answer/ice without explicit join_call
            if sender not in self.call_connections[call_id]:
                self.call_connections[call_id].add(sender)
                self.client_calls[sender].add(call_id)
                # Also add to subscriptions when they actively participate
                self.call_subscriptions[call_id].add(sender)
                logger.info(f"Client {sender_id} auto-joined call {call_id} ({len(self.call_connections[call_id])} participants)")

            # Handle explicit join_call (log separately for clarity)
            if message_type == "join_call":
                logger.info(f"Client {sender_id} sent explicit join_call for {call_id}")

            # Forward message to other participants in the same call
            # For join_call, also broadcast to any clients that haven't joined yet (so they can prepare)
            await self.forward_message(sender, call_id, message, broadcast_join=message_type == "join_call")

        except json.JSONDecodeError as e:
            logger.warning(f"Client {sender_id} sent invalid JSON: {e}")
            # Don't crash - just ignore invalid messages

        except Exception as e:
            logger.error(f"Error processing message from client {sender_id}: {e}", exc_info=True)

    async def forward_message(self, sender: WebSocketServerProtocol, call_id: str, message: str, broadcast_join: bool = False):
        """
        Forward a message to all participants in a call except the sender.

        Forwarding logic:
        - join_call: Broadcast to ALL clients + add them to subscription list
        - Other messages: Forward to participants + subscribed clients who haven't joined other calls

        Args:
            sender: WebSocket that sent the message
            call_id: Call identifier
            message: Message to forward (JSON string)
            broadcast_join: If True, broadcast to all connected clients (used for join_call)
        """
        if broadcast_join:
            # For join_call: broadcast to clients not in any call yet (discovery mode)
            # Clients already in a call don't need discovery broadcasts
            recipients = set()
            for client in self.all_clients:
                if len(self.client_calls.get(client, set())) == 0:
                    recipients.add(client)
        else:
            # Normal forwarding: send to:
            # 1. Clients that have joined this call
            # 2. Clients subscribed to this call who haven't joined any OTHER call
            # 3. Clients not subscribed to any call (for discovery)
            recipients = self.call_connections.get(call_id, set()).copy()

            # Add subscribed clients who haven't joined a different call
            for client in self.call_subscriptions.get(call_id, set()):
                # Include if not in any call OR only in this call
                if len(self.client_calls.get(client, set())) == 0:
                    recipients.add(client)

            # Add clients not subscribed to ANY call (new clients - for discovery)
            for client in self.all_clients:
                if len(self.client_calls.get(client, set())) == 0:
                    # Check if not subscribed to any call
                    not_subscribed = True
                    for subs in self.call_subscriptions.values():
                        if client in subs:
                            not_subscribed = False
                            break
                    if not_subscribed:
                        recipients.add(client)

        # Don't echo back to sender
        recipients_except_sender = recipients - {sender}

        if not recipients_except_sender:
            logger.debug(f"No recipients for call {call_id} (excluding sender)")
            return

        # For join_call broadcasts: subscribe recipients to this call
        # This marks them as "interested" in the call for future message forwarding
        if broadcast_join:
            for recipient in recipients_except_sender:
                self.call_subscriptions[call_id].add(recipient)

        # Send to all recipients
        sender_id = id(sender)
        logger.debug(f"Forwarding message from {sender_id} to {len(recipients_except_sender)} recipient(s)")

        # Send concurrently to all recipients
        tasks = [
            self.send_message(recipient, message)
            for recipient in recipients_except_sender
        ]

        results = await asyncio.gather(*tasks, return_exceptions=True)

        # Log any send failures
        for recipient, result in zip(recipients_except_sender, results):
            if isinstance(result, Exception):
                logger.warning(f"Failed to forward message to client {id(recipient)}: {result}")

    async def send_message(self, websocket: WebSocketServerProtocol, message: str):
        """
        Send a message to a client.

        Args:
            websocket: WebSocket connection
            message: Message to send (JSON string)
        """
        try:
            await websocket.send(message)
        except websockets.exceptions.ConnectionClosed:
            logger.debug(f"Cannot send to client {id(websocket)}: connection closed")
            # Will be cleaned up in handle_client finally block
        except Exception as e:
            logger.error(f"Error sending message to client {id(websocket)}: {e}")
            raise

    async def cleanup_client(self, websocket: WebSocketServerProtocol):
        """
        Clean up a disconnected client.

        Removes client from all call groups.

        Args:
            websocket: WebSocket connection to clean up
        """
        client_id = id(websocket)

        # Get all calls this client was in
        calls = self.client_calls.get(websocket, set()).copy()

        # Remove from all calls
        for call_id in calls:
            if call_id in self.call_connections:
                self.call_connections[call_id].discard(websocket)

                # Clean up empty call groups
                if not self.call_connections[call_id]:
                    del self.call_connections[call_id]
                    logger.info(f"Call {call_id} has no more participants, removing")

        # Remove from all subscription lists
        for call_id, subscribers in list(self.call_subscriptions.items()):
            subscribers.discard(websocket)
            if not subscribers:
                del self.call_subscriptions[call_id]

        # Remove client tracking
        if websocket in self.client_calls:
            del self.client_calls[websocket]

        # Remove from all_clients set
        self.all_clients.discard(websocket)

        logger.debug(f"Cleaned up client {client_id} from {len(calls)} call(s)")

    async def start(self):
        """
        Start the WebSocket server.

        This method blocks until the server is stopped.
        """
        logger.info(f"Starting signaling server on {self.host}:{self.port}")

        async with websockets.serve(
            self.handle_client,
            self.host,
            self.port,
            # Configuration
            ping_interval=30,  # Send ping every 30s
            ping_timeout=10,   # Wait 10s for pong
            close_timeout=5,   # Wait 5s when closing
        ):
            logger.info(f"✓ Signaling server listening on ws://{self.host}:{self.port}")
            logger.info("Press Ctrl+C to stop")

            # Run forever
            await asyncio.Future()  # Never completes


async def main():
    """Main entry point for signaling server."""
    import argparse

    parser = argparse.ArgumentParser(description="WebRTC Signaling Server for Teams Media Bot")
    parser.add_argument("--host", default="0.0.0.0", help="Host address to bind to (default: 0.0.0.0)")
    parser.add_argument("--port", type=int, default=8765, help="Port to listen on (default: 8765)")
    parser.add_argument("--log-level", default="INFO", choices=["DEBUG", "INFO", "WARNING", "ERROR"],
                        help="Logging level (default: INFO)")

    args = parser.parse_args()

    # Set log level
    logging.getLogger().setLevel(getattr(logging, args.log_level))

    # Create and start server
    server = SignalingServer(host=args.host, port=args.port)

    try:
        await server.start()
    except KeyboardInterrupt:
        logger.info("Shutting down signaling server...")
    except Exception as e:
        logger.error(f"Server error: {e}", exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nServer stopped")
