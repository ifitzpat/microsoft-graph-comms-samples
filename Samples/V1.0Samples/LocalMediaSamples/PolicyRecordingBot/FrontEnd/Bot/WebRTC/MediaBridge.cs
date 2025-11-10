// <copyright file="MediaBridge.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.FrontEnd.Bot.WebRTC
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Graph.Communications.Common;
    using Microsoft.Graph.Communications.Common.Telemetry;

    /// <summary>
    /// Bridges audio between Teams media streams and WebRTC peer connections.
    /// Thread-safe, handles buffering and format conversion.
    /// </summary>
    public class MediaBridge : ObjectRootDisposable
    {
        /// <summary>
        /// Default maximum queue size (50 frames = ~1 second @ 20ms per frame).
        /// </summary>
        private const int DefaultMaxQueueSize = 50;

        private readonly IWebRTCManager webRtcManager;
        private readonly IAudioConverter audioConverter;

        // Bounded queues to prevent memory buildup
        private readonly BlockingCollection<AudioFrame> teamsToWebRtcQueue;
        private readonly BlockingCollection<AudioFrame> webRtcToTeamsQueue;

        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly Task teamsToWebRtcProcessingTask;
        private readonly Task webRtcToTeamsProcessingTask;

        private BotMediaStream botMediaStream;
        private int droppedFrameCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaBridge"/> class.
        /// </summary>
        /// <param name="logger">Graph logger.</param>
        /// <param name="webRtcManager">WebRTC manager instance.</param>
        /// <param name="audioConverter">Audio format converter (optional, will create default if null).</param>
        /// <param name="maxQueueSize">Maximum queue size (default 50).</param>
        public MediaBridge(
            IGraphLogger logger,
            IWebRTCManager webRtcManager,
            IAudioConverter audioConverter = null,
            int maxQueueSize = DefaultMaxQueueSize)
            : base(logger)
        {
            ArgumentVerifier.ThrowOnNullArgument(webRtcManager, nameof(webRtcManager));

            this.webRtcManager = webRtcManager;
            this.audioConverter = audioConverter ?? new AudioConverter(logger);

            // Initialize bounded queues
            this.teamsToWebRtcQueue = new BlockingCollection<AudioFrame>(
                new ConcurrentQueue<AudioFrame>(),
                maxQueueSize);

            this.webRtcToTeamsQueue = new BlockingCollection<AudioFrame>(
                new ConcurrentQueue<AudioFrame>(),
                maxQueueSize);

            this.cancellationTokenSource = new CancellationTokenSource();

            // Start processing tasks
            this.teamsToWebRtcProcessingTask = Task.Run(
                () => this.ProcessTeamsToWebRtcAsync(this.cancellationTokenSource.Token));

            this.webRtcToTeamsProcessingTask = Task.Run(
                () => this.ProcessWebRtcToTeamsAsync(this.cancellationTokenSource.Token));

            // Subscribe to WebRTC audio received events
            this.webRtcManager.OnAudioReceived += this.OnWebRtcAudioReceived;

            this.GraphLogger.Info("MediaBridge initialized successfully");
        }

        /// <summary>
        /// Attaches the BotMediaStream for bidirectional audio flow.
        /// </summary>
        /// <param name="stream">The BotMediaStream instance.</param>
        public void AttachBotMediaStream(BotMediaStream stream)
        {
            ArgumentVerifier.ThrowOnNullArgument(stream, nameof(stream));

            this.botMediaStream = stream;
            this.GraphLogger.Info("BotMediaStream attached to MediaBridge");
        }

        /// <summary>
        /// Sends audio from Teams to WebRTC.
        /// Called by BotMediaStream when audio is received from Teams.
        /// </summary>
        /// <param name="pcmData">PCM audio data (16-bit, 16kHz, mono).</param>
        /// <param name="timestamp">Audio timestamp.</param>
        public void SendAudioToWebRTC(byte[] pcmData, long timestamp)
        {
            ArgumentVerifier.ThrowOnNullArgument(pcmData, nameof(pcmData));

            var frame = new AudioFrame
            {
                Data = pcmData,
                Timestamp = timestamp,
                Format = AudioFormat.PCM16kHz,
            };

            // Non-blocking add with timeout
            if (!this.teamsToWebRtcQueue.TryAdd(frame, TimeSpan.FromMilliseconds(10)))
            {
                Interlocked.Increment(ref this.droppedFrameCount);
                this.GraphLogger.Warn($"Teams->WebRTC queue full, dropping frame (total dropped: {this.droppedFrameCount})");

                // Alert if drop rate is high
                if (this.droppedFrameCount % 10 == 0)
                {
                    this.GraphLogger.Error($"High frame drop rate detected: {this.droppedFrameCount} frames dropped");
                }
            }
        }

        /// <summary>
        /// Gets the number of dropped frames (for monitoring).
        /// </summary>
        /// <returns>Dropped frame count.</returns>
        public int GetDroppedFrameCount()
        {
            return this.droppedFrameCount;
        }

        /// <summary>
        /// Gets the current queue sizes for monitoring.
        /// </summary>
        /// <returns>Tuple of (teamsToWebRtc, webRtcToTeams) queue counts.</returns>
        public (int TeamsToWebRtc, int WebRtcToTeams) GetQueueSizes()
        {
            return (this.teamsToWebRtcQueue.Count, this.webRtcToTeamsQueue.Count);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                this.GraphLogger.Info("Disposing MediaBridge...");

                // Signal cancellation and wait for tasks to complete
                this.cancellationTokenSource.Cancel();

                try
                {
                    Task.WaitAll(
                        new[] { this.teamsToWebRtcProcessingTask, this.webRtcToTeamsProcessingTask },
                        TimeSpan.FromSeconds(5));
                }
                catch (AggregateException ex)
                {
                    this.GraphLogger.Warn($"Exception while waiting for processing tasks to complete: {ex.Message}");
                }

                // Unsubscribe from events
                this.webRtcManager.OnAudioReceived -= this.OnWebRtcAudioReceived;

                // Dispose resources
                this.teamsToWebRtcQueue?.Dispose();
                this.webRtcToTeamsQueue?.Dispose();
                this.audioConverter?.Dispose();
                this.cancellationTokenSource?.Dispose();

                this.GraphLogger.Info("MediaBridge disposed successfully");
            }
        }

        /// <summary>
        /// Event handler for audio received from WebRTC.
        /// </summary>
        /// <param name="opusData">Opus-encoded audio data.</param>
        /// <param name="timestamp">RTP timestamp.</param>
        private void OnWebRtcAudioReceived(byte[] opusData, uint timestamp)
        {
            var frame = new AudioFrame
            {
                Data = opusData,
                Timestamp = timestamp,
                Format = AudioFormat.Opus,
            };

            if (!this.webRtcToTeamsQueue.TryAdd(frame, TimeSpan.FromMilliseconds(10)))
            {
                this.GraphLogger.Warn("WebRTC->Teams queue full, dropping frame");
            }
        }

        /// <summary>
        /// Processing loop: Teams PCM → Opus → WebRTC.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the async operation.</returns>
        private async Task ProcessTeamsToWebRtcAsync(CancellationToken cancellationToken)
        {
            this.GraphLogger.Info("MediaBridge: Teams->WebRTC processing started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    AudioFrame frame = null;

                    try
                    {
                        // Take with cancellation support
                        frame = this.teamsToWebRtcQueue.Take(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        // Convert PCM to Opus
                        byte[] opusData = this.audioConverter.ConvertPcmToOpus(frame.Data);

                        // Send via WebRTC
                        await this.webRtcManager.SendAudioAsync(opusData).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        this.GraphLogger.Error(ex, "Error processing Teams->WebRTC audio frame");
                    }
                }
            }
            catch (Exception ex)
            {
                this.GraphLogger.Error(ex, "Fatal error in Teams->WebRTC processing loop");
            }
            finally
            {
                this.GraphLogger.Info("MediaBridge: Teams->WebRTC processing stopped");
            }
        }

        /// <summary>
        /// Processing loop: WebRTC → Opus → PCM → Teams.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the async operation.</returns>
        private async Task ProcessWebRtcToTeamsAsync(CancellationToken cancellationToken)
        {
            this.GraphLogger.Info("MediaBridge: WebRTC->Teams processing started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    AudioFrame frame = null;

                    try
                    {
                        frame = this.webRtcToTeamsQueue.Take(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    try
                    {
                        // Convert Opus to PCM
                        byte[] pcmData = this.audioConverter.ConvertOpusToPcm(frame.Data);

                        // Send to Teams via BotMediaStream
                        if (this.botMediaStream != null)
                        {
                            // Note: This requires BotMediaStream to have SendAudioToTeams method
                            // which will be implemented when we modify BotMediaStream
                            await Task.Run(() => this.SendAudioToTeamsInternal(pcmData)).ConfigureAwait(false);
                        }
                        else
                        {
                            this.GraphLogger.Warn("BotMediaStream not attached, cannot send audio to Teams");
                        }
                    }
                    catch (Exception ex)
                    {
                        this.GraphLogger.Error(ex, "Error processing WebRTC->Teams audio frame");
                    }
                }
            }
            catch (Exception ex)
            {
                this.GraphLogger.Error(ex, "Fatal error in WebRTC->Teams processing loop");
            }
            finally
            {
                this.GraphLogger.Info("MediaBridge: WebRTC->Teams processing stopped");
            }
        }

        /// <summary>
        /// Internal method to send audio to Teams.
        /// This is a placeholder until BotMediaStream.SendAudioToTeams is implemented.
        /// </summary>
        /// <param name="pcmData">PCM audio data.</param>
        private void SendAudioToTeamsInternal(byte[] pcmData)
        {
            // TODO: Implement when BotMediaStream supports sending
            // For now, just log
            this.GraphLogger.Verbose($"Would send {pcmData.Length} bytes to Teams");
        }
    }
}
