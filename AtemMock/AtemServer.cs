using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using LibAtem.Commands;
using LibAtem.Net;

namespace AtemMock
{
    public class AtemServer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AtemServer));

        private readonly AtemConnectionList _connections;
        private readonly IReadOnlyList<byte[]> _state;

        private Socket _socket;

        // TODO - remove this list, and replace with something more sensible...
        private readonly List<Timer> timers = new List<Timer>();

        public AtemServer(IReadOnlyList<byte[]> state)
        {
            _state = state;
            _connections = new AtemConnectionList();
        }

        public void StartPingTimer()
        {
            timers.Add(new Timer(o =>
            {
                _connections.QueuePings();
            }, null, 0, AtemConstants.PingInterval));
        }
        
        private static Socket CreateSocket()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 9910);
            serverSocket.Bind(ipEndPoint);

            return serverSocket;
        }

        public void StartReceive()
        {
            _socket = CreateSocket();
            
            var thread = new Thread(async () =>
            {
                while (true)
                {
                    try
                    {
                        //Start receiving data
                        ArraySegment<byte> buff = new ArraySegment<byte>(new byte[2500]);
                        var end = new IPEndPoint(IPAddress.Any, 0);
                        SocketReceiveFromResult v = await _socket.ReceiveFromAsync(buff, SocketFlags.None, end);

                        AtemServerConnection conn = _connections.FindOrCreateConnection(v.RemoteEndPoint, out _);
                        if (conn == null)
                            continue;

                        byte[] buffer = buff.Array;
                        var packet = new ReceivedPacket(buffer);

                        if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                        {
                            conn.ResetConnStatsInfo();
                            // send handshake back
                            byte[] test =
                            {
                                buffer[0], buffer[1], // flags + length
                                buffer[2], buffer[3], // session id
                                0x00, 0x00, // acked pkt id
                                0x00, 0x00, // retransmit request
                                buffer[8], buffer[9], // unknown2
                                0x00, 0x00, // server pkt id
                                0x02, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00
                            };

                            var sendThread = new Thread(o =>
                            {
                                while (!conn.HasTimedOut)
                                {
                                    conn.TrySendQueued(_socket);
                                    Task.Delay(3).Wait();
                                }
                            });
                            sendThread.Start();

                            await _socket.SendToAsync(new ArraySegment<byte>(test, 0, 20), SocketFlags.None, v.RemoteEndPoint);

                            continue;
                        }

                        if (!conn.IsOpened)
                        {
                            var recvThread = new Thread(o =>
                            {
                                while (!conn.HasTimedOut || conn.HasCommandsToProcess)
                                {
                                    List<ICommand> cmds = conn.GetNextCommands();

                                    Log.DebugFormat("Recieved {0} commands", cmds.Count);
                                    //conn.HandleInner(_state, connection, cmds);
                                }
                            });
                            recvThread.Start();
                        }
                        conn.Receive(_socket, packet);

                        if (conn.ReadyForData)
                            QueueDataDumps(conn);
                    }
                    catch (SocketException)
                    {
                        // Reinit the socket as it is now unavailable
                        //_socket = CreateSocket();
                    }
                }
            });
            thread.Start();
        }
        
        private void QueueDataDumps(AtemConnection conn)
        {
            try
            {

                foreach (byte[] cmd in _state)
                {
                    var builder = new OutboundMessageBuilder();
                    if (!builder.TryAddData(cmd))
                        throw new Exception("Failed to build message!");

                    conn.QueueMessage(builder.Create());
                }

                Log.InfoFormat("Sent all data to {0}", conn.Endpoint);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
