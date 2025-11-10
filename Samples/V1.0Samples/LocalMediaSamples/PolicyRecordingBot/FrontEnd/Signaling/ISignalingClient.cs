// <copyright file="ISignalingClient.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Signaling
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Interface for WebSocket-based signaling client.
    /// Handles WebRTC signaling (SDP and ICE) with GStreamer services.
    /// </summary>
    public interface ISignalingClient : IDisposable
    {
        /// <summary>
        /// Event raised when an SDP answer is received from the remote peer.
        /// </summary>
        event Func<string, string, Task> OnAnswerReceived;

        /// <summary>
        /// Event raised when an ICE candidate is received from the remote peer.
        /// </summary>
        event Func<string, string, Task> OnIceCandidateReceived;

        /// <summary>
        /// Gets a value indicating whether the client is currently connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects to the signaling server.
        /// </summary>
        /// <returns>Task representing the async operation.</returns>
        Task ConnectAsync();

        /// <summary>
        /// Disconnects from the signaling server.
        /// </summary>
        /// <returns>Task representing the async operation.</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Sends a join_call message to join a call.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <returns>Task representing the async operation.</returns>
        Task SendJoinCallAsync(string callId);

        /// <summary>
        /// Sends an SDP offer to the remote peer.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <param name="sdp">The SDP offer string.</param>
        /// <returns>Task representing the async operation.</returns>
        Task SendOfferAsync(string callId, string sdp);

        /// <summary>
        /// Sends an ICE candidate to the remote peer.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <param name="candidateJson">The ICE candidate as JSON string.</param>
        /// <returns>Task representing the async operation.</returns>
        Task SendIceCandidateAsync(string callId, string candidateJson);
    }
}
