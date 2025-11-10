// <copyright file="IWebRTCManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Interface for WebRTC manager that handles peer connections with GStreamer services.
    /// </summary>
    public interface IWebRTCManager
    {
        /// <summary>
        /// Event raised when audio is received from WebRTC peer.
        /// </summary>
        event Action<byte[], uint> OnAudioReceived;

        /// <summary>
        /// Creates a new WebRTC peer connection for a call.
        /// </summary>
        /// <param name="callId">The call identifier.</param>
        /// <returns>True if connection was created successfully.</returns>
        Task<bool> CreateConnectionAsync(string callId);

        /// <summary>
        /// Sends audio data via WebRTC (as RTP packets).
        /// </summary>
        /// <param name="opusData">Opus-encoded audio data.</param>
        /// <returns>Task representing the async operation.</returns>
        Task SendAudioAsync(byte[] opusData);

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        /// <returns>Connection state string (e.g., "connected", "failed").</returns>
        string GetConnectionState();

        /// <summary>
        /// Gets the number of active connections.
        /// </summary>
        /// <returns>Active connection count.</returns>
        int GetActiveConnectionCount();
    }
}
