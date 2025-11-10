// <copyright file="TestLogger.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

namespace Sample.PolicyRecordingBot.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Microsoft.Graph.Communications.Common;
    using Microsoft.Graph.Communications.Common.Telemetry;

    /// <summary>
    /// Test logger for unit tests.
    /// Captures log messages for assertion and debugging.
    /// </summary>
    public class TestLogger : IGraphLogger
    {
        private readonly List<LogEvent> logEvents = new List<LogEvent>();
        private readonly object lockObject = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="TestLogger"/> class.
        /// </summary>
        /// <param name="component">Component name.</param>
        /// <param name="minimumLevel">Minimum trace level.</param>
        public TestLogger(string component = "TestLogger", TraceLevel minimumLevel = TraceLevel.Verbose)
        {
            this.Component = component;
            this.DiagnosticLevel = minimumLevel;
        }

        /// <inheritdoc/>
        public string Component { get; }

        /// <inheritdoc/>
        public TraceLevel DiagnosticLevel { get; set; }

        /// <summary>
        /// Gets the captured log events.
        /// </summary>
        public IReadOnlyList<LogEvent> LogEvents
        {
            get
            {
                lock (this.lockObject)
                {
                    return this.logEvents.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public void Error(Exception exception, string message = null)
        {
            this.Log(TraceLevel.Error, message ?? exception?.Message, exception);
        }

        /// <inheritdoc/>
        public void Info(string message)
        {
            this.Log(TraceLevel.Info, message);
        }

        /// <inheritdoc/>
        public void Verbose(string message)
        {
            this.Log(TraceLevel.Verbose, message);
        }

        /// <inheritdoc/>
        public void Warn(string message)
        {
            this.Log(TraceLevel.Warning, message);
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<LogEvent> observer)
        {
            return new Observer<LogEvent>(
                this,
                onNext: observer.OnNext,
                onError: observer.OnError,
                onCompleted: observer.OnCompleted);
        }

        /// <summary>
        /// Clears all captured log events.
        /// </summary>
        public void Clear()
        {
            lock (this.lockObject)
            {
                this.logEvents.Clear();
            }
        }

        /// <summary>
        /// Gets log messages of a specific level.
        /// </summary>
        /// <param name="level">Trace level to filter.</param>
        /// <returns>Filtered log messages.</returns>
        public List<string> GetMessages(TraceLevel level)
        {
            var messages = new List<string>();
            lock (this.lockObject)
            {
                foreach (var evt in this.logEvents)
                {
                    if (evt.Level == level)
                    {
                        messages.Add(evt.Message);
                    }
                }
            }

            return messages;
        }

        /// <summary>
        /// Checks if any error was logged.
        /// </summary>
        /// <returns>True if errors exist.</returns>
        public bool HasErrors()
        {
            return this.GetMessages(TraceLevel.Error).Count > 0;
        }

        private void Log(TraceLevel level, string message, Exception exception = null)
        {
            if (level > this.DiagnosticLevel)
            {
                return;
            }

            var logEvent = new LogEvent
            {
                Level = level,
                Message = message,
                EventType = exception != null ? LogEventType.Exception : LogEventType.Trace,
                Exception = exception,
                Timestamp = DateTime.UtcNow,
            };

            lock (this.lockObject)
            {
                this.logEvents.Add(logEvent);
            }

            // Also output to test console for debugging
            Console.WriteLine($"[{level}] {message}");
            if (exception != null)
            {
                Console.WriteLine($"Exception: {exception}");
            }
        }
    }
}
