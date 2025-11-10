// <copyright file="WebRTCManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Graph.Communications.Common.Telemetry;
    using Newtonsoft.Json;
    using Sample.PolicyRecordingBot.FrontEnd.Signaling;
    using SIPSorcery.Net;
    using SIPSorceryMedia.Abstractions;

    /// <summary>
    /// Manages WebRTC peer connections with GStreamer services.
    /// Handles SDP negotiation, ICE candidates, and RTP audio streaming.
    /// </summary>
    public class WebRTCManager : IWebRTCManager, IDisposable
    {
        private readonly IGraphLogger logger;
        private readonly ISignalingClient signalingClient;
        private readonly string turnServerUrl;
        private readonly string turnUsername;
        private readonly string turnPassword;
        private readonly ConcurrentDictionary<string, RTCPeerConnection> peerConnections;
        private readonly SemaphoreSlim connectionLock;
        private readonly CancellationTokenSource cancellationTokenSource;

        private string currentCallId;
        private RTCPeerConnection currentPeerConnection;
        private MediaStreamTrack audioTrack;
        private int reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 3;
        private bool disposed = false;

        /// <summary>
        /// Event raised when audio is received from WebRTC peer.
        /// </summary>
        public event Action<byte[], uint> OnAudioReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebRTCManager"/> class.
        /// </summary>
        /// <param name="logger">Graph logger for telemetry.</param>
        /// <param name="signalingClient">Signaling client for SDP/ICE exchange.</param>
        /// <param name="turnServerUrl">TURN server URL (e.g., turn:server.com:3478).</param>
        /// <param name="turnUsername">TURN server username.</param>
        /// <param name="turnPassword">TURN server password.</param>
        public WebRTCManager(
            IGraphLogger logger,
            ISignalingClient signalingClient,
            string turnServerUrl,
            string turnUsername,
            string turnPassword)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.signalingClient = signalingClient ?? throw new ArgumentNullException(nameof(signalingClient));
            this.turnServerUrl = turnServerUrl;
            this.turnUsername = turnUsername;
            this.turnPassword = turnPassword;

            this.peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();
            this.connectionLock = new SemaphoreSlim(1, 1);
            this.cancellationTokenSource = new CancellationTokenSource();

            // Wire up signaling events
            this.signalingClient.OnAnswerReceived += this.OnAnswerReceivedAsync;
            this.signalingClient.OnIceCandidateReceived += this.OnRemoteIceCandidateReceivedAsync;

            this.logger.Info($"WebRTCManager initialized with TURN server: {turnServerUrl}");
        }

        /// <summary>
        /// Creates a new WebRTC peer connection for a call.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <returns>True if connection was created successfully.</returns>
        public async Task<bool> CreateConnectionAsync(string callId)
        {
            if (string.IsNullOrEmpty(callId))
            {
                throw new ArgumentException("Call ID cannot be null or empty", nameof(callId));
            }

            await this.connectionLock.WaitAsync();

            try
            {
                this.logger.Info($"Creating WebRTC connection for call {callId}");

                // Configure ICE servers
                var configuration = new RTCConfiguration
                {
                    iceServers = new List<RTCIceServer>
                    {
                        new RTCIceServer
                        {
                            urls = this.turnServerUrl,
                            username = this.turnUsername,
                            credential = this.turnPassword,
                            credentialType = RTCIceCredentialType.password,
                        },
                    },
                };

                // Create peer connection
                var pc = new RTCPeerConnection(configuration);

                // Wire up event handlers
                pc.onicecandidate += (candidate) => this.OnIceCandidate(callId, candidate);
                pc.onconnectionstatechange += (state) => this.OnConnectionStateChange(callId, state);
                pc.oniceconnectionstatechange += (state) => this.OnIceConnectionStateChange(callId, state);

                // Add audio track (recvonly for receiving from GStreamer)
                var audioTrackConfig = new AudioTrackConfig
                {
                    Codecs = new List<AudioCodecsEnum> { AudioCodecsEnum.OPUS },
                };

                MediaStreamTrack track = new MediaStreamTrack(
                    SDPMediaTypesEnum.audio,
                    false,
                    new List<SDPAudioVideoMediaFormat>
                    {
                        new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.OPUS),
                    },
                    MediaStreamStatusEnum.SendRecv);

                pc.addTrack(track);

                // Wire up RTP packet receive handler
                pc.OnRtpPacketReceived += (rep, media, rtpPacket) => this.OnRtpPacketReceived(rtpPacket);

                // Store connection
                this.peerConnections[callId] = pc;
                this.currentCallId = callId;
                this.currentPeerConnection = pc;
                this.audioTrack = track;

                // Create and set local description (offer)
                var offer = pc.createOffer(null);
                await pc.setLocalDescription(offer);

                this.logger.Info($"WebRTC connection created for call {callId}, SDP offer ready");
                this.logger.Verbose($"SDP Offer: {offer.sdp}");

                // Send offer via SignalingClient
                await this.signalingClient.SendOfferAsync(callId, offer.sdp);

                this.reconnectAttempts = 0;
                return true;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"Failed to create WebRTC connection for call {callId}");
                return false;
            }
            finally
            {
                this.connectionLock.Release();
            }
        }

        /// <summary>
        /// Sends audio data via WebRTC (as RTP packets).
        /// </summary>
        /// <param name="opusData">Opus-encoded audio data.</param>
        /// <returns>Task representing the async operation.</returns>
        public async Task SendAudioAsync(byte[] opusData)
        {
            if (opusData == null || opusData.Length == 0)
            {
                throw new ArgumentException("Opus data cannot be null or empty", nameof(opusData));
            }

            if (this.currentPeerConnection == null)
            {
                this.logger.Warn("Cannot send audio: No active peer connection");
                return;
            }

            var connectionState = this.currentPeerConnection.connectionState;
            if (connectionState != RTCPeerConnectionState.connected)
            {
                this.logger.Warn($"Cannot send audio: Connection state is {connectionState}");
                return;
            }

            try
            {
                // Send RTP packet with Opus payload
                // SIPSorcery will handle RTP packetization
                // The audio track's RTP sender will send the packet
                if (this.audioTrack != null)
                {
                    // Generate timestamp (increments by sample count for 20ms @ 48kHz = 960 samples)
                    uint timestamp = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 48);

                    await this.currentPeerConnection.SendAudio(
                        (uint)opusData.Length,
                        opusData);

                    this.logger.Verbose($"Sent {opusData.Length} bytes of Opus audio via RTP");
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to send audio via WebRTC");
            }
        }

        /// <summary>
        /// Handles remote SDP answer from signaling server.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <param name="answerSdp">The SDP answer string.</param>
        /// <returns>Task representing the async operation.</returns>
        public async Task OnAnswerReceivedAsync(string callId, string answerSdp)
        {
            if (!this.peerConnections.TryGetValue(callId, out var pc))
            {
                this.logger.Warn($"Received answer for unknown call {callId}");
                return;
            }

            try
            {
                this.logger.Info($"Setting remote description (answer) for call {callId}");
                this.logger.Verbose($"SDP Answer: {answerSdp}");

                var answer = new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = answerSdp,
                };

                await pc.setRemoteDescription(answer);
                this.logger.Info($"Remote description set successfully for call {callId}");
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"Failed to set remote description for call {callId}");
            }
        }

        /// <summary>
        /// Handles remote ICE candidate from signaling server.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <param name="candidateJson">The ICE candidate as JSON string.</param>
        /// <returns>Task representing the async operation.</returns>
        private async Task OnRemoteIceCandidateReceivedAsync(string callId, string candidateJson)
        {
            if (!this.peerConnections.TryGetValue(callId, out var pc))
            {
                this.logger.Warn($"Received ICE candidate for unknown call {callId}");
                return;
            }

            try
            {
                this.logger.Verbose($"Adding ICE candidate for call {callId}");

                // Parse candidate JSON
                var candidateObj = JsonConvert.DeserializeObject<dynamic>(candidateJson);
                var candidateInit = new RTCIceCandidateInit
                {
                    candidate = candidateObj.candidate,
                    sdpMid = candidateObj.sdpMid,
                    sdpMLineIndex = (ushort)(candidateObj.sdpMLineIndex ?? 0),
                };

                await pc.addIceCandidate(candidateInit);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"Failed to add ICE candidate for call {callId}");
            }
        }

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        /// <returns>Connection state string (e.g., "connected", "failed").</returns>
        public string GetConnectionState()
        {
            if (this.currentPeerConnection == null)
            {
                return "closed";
            }

            return this.currentPeerConnection.connectionState.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Gets the number of active connections.
        /// </summary>
        /// <returns>Active connection count.</returns>
        public int GetActiveConnectionCount()
        {
            return this.peerConnections.Count(kvp =>
                kvp.Value.connectionState == RTCPeerConnectionState.connected);
        }

        /// <summary>
        /// Handles ICE candidate generation.
        /// </summary>
        private void OnIceCandidate(string callId, RTCIceCandidate candidate)
        {
            if (candidate != null)
            {
                this.logger.Verbose($"ICE candidate generated for call {callId}: {candidate.candidate}");

                // Send candidate via SignalingClient
                var candidateInit = new
                {
                    candidate = candidate.candidate,
                    sdpMid = candidate.sdpMid,
                    sdpMLineIndex = candidate.sdpMLineIndex,
                };
                string candidateJson = JsonConvert.SerializeObject(candidateInit);
                Task.Run(async () => await this.signalingClient.SendIceCandidateAsync(callId, candidateJson));
            }
            else
            {
                this.logger.Info($"ICE candidate gathering complete for call {callId}");
            }
        }

        /// <summary>
        /// Handles connection state changes.
        /// </summary>
        private void OnConnectionStateChange(string callId, RTCPeerConnectionState state)
        {
            this.logger.Info($"Connection state changed for call {callId}: {state}");

            if (state == RTCPeerConnectionState.failed && this.reconnectAttempts < MaxReconnectAttempts)
            {
                this.logger.Warn($"Connection failed, attempting reconnect ({this.reconnectAttempts + 1}/{MaxReconnectAttempts})");
                Task.Run(() => this.AttemptReconnectAsync(callId));
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                this.logger.Info($"Connection closed for call {callId}, cleaning up");
                this.CleanupConnection(callId);
            }
        }

        /// <summary>
        /// Handles ICE connection state changes.
        /// </summary>
        private void OnIceConnectionStateChange(string callId, RTCIceConnectionState state)
        {
            this.logger.Info($"ICE connection state changed for call {callId}: {state}");
        }

        /// <summary>
        /// Handles incoming RTP packets (audio from GStreamer).
        /// </summary>
        private void OnRtpPacketReceived(RTPPacket rtpPacket)
        {
            try
            {
                // Extract Opus payload from RTP packet
                byte[] opusData = rtpPacket.Payload;
                uint timestamp = rtpPacket.Header.Timestamp;

                this.logger.Verbose($"Received RTP packet: {opusData.Length} bytes, timestamp={timestamp}");

                // Raise event for MediaBridge to handle
                this.OnAudioReceived?.Invoke(opusData, timestamp);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error processing received RTP packet");
            }
        }

        /// <summary>
        /// Attempts to reconnect after connection failure.
        /// </summary>
        private async Task AttemptReconnectAsync(string callId)
        {
            this.reconnectAttempts++;

            // Exponential backoff: 2s, 4s, 8s
            int delayMs = (int)Math.Pow(2, this.reconnectAttempts) * 1000;
            this.logger.Info($"Waiting {delayMs}ms before reconnect attempt");

            await Task.Delay(delayMs, this.cancellationTokenSource.Token);

            if (!this.cancellationTokenSource.Token.IsCancellationRequested)
            {
                // Close old connection
                this.CleanupConnection(callId);

                // Create new connection
                await this.CreateConnectionAsync(callId);
            }
        }

        /// <summary>
        /// Cleans up a peer connection.
        /// </summary>
        private void CleanupConnection(string callId)
        {
            if (this.peerConnections.TryRemove(callId, out var pc))
            {
                try
                {
                    pc.close();
                    pc.Dispose();
                    this.logger.Info($"Cleaned up connection for call {callId}");
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, $"Error cleaning up connection for call {callId}");
                }
            }

            if (this.currentCallId == callId)
            {
                this.currentCallId = null;
                this.currentPeerConnection = null;
                this.audioTrack = null;
            }
        }

        /// <summary>
        /// Disposes the WebRTC manager and all connections.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.logger.Info("Disposing WebRTCManager");

            // Cancel any pending operations
            this.cancellationTokenSource.Cancel();

            // Close all connections
            foreach (var kvp in this.peerConnections)
            {
                this.CleanupConnection(kvp.Key);
            }

            this.peerConnections.Clear();

            this.connectionLock?.Dispose();
            this.cancellationTokenSource?.Dispose();

            this.disposed = true;
        }
    }
}
