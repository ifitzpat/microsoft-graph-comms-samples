// <copyright file="AudioFrame.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    /// <summary>
    /// Represents an audio frame with metadata.
    /// </summary>
    public class AudioFrame
    {
        /// <summary>
        /// Gets or sets the audio data.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Gets or sets the timestamp.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the audio format.
        /// </summary>
        public AudioFormat Format { get; set; }
    }

    /// <summary>
    /// Audio format enumeration.
    /// </summary>
    public enum AudioFormat
    {
        /// <summary>
        /// PCM 16kHz, 16-bit, mono.
        /// </summary>
        PCM16kHz,

        /// <summary>
        /// Opus codec.
        /// </summary>
        Opus,
    }
}
