using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Complete;
using RtmpSharp.Hosting;

namespace RtmpSharp.Net
{
    public class Supervisor
    {
        class SessionState
        {
            public CancellationTokenSource CancellationTokenSource;
            public DateTime LastPing;
            public IStreamSession Session;
        }

        private Thread thread = null;
        private readonly RtmpServer server = null;
        private CancellationToken cancellationToken = default;
        private TimeSpan pingInterval = default;
        private TimeSpan responseThreshole = default;
        private Dictionary<ushort, SessionState> sessionStates = new Dictionary<ushort, SessionState>();
        public Supervisor(RtmpServer server)
        {
            this.server = server;
        }
        public void StartAsync(TimeSpan pingInterval, TimeSpan responseThreshole, CancellationToken ct = default)
        {
            if (thread != null)
            {
                throw new InvalidOperationException("already started");
            }
            cancellationToken = ct;
            this.pingInterval = pingInterval;
            this.responseThreshole = responseThreshole;
            thread = new Thread(ThreadEntry);
            thread.Start();
        }
        private void ThreadEntry()
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var kv in server.connectedSessions)
                    {
                        if (!sessionStates.TryGetValue(kv.Key, out var sessionState))
                        {
                            sessionState = new SessionState()
                            {
                                LastPing = DateTime.Now,
                                Session = kv.Value
                            };
                            sessionStates.Add(kv.Key, sessionState);
                        }
                        var session = kv.Value;
                        if (DateTime.Now - sessionState.LastPing >= pingInterval && sessionState.CancellationTokenSource == null)
                        {
                            sessionState.CancellationTokenSource = new CancellationTokenSource();
                            sessionState.CancellationTokenSource.CancelAfter((int)responseThreshole.TotalMilliseconds);
                            var pingTask = session.PingAsync(sessionState.CancellationTokenSource.Token);
                            pingTask.ContinueWith(tsk =>
                            {
                                sessionState.CancellationTokenSource.Dispose();
                                sessionState.CancellationTokenSource = null;
                            }, TaskContinuationOptions.OnlyOnRanToCompletion);
                            pingTask.ContinueWith(tsk =>
                            {
                                sessionState.Session.Disconnect(new ExceptionalEventArgs("pingpong timeout"));
                                sessionState.CancellationTokenSource.Dispose();
                                sessionState.CancellationTokenSource = null;
                            }, TaskContinuationOptions.OnlyOnCanceled);
                        }
                    }
                    Thread.Sleep(1);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch
            {

            }
        }
    }
}