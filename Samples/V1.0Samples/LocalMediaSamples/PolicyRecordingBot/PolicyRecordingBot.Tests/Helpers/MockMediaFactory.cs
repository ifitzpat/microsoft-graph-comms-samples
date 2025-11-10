// <copyright file="MockMediaFactory.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.Tests.Helpers
{
    using System;
    using System.Runtime.InteropServices;
    using Microsoft.Skype.Bots.Media;

    /// <summary>
    /// Factory for creating mock media buffers and events for testing.
    /// </summary>
    public static class MockMediaFactory
    {
        /// <summary>
        /// Creates a mock audio buffer with test data.
        /// </summary>
        /// <param name="length">Buffer length in bytes (default 640 for 20ms @ 16kHz).</param>
        /// <param name="timestamp">Buffer timestamp.</param>
        /// <returns>Mock AudioMediaBuffer.</returns>
        public static AudioMediaBuffer CreateAudioBuffer(uint length = 640, long timestamp = 0)
        {
            // Allocate unmanaged memory to simulate real media buffer
            IntPtr unmanagedBuffer = Marshal.AllocHGlobal((int)length);

            // Fill with test pattern (sine wave at 440Hz for example)
            byte[] testData = GenerateTestAudioData((int)length);
            Marshal.Copy(testData, 0, unmanagedBuffer, (int)length);

            // Note: In a real scenario, you'd use the actual AudioMediaBuffer from the SDK
            // For now, this is a placeholder. You'll need to adjust based on actual SDK types.
            return new AudioMediaBuffer
            {
                Data = unmanagedBuffer,
                Length = length,
                Timestamp = timestamp,
            };
        }

        /// <summary>
        /// Creates a mock AudioMediaReceivedEventArgs for testing.
        /// </summary>
        /// <param name="length">Buffer length.</param>
        /// <param name="timestamp">Timestamp.</param>
        /// <returns>Event args with mock buffer.</returns>
        public static AudioMediaReceivedEventArgs CreateAudioReceivedEventArgs(
            uint length = 640,
            long timestamp = 0)
        {
            var buffer = CreateAudioBuffer(length, timestamp);

            // Note: Adjust this based on actual SDK constructor
            // This is a placeholder for the pattern
            throw new NotImplementedException(
                "This method needs to be implemented based on actual SDK types. " +
                "See BotMediaStreamTests.cs for usage examples with Moq instead.");
        }

        /// <summary>
        /// Frees mock audio buffer memory.
        /// </summary>
        /// <param name="buffer">Buffer to free.</param>
        public static void FreeAudioBuffer(AudioMediaBuffer buffer)
        {
            if (buffer?.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer.Data);
            }
        }

        /// <summary>
        /// Generates test audio data (PCM 16kHz, 16-bit mono).
        /// Creates a simple sine wave for testing.
        /// </summary>
        /// <param name="length">Length in bytes.</param>
        /// <param name="frequency">Frequency in Hz (default 440Hz - A note).</param>
        /// <returns>PCM audio data.</returns>
        public static byte[] GenerateTestAudioData(int length, int frequency = 440)
        {
            byte[] data = new byte[length];
            int sampleRate = 16000; // 16kHz
            int numSamples = length / 2; // 16-bit = 2 bytes per sample

            for (int i = 0; i < numSamples; i++)
            {
                // Generate sine wave: sin(2π * frequency * time)
                double time = i / (double)sampleRate;
                double sample = Math.Sin(2 * Math.PI * frequency * time);

                // Convert to 16-bit PCM (-32768 to 32767)
                short pcmSample = (short)(sample * 32767);

                // Write as little-endian bytes
                data[i * 2] = (byte)(pcmSample & 0xFF);
                data[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
            }

            return data;
        }

        /// <summary>
        /// Analyzes audio data to detect primary frequency.
        /// Useful for verifying test audio integrity.
        /// </summary>
        /// <param name="audioData">PCM audio data.</param>
        /// <param name="sampleRate">Sample rate (default 16kHz).</param>
        /// <returns>Detected frequency in Hz.</returns>
        public static double DetectFrequency(byte[] audioData, int sampleRate = 16000)
        {
            // Simple zero-crossing rate detection
            // For production, use FFT for accurate frequency detection
            int zeroCrossings = 0;
            short previousSample = 0;

            for (int i = 0; i < audioData.Length; i += 2)
            {
                if (i + 1 >= audioData.Length)
                {
                    break;
                }

                short sample = (short)(audioData[i] | (audioData[i + 1] << 8));

                if ((previousSample >= 0 && sample < 0) || (previousSample < 0 && sample >= 0))
                {
                    zeroCrossings++;
                }

                previousSample = sample;
            }

            // Frequency ≈ (zero crossings / 2) / duration
            double duration = (audioData.Length / 2.0) / sampleRate;
            double frequency = (zeroCrossings / 2.0) / duration;

            return frequency;
        }
    }
}
