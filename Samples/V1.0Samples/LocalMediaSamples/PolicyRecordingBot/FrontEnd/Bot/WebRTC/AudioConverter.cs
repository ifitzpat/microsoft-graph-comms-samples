// <copyright file="AudioConverter.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;
    using Concentus.Enums;
    using Concentus.Structs;
    using Microsoft.Graph.Communications.Common;
    using Microsoft.Graph.Communications.Common.Telemetry;

    /// <summary>
    /// Converts audio between PCM 16kHz and Opus formats.
    /// Uses Concentus library for Opus encoding/decoding.
    /// </summary>
    public class AudioConverter : IAudioConverter
    {
        private const int SampleRate = 16000; // 16kHz
        private const int Channels = 1; // Mono
        private const int FrameSizeMs = 20; // 20ms frames
        private const int FrameSizeSamples = SampleRate * FrameSizeMs / 1000; // 320 samples
        private const int FrameSizeBytes = FrameSizeSamples * 2; // 640 bytes (16-bit samples)

        private readonly IGraphLogger logger;

        // Opus encoder/decoder using Concentus library
        private readonly OpusEncoder encoder;
        private readonly OpusDecoder decoder;
        private bool disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioConverter"/> class.
        /// </summary>
        /// <param name="logger">Graph logger.</param>
        public AudioConverter(IGraphLogger logger)
        {
            ArgumentVerifier.ThrowOnNullArgument(logger, nameof(logger));

            this.logger = logger;

            // Initialize Opus encoder/decoder
            // Using VOIP application for low latency and voice optimization
            this.encoder = OpusEncoder.Create(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            this.decoder = OpusDecoder.Create(SampleRate, Channels);

            // Configure encoder for optimal voice quality
            this.encoder.Bitrate = 24000; // 24 kbps - good quality for voice
            this.encoder.Complexity = 10; // Max complexity for best quality
            this.encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            this.encoder.ForceMode = OpusMode.MODE_SILK_ONLY; // SILK for voice

            this.logger.Info("AudioConverter initialized with Opus codec (16kHz, mono, 24kbps)");
        }

        /// <summary>
        /// Converts PCM 16kHz audio to Opus.
        /// </summary>
        /// <param name="pcmData">PCM audio data (16-bit, 16kHz, mono).</param>
        /// <returns>Opus-encoded audio data.</returns>
        public byte[] ConvertPcmToOpus(byte[] pcmData)
        {
            ArgumentVerifier.ThrowOnNullArgument(pcmData, nameof(pcmData));

            if (pcmData.Length != FrameSizeBytes)
            {
                this.logger.Warn($"PCM data size {pcmData.Length} bytes does not match expected {FrameSizeBytes} bytes for 20ms frame");
            }

            try
            {
                // Convert byte array to short array (16-bit PCM samples)
                short[] pcmSamples = new short[pcmData.Length / 2];
                Buffer.BlockCopy(pcmData, 0, pcmSamples, 0, pcmData.Length);

                // Encode to Opus
                byte[] opusData = new byte[4000]; // Max Opus frame size
                int encodedLength = this.encoder.Encode(pcmSamples, 0, FrameSizeSamples, opusData, 0, opusData.Length);

                if (encodedLength < 0)
                {
                    this.logger.Error($"Opus encoding failed with error code: {encodedLength}");
                    throw new InvalidOperationException($"Opus encoding failed with error code: {encodedLength}");
                }

                // Resize to actual encoded length
                byte[] result = new byte[encodedLength];
                Buffer.BlockCopy(opusData, 0, result, 0, encodedLength);

                this.logger.Verbose($"Encoded {pcmData.Length} bytes PCM to {result.Length} bytes Opus");
                return result;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error converting PCM to Opus");
                throw;
            }
        }

        /// <summary>
        /// Converts Opus audio to PCM 16kHz.
        /// </summary>
        /// <param name="opusData">Opus-encoded audio data.</param>
        /// <returns>PCM audio data (16-bit, 16kHz, mono).</returns>
        public byte[] ConvertOpusToPcm(byte[] opusData)
        {
            ArgumentVerifier.ThrowOnNullArgument(opusData, nameof(opusData));

            try
            {
                // Decode Opus frame
                short[] pcmSamples = new short[FrameSizeSamples * 2]; // Allow for FEC (Forward Error Correction)
                int decodedSamples = this.decoder.Decode(opusData, 0, opusData.Length, pcmSamples, 0, FrameSizeSamples, false);

                if (decodedSamples < 0)
                {
                    this.logger.Error($"Opus decoding failed with error code: {decodedSamples}");
                    throw new InvalidOperationException($"Opus decoding failed with error code: {decodedSamples}");
                }

                // Convert to byte array (16-bit PCM)
                byte[] pcmData = new byte[decodedSamples * 2];
                Buffer.BlockCopy(pcmSamples, 0, pcmData, 0, pcmData.Length);

                this.logger.Verbose($"Decoded {opusData.Length} bytes Opus to {pcmData.Length} bytes PCM");
                return pcmData;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error converting Opus to PCM");
                throw;
            }
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    // Note: OpusEncoder and OpusDecoder are structs in Concentus
                    // and don't implement IDisposable, so no cleanup needed
                    this.logger.Info("AudioConverter disposed");
                }

                this.disposed = true;
            }
        }
    }
}
