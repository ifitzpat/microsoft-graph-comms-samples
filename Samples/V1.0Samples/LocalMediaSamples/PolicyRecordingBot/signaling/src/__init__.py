"""
WebSocket Signaling Server for Teams Media Bot.

Forwards WebRTC signaling messages (SDP offers/answers, ICE candidates)
between Teams bot and GStreamer services.

Features:
- Multi-tenant: Messages isolated by callId
- Connection management: Tracks clients per call
- Error handling: Gracefully handles invalid JSON and disconnections
- Logging: Structured logging for debugging

Usage:
    python -m src.signaling_server
    python -m src.signaling_server --port 8765 --host 0.0.0.0
"""
