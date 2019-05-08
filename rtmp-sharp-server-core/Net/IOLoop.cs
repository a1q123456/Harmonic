using System;
using System.Collections.Generic;
using System.Text;

namespace RtmpSharp.Net
{
    public class IOLoop
    {
        static IOLoop()
        {
            Instance = new IOLoop();
        }

        private IOLoop()
        { }

        public static IOLoop Instance { get; }

        public bool Started { get; private set; } = false;

        private void StartIOLoop()
        {
            try
            {
                while (Started)
                {
                    foreach (var current in connects)
                    {
                        ct.ThrowIfCancellationRequested();
                        StreamConnectState state = current.Value;
                        ushort client_id = current.Key;
                        IStreamSession connect = state.Connect;
                        if (connect.IsDisconnected)
                        {
                            CloseClient(client_id);
                            continue;
                        }
                        try
                        {
                            if (state.WriterTask == null || state.WriterTask.IsCompleted)
                            {
                                state.WriterTask = connect.WriteOnceAsync(ct);
                            }
                            if (state.WriterTask.IsCanceled || state.WriterTask.IsFaulted)
                            {
                                throw state.WriterTask.Exception;
                            }
                            if (state.LastPing == null || DateTime.UtcNow - state.LastPing >= new TimeSpan(0, 0, PingPeriod))
                            {
                                connect.PingAsync(PingTimeout);
                                state.LastPing = DateTime.UtcNow;
                            }


                            if (state.ReaderTask == null || state.ReaderTask.IsCompleted)
                            {
                                state.ReaderTask = connect.ReadOnceAsync(ct);
                            }
                            if (state.ReaderTask.IsCanceled || state.ReaderTask.IsFaulted)
                            {
                                throw state.ReaderTask.Exception;
                            }

                        }
                        catch
                        {
                            CloseClient(client_id);
                            continue;
                        }
                    }
                    var prepare_add_length = prepare_to_add.Count;
                    if (prepare_add_length != 0)
                    {
                        for (int i = 0; i < prepare_add_length; i++)
                        {
                            var current = prepare_to_add[0];
                            connects.Add(current.Key, current.Value);
                            prepare_to_add.RemoveAt(0);
                        }
                    }

                    var prepare_remove_length = prepare_to_remove.Count;
                    if (prepare_remove_length != 0)
                    {
                        for (int i = 0; i < prepare_remove_length; i++)
                        {
                            var current = prepare_to_remove[0];
                            connects.TryGetValue(current, out var connection);
                            connects.Remove(current);
                            prepare_to_remove.RemoveAt(0);

                        }
                    }
                }
            }
            catch
            {

            }
        }
    }
}
