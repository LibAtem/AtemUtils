using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using log4net;
using log4net.Config;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Commands.MixEffects;
using LibAtem.Net;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using LibAtem.Commands.DeviceProfile;

namespace AtemProxy
{ 
    public class ProxyConnection
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ProxyConnection));

        private readonly ConcurrentQueue<LogItem> _logQueue;

        private readonly Socket _serverSocket;
        private readonly EndPoint _clientEndPoint;

        public IPEndPoint AtemEndpoint { get; }
        public UdpClient AtemConnection { get; }

        public ProxyConnection(ConcurrentQueue<LogItem> logQueue, string address, Socket serverSocket, EndPoint clientEndpoint)
        {
            _logQueue = logQueue;
            _serverSocket = serverSocket;
            _clientEndPoint = clientEndpoint;

            AtemEndpoint = new IPEndPoint(IPAddress.Parse(address), 9910);
            AtemConnection = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

            StartReceivingFromAtem(address);
        }
        
        private bool MutateServerCommand(ICommand cmd)
        {
            /*
            if (cmd is MultiviewPropertiesGetCommand mvpCmd)
            {
                // mvpCmd.SafeAreaEnabled = false;
                return true;
            }
            if (cmd is MultiviewerConfigCommand mvcCmd)
            {
                // mvcCmd.Count = 1;
                // mvcCmd.WindowCount = 9; // < 10 works, no effect?
                // mvcCmd.Tmp2 = 0;
                // mvcCmd.CanRouteInputs = false; // Confirmed
                // mvcCmd.CanToggleSafeArea = 0; // Breals
                // mvcCmd.SupportsVuMeters = 0; // 
                return true;
            }
            else if (cmd is MixEffectBlockConfigCommand meCmd)
            {
                meCmd.KeyCount = 1;
                return true;
            }
            else if (cmd is TopologyV8Command top8Cmd)
            {
                // topCmd.SuperSource = 2; // Breaks
                // topCmd.TalkbackOutputs = 8;
                // topCmd.SerialPort = 0; // < 1 Works
                // topCmd.DVE = 2; // > 1 Works
                top8Cmd.MediaPlayers = 1; // < 2 Works
                // topCmd.Stingers = 0; // < 1 Works
                top8Cmd.DownstreamKeyers = 1; // < 1 Works
                // topCmd.Auxiliaries = 4; // Breaks
                // topCmd.HyperDecks = 2; // Works
                // topCmd.TalkbackOverSDI = 4; //
                // top8Cmd.Tmp11 = 2; // < 1 breaks. > 1 is ok
                // top8Cmd.Tmp12 = 0; // All work
                // topCmd.Tmp14 = 1; // Breaks
                // top8Cmd.Tmp20 = 1;

                Console.WriteLine("{0}", JsonConvert.SerializeObject(top8Cmd));
                return true;
            }
            else if (cmd is TopologyCommand topCmd)
            {
                // topCmd.SuperSource = 2; // Breaks
                // topCmd.TalkbackOutputs = 8;
                // topCmd.SerialPort = 0; // < 1 Works
                // topCmd.DVE = 2; // > 1 Works
                // topCmd.MediaPlayers = 1; // < 2 Works
                // topCmd.Stingers = 0; // < 1 Works
                topCmd.DownstreamKeyers = 1; // < 1 Works
                // topCmd.Auxiliaries = 4; // Breaks
                // topCmd.HyperDecks = 2; // Works
                // topCmd.TalkbackOverSDI = 4; //
                // topCmd.Tmp11 = 2; // < 1 breaks. > 1 is ok
                // topCmd.Tmp12 = 0; // All work
                // topCmd.Tmp14 = 1; // Breaks
                // topCmd.Tmp20 = 0;
                return true;
            }*/
            return false;
        }

        private byte[] ParsedCommandToBytes(ParsedCommandSpec cmd)
        {
            var build = new CommandBuilder(cmd.Name);
            build.AddByte(cmd.Body);
            return build.ToByteArray();
        }
        
        private byte[] CompileMessage(ReceivedPacket origPacket, byte[] payload)
        {
            byte opcode = (byte)origPacket.CommandCode;
            byte len1 = (byte)((ReceivedPacket.HeaderLength + payload.Length) / 256 | opcode << 3); // opcode 0x08 + length
            byte len2 = (byte)((ReceivedPacket.HeaderLength + payload.Length) % 256);

            byte[] buffer =
            {
                len1, len2, // Opcode & Length
                (byte)(origPacket.SessionId / 256),  (byte)(origPacket.SessionId % 256), // session id
                0x00, 0x00, // ACKed Pkt Id
                0x00, 0x00, // Unknown
                0x00, 0x00, // unknown2
                (byte)(origPacket.PacketId / 256),  (byte)(origPacket.PacketId % 256), // pkt id
            };

            // If no payload, dont append it
            if (payload.Length == 0)
                return buffer;

            return buffer.Concat(payload).ToArray();
        }

        private void StartReceivingFromAtem(string address)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        IPEndPoint ep = AtemEndpoint;
                        byte[] data = AtemConnection.Receive(ref ep);

                        //Log.InfoFormat("Got message from atem. {0} bytes", data.Length);
                        
                        var packet = new ReceivedPacket(data);
                        if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.AckRequest) &&
                            !packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                        {
                            // Handle this further
                            var newPayload = new byte[0];
                            bool changed = false;
                            foreach (var rawCmd in packet.Commands)
                            {
                                var cmd = CommandParser.Parse(ProxyServer.Version, rawCmd);
                                if (cmd != null)
                                {
                                    if (cmd is VersionCommand vcmd)
                                    {
                                        ProxyServer.Version = vcmd.ProtocolVersion;
                                    }

                                    var name = CommandManager.FindNameAndVersionForType(cmd);
                                    // Log.InfoFormat("Recv {0} {1}", name.Item1, JsonConvert.SerializeObject(cmd));

                                    if (MutateServerCommand(cmd))
                                    {
                                        changed = true;
                                        newPayload = newPayload.Concat(cmd.ToByteArray()).ToArray();
                                    }
                                    else
                                    {
                                        newPayload = newPayload.Concat(ParsedCommandToBytes(rawCmd)).ToArray();
                                    }

                                }
                                else
                                {
                                    newPayload = newPayload.Concat(ParsedCommandToBytes(rawCmd)).ToArray();
                                }
                            }

                            if (changed)
                            {
                                data = CompileMessage(packet, newPayload);
                            }
                        }

                        _logQueue.Enqueue(new LogItem()
                        {
                            IsSend = false,
                            Payload = data
                        });

                        try
                        {
                            _serverSocket.SendTo(data, SocketFlags.None, _clientEndPoint);
                        }
                        catch (ObjectDisposedException)
                        {
                            Log.ErrorFormat("{0} - Discarding message due to socket being disposed", _clientEndPoint);
                        }
                    }
                    catch (SocketException)
                    {
                        Log.ErrorFormat("Socket Exception");
                    }
                }
            });
            thread.Start();
        }
    }

    public class LogItem
    {
        public bool IsSend { get; set; }
        public byte[] Payload { get; set; }
    }

    public class ProxyServer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ProxyServer));

        private Socket _socket;

        private Dictionary<string, ProxyConnection> _clients = new Dictionary<string, ProxyConnection>();
        public static ProtocolVersion Version = ProtocolVersion.Minimum;

        private ConcurrentQueue<LogItem> _logQueue = new ConcurrentQueue<LogItem>();

        public ProxyServer(string address)
        {
            // TODO - need to clean out stale clients

            StartLogWriter();
            StartReceivingFromClients(address);
        }

        private void StartLogWriter()
        {
            var thread = new Thread(() =>
            {
                while(true)
                {
                    if (!_logQueue.TryDequeue(out LogItem item))
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    var packet = new ReceivedPacket(item.Payload);
                    if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.AckRequest) &&
                        !packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                    {
                        string dirStr = item.IsSend ? "Send" : "Recv";
                        // Handle this further
                        foreach (var rawCmd in packet.Commands)
                        {
                            var cmd = CommandParser.Parse(Version, rawCmd);
                            if (cmd != null)
                            {
                                Log.InfoFormat("{0} {1} {2} ({3})", dirStr, rawCmd.Name, JsonConvert.SerializeObject(cmd), BitConverter.ToString(rawCmd.Body));
                            } else
                            {
                                Log.InfoFormat("{0} unknown {1} {2}", dirStr, rawCmd.Name, BitConverter.ToString(rawCmd.Body));
                            }
                        }
                    }
                }
            });
            thread.Start();
        }

        private static Socket CreateSocket()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 9910);
            serverSocket.Bind(ipEndPoint);

            return serverSocket;
        }

        private void StartReceivingFromClients(string address)
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

                        string epStr = v.RemoteEndPoint.ToString();
                        if (!_clients.TryGetValue(epStr, out ProxyConnection client))
                        {
                            Log.InfoFormat("Got connection from new client: {0}", epStr);
                            client = new ProxyConnection(_logQueue, address, _socket, v.RemoteEndPoint);
                            _clients.Add(epStr, client);
                        }
                        
                        //Log.InfoFormat("Got message from client. {0} bytes", v.ReceivedBytes);

                        var resBuff = buff.ToArray();
                        var resSize = v.ReceivedBytes;

                        _logQueue.Enqueue(new LogItem()
                        {
                            IsSend = true,
                            Payload = resBuff
                        });
                        
                        try
                        {
                            client.AtemConnection.Send(resBuff, resSize, client.AtemEndpoint);
                        }
                        catch (ObjectDisposedException)
                        {
                            Log.ErrorFormat("{0} - Discarding message due to socket being disposed", client.AtemEndpoint);
                        }
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
    }
    
    public class Program
    {
        public static void Main(string[] args)
        {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            var log = LogManager.GetLogger(typeof(Program));
            log.Info("Starting");

            Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));

            var server = new ProxyServer(config.AtemAddress);
        }
    }
}
