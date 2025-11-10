// <copyright file="SignalingClient.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Signaling
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Graph.Communications.Common.Telemetry;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// WebSocket-based signaling client for WebRTC negotiation.
    /// Connects to Python signaling server to exchange SDP and ICE.
    /// </summary>
    public class SignalingClient : ISignalingClient
    {
        private readonly IGraphLogger logger;
        private readonly string signalingUrl;
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private Task receiveTask;
        private readonly SemaphoreSlim sendLock;
        private int reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 5;
        private bool disposed = false;
        private bool intentionalDisconnect = false;

        /// <summary>
        /// Event raised when an SDP answer is received from the remote peer.
        /// </summary>
        public event Func<string, string, Task> OnAnswerReceived;

        /// <summary>
        /// Event raised when an ICE candidate is received from the remote peer.
        /// </summary>
        public event Func<string, string, Task> OnIceCandidateReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="SignalingClient"/> class.
        /// </summary>
        /// <param name="logger">Graph logger for telemetry.</param>
        /// <param name="signalingUrl">WebSocket URL for signaling server (e.g., ws://localhost:8765).</param>
        public SignalingClient(IGraphLogger logger, string signalingUrl)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.signalingUrl = signalingUrl ?? throw new ArgumentNullException(nameof(signalingUrl));

            this.webSocket = new ClientWebSocket();
            this.cancellationTokenSource = new CancellationTokenSource();
            this.sendLock = new SemaphoreSlim(1, 1);

            this.logger.Info($"SignalingClient initialized with URL: {signalingUrl}");
        }

        /// <summary>
        /// Gets a value indicating whether the client is currently connected.
        /// </summary>
        public bool IsConnected =>
            this.webSocket != null &&
            this.webSocket.State == WebSocketState.Open;

        /// <summary>
        /// Connects to the signaling server.
        /// </summary>
        /// <returns>Task representing the async operation.</returns>
        public async Task ConnectAsync()
        {
            if (this.IsConnected)
            {
                this.logger.Warn("Already connected to signaling server");
                return;
            }

            try
            {
                this.logger.Info($"Connecting to signaling server at {this.signalingUrl}");

                this.intentionalDisconnect = false;
                this.webSocket = new ClientWebSocket();
                this.cancellationTokenSource = new CancellationTokenSource();

                await this.webSocket.ConnectAsync(new Uri(this.signalingUrl), this.cancellationTokenSource.Token);

                this.logger.Info("✓ Connected to signaling server");

                // Start receive loop
                this.receiveTask = Task.Run(() => this.ReceiveLoopAsync());

                this.reconnectAttempts = 0;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to connect to signaling server");
                throw;
            }
        }

        /// <summary>
        /// Disconnects from the signaling server.
        /// </summary>
        /// <returns>Task representing the async operation.</returns>
        public async Task DisconnectAsync()
        {
            this.intentionalDisconnect = true;

            if (this.webSocket == null || this.webSocket.State != WebSocketState.Open)
            {
                this.logger.Warn("Not connected to signaling server");
                return;
            }

            try
            {
                this.logger.Info("Disconnecting from signaling server");

                this.cancellationTokenSource?.Cancel();

                if (this.webSocket.State == WebSocketState.Open)
                {
                    await this.webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client disconnecting",
                        CancellationToken.None);
                }

                if (this.receiveTask != null)
                {
                    await this.receiveTask;
                }

                this.logger.Info("Disconnected from signaling server");
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error during disconnect");
            }
        }

        /// <summary>
        /// Sends a join_call message to join a call.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <returns>Task representing the async operation.</returns>
        public async Task SendJoinCallAsync(string callId)
        {
            if (string.IsNullOrEmpty(callId))
            {
                throw new ArgumentException("Call ID cannot be null or empty", nameof(callId));
            }

            var message = new
            {
                type = "join_call",
                callId = callId,
                botId = Environment.MachineName,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            await this.SendMessageAsync(message);
            this.logger.Info($"Sent join_call for call {callId}");
        }

        /// <summary>
        /// Sends an SDP offer to the remote peer.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <param name="sdp">The SDP offer string.</param>
        /// <returns>Task representing the async operation.</returns>
        public async Task SendOfferAsync(string callId, string sdp)
        {
            if (string.IsNullOrEmpty(callId))
            {
                throw new ArgumentException("Call ID cannot be null or empty", nameof(callId));
            }

            if (string.IsNullOrEmpty(sdp))
            {
                throw new ArgumentException("SDP cannot be null or empty", nameof(sdp));
            }

            var message = new
            {
                type = "offer",
                callId = callId,
                sdp = sdp,
            };

            await this.SendMessageAsync(message);
            this.logger.Info($"Sent SDP offer for call {callId}");
            this.logger.Verbose($"SDP Offer: {sdp}");
        }

        /// <summary>
        /// Sends an ICE candidate to the remote peer.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <param name="candidateJson">The ICE candidate as JSON string.</param>
        /// <returns>Task representing the async operation.</returns>
        public async Task SendIceCandidateAsync(string callId, string candidateJson)
        {
            if (string.IsNullOrEmpty(callId))
            {
                throw new ArgumentException("Call ID cannot be null or empty", nameof(callId));
            }

            if (string.IsNullOrEmpty(candidateJson))
            {
                throw new ArgumentException("Candidate JSON cannot be null or empty", nameof(candidateJson));
            }

            // Parse candidate JSON to embed it in the message
            JObject candidateObj = JObject.Parse(candidateJson);

            var message = new
            {
                type = "ice_candidate",
                callId = callId,
                candidate = candidateObj,
            };

            await this.SendMessageAsync(message);
            this.logger.Verbose($"Sent ICE candidate for call {callId}");
        }

        /// <summary>
        /// Sends a JSON message via WebSocket.
        /// </summary>
        private async Task SendMessageAsync(object message)
        {
            if (!this.IsConnected)
            {
                throw new InvalidOperationException("Not connected to signaling server");
            }

            await this.sendLock.WaitAsync();

            try
            {
                string json = JsonConvert.SerializeObject(message);
                byte[] buffer = Encoding.UTF8.GetBytes(json);

                await this.webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken: this.cancellationTokenSource.Token);

                this.logger.Verbose($"→ Sent: {json}");
            }
            finally
            {
                this.sendLock.Release();
            }
        }

        /// <summary>
        /// Receive loop that processes incoming messages.
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];

            try
            {
                while (!this.cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result;

                    try
                    {
                        result = await this.webSocket.ReceiveAsync(segment, this.cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        this.logger.Info("Receive loop cancelled");
                        break;
                    }
                    catch (WebSocketException ex)
                    {
                        this.logger.Error(ex, "WebSocket error during receive");
                        await this.HandleConnectionLossAsync();
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        this.logger.Info($"WebSocket closed by server: {result.CloseStatus} - {result.CloseStatusDescription}");
                        await this.HandleConnectionLossAsync();
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        this.logger.Verbose($"← Received: {message}");

                        await this.ProcessMessageAsync(message);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Unexpected error in receive loop");
                await this.HandleConnectionLossAsync();
            }
        }

        /// <summary>
        /// Processes an incoming message and dispatches events.
        /// </summary>
        private async Task ProcessMessageAsync(string messageJson)
        {
            try
            {
                var message = JObject.Parse(messageJson);
                string messageType = message["type"]?.ToString();
                string callId = message["callId"]?.ToString();

                if (string.IsNullOrEmpty(messageType))
                {
                    this.logger.Warn("Received message without 'type' field");
                    return;
                }

                switch (messageType)
                {
                    case "answer":
                        await this.HandleAnswerMessageAsync(callId, message);
                        break;

                    case "ice_candidate":
                        await this.HandleIceCandidateMessageAsync(callId, message);
                        break;

                    case "join_call":
                        // Ignore join_call echoes (we don't need to handle our own joins)
                        this.logger.Verbose($"Received join_call for call {callId}");
                        break;

                    default:
                        this.logger.Verbose($"Received unhandled message type: {messageType}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                this.logger.Error(ex, $"Failed to parse message JSON: {messageJson}");
                // Don't throw - continue processing other messages
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error processing message");
                // Don't throw - continue processing other messages
            }
        }

        /// <summary>
        /// Handles an SDP answer message.
        /// </summary>
        private async Task HandleAnswerMessageAsync(string callId, JObject message)
        {
            string sdp = message["sdp"]?.ToString();

            if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(sdp))
            {
                this.logger.Warn("Received invalid answer message (missing callId or sdp)");
                return;
            }

            this.logger.Info($"Received SDP answer for call {callId}");
            this.logger.Verbose($"SDP Answer: {sdp}");

            // Raise event
            if (this.OnAnswerReceived != null)
            {
                await this.OnAnswerReceived(callId, sdp);
            }
        }

        /// <summary>
        /// Handles an ICE candidate message.
        /// </summary>
        private async Task HandleIceCandidateMessageAsync(string callId, JObject message)
        {
            var candidate = message["candidate"];

            if (string.IsNullOrEmpty(callId) || candidate == null)
            {
                this.logger.Warn("Received invalid ICE candidate message");
                return;
            }

            string candidateJson = candidate.ToString(Formatting.None);

            this.logger.Verbose($"Received ICE candidate for call {callId}");

            // Raise event
            if (this.OnIceCandidateReceived != null)
            {
                await this.OnIceCandidateReceived(callId, candidateJson);
            }
        }

        /// <summary>
        /// Handles connection loss and attempts reconnection.
        /// </summary>
        private async Task HandleConnectionLossAsync()
        {
            if (this.intentionalDisconnect || this.disposed)
            {
                this.logger.Info("Connection closed intentionally, not reconnecting");
                return;
            }

            if (this.reconnectAttempts >= MaxReconnectAttempts)
            {
                this.logger.Error($"Max reconnection attempts ({MaxReconnectAttempts}) reached, giving up");
                return;
            }

            this.reconnectAttempts++;

            // Exponential backoff: 2s, 4s, 8s, 16s, 32s
            int delayMs = (int)Math.Pow(2, this.reconnectAttempts) * 1000;
            this.logger.Warn($"Connection lost, attempting reconnect #{this.reconnectAttempts} in {delayMs}ms");

            await Task.Delay(delayMs);

            try
            {
                // Close old WebSocket
                if (this.webSocket != null)
                {
                    this.webSocket.Dispose();
                }

                // Reconnect
                await this.ConnectAsync();

                this.logger.Info("✓ Reconnected successfully");
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"Reconnection attempt #{this.reconnectAttempts} failed");

                // Try again if we haven't hit the limit
                if (this.reconnectAttempts < MaxReconnectAttempts)
                {
                    await this.HandleConnectionLossAsync();
                }
            }
        }

        /// <summary>
        /// Disposes the signaling client and closes the connection.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.logger.Info("Disposing SignalingClient");

            this.disposed = true;
            this.intentionalDisconnect = true;

            // Disconnect synchronously (blocking)
            this.DisconnectAsync().Wait(TimeSpan.FromSeconds(5));

            this.webSocket?.Dispose();
            this.cancellationTokenSource?.Dispose();
            this.sendLock?.Dispose();
        }
    }
}
