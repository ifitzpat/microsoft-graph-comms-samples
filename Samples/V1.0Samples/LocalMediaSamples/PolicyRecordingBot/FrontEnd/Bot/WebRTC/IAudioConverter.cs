// <copyright file="IAudioConverter.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;

    /// <summary>
    /// Interface for audio format conversion between PCM and Opus.
    /// </summary>
    public interface IAudioConverter : IDisposable
    {
        /// <summary>
        /// Converts PCM 16kHz audio to Opus.
        /// </summary>
        /// <param name="pcmData">PCM audio data (16-bit, 16kHz, mono).</param>
        /// <returns>Opus-encoded audio data.</returns>
        byte[] ConvertPcmToOpus(byte[] pcmData);

        /// <summary>
        /// Converts Opus audio to PCM 16kHz.
        /// </summary>
        /// <param name="opusData">Opus-encoded audio data.</param>
        /// <returns>PCM audio data (16-bit, 16kHz, mono).</returns>
        byte[] ConvertOpusToPcm(byte[] opusData);
    }
}
