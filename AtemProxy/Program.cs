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

namespace AtemProxy
{
    public class ProxyServer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ProxyServer));

        private Socket _socket;
        private EndPoint _serverEndpoint;
        
        private UdpClient _client;
        private IPEndPoint _clientEndpoint;

        private AtemClient _atemConn;

        public ProxyServer(string address)
        {
            _atemConn = new AtemClient(address);
            
            StartReceivingFromAtem(address);
            StartReceivingFromClient();
        }

        private static Socket CreateSocket()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 9910);
            serverSocket.Bind(ipEndPoint);

            return serverSocket;
        }

        private bool ShouldBlockCommand(ICommand cmd)
        {
            // Note: this can use the regular AtemClient to send random commands.
            if (cmd is ProgramInputSetCommand pisCmd)
            {
                pisCmd.Source += 1;
                _atemConn.SendCommand(pisCmd);
                return true;
            }

            return false;
        }

        private void StartReceivingFromClient()
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
                        
                        if (_serverEndpoint == default(EndPoint))
                            _serverEndpoint = v.RemoteEndPoint;
                        
                        Log.InfoFormat("Got message from client: {0}", v.RemoteEndPoint);

                        if (_serverEndpoint.ToString() != v.RemoteEndPoint.ToString())
                        {
                            Log.InfoFormat("Got connection attempt from new client");
                            continue;
                        }
                        
                        Log.InfoFormat("Got message from client. {0} bytes", v.ReceivedBytes);

                        var resBuff = buff.ToArray();
                        var resSize = v.ReceivedBytes;
                        
                        var packet = new ReceivedPacket(resBuff);
                        if (packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.AckRequest) &&
                            !packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.Handshake))
                        {
                            // Handle this further
                            var allowedCommands = new List<ParsedCommand>();
                            bool changedCommands = false;
                            foreach (var rawCmd in packet.Commands)
                            {
                                var cmd = CommandParser.Parse(rawCmd);
                                if (cmd == null || !ShouldBlockCommand(cmd))
                                {
                                    allowedCommands.Add(rawCmd);
                                }
                                else
                                {
                                    changedCommands = true;
                                }
                            }

                            // 0 length commands are allowed, so don't bother checking for empty

                            if (changedCommands)
                            {
                                resBuff = CompileMessage(packet, allowedCommands);
                                resSize = resBuff.Length;
                            }
                        }
                        
                        
                        try
                        {
                            //_client.Client.SendTo(buff.Array, SocketFlags.None, _clientEndpoint);
                            _client.Send(resBuff, resSize, _clientEndpoint);
                        }
                        catch (ObjectDisposedException)
                        {
                            Log.ErrorFormat("{0} - Discarding message due to socket being disposed", _clientEndpoint);
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
        
        private byte[] CompileMessage(ReceivedPacket origPacket, List<ParsedCommand> commands)
        {
            byte[] payload = new byte[0];
            commands.ForEach(c =>
            {
                var b = new CommandBuilder(c.Name);
                b.AddByte(c.Body);
                payload = payload.Concat(b.ToByteArray()).ToArray();
            });
            
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
            _clientEndpoint = new IPEndPoint(IPAddress.Parse(address), 9910);
            _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        IPEndPoint ep = _clientEndpoint;
                        byte[] data = _client.Receive(ref ep);
                        
                        Log.InfoFormat("Got message from atem. {0} bytes", data.Length);
                        
                        try
                        {
                            // This can send everything back, as we only want to be able to intercept commands sent to the atem
                            _socket.SendTo(data, SocketFlags.None, _serverEndpoint);
                        }
                        catch (ObjectDisposedException)
                        {
                            Log.ErrorFormat("{0} - Discarding message due to socket being disposed", _serverEndpoint);
                        }
                    }
                    catch (SocketException)
                    {
                        Log.ErrorFormat("Socket Exception");
                    }
                }
            });
            thread.Name = "LibAtem.Receive";
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
            //var client = new AtemClient(config.AtemAddress);

            var server = new ProxyServer(config.AtemAddress);
            
            
            ConsoleKeyInfo key;
            Console.WriteLine("Press escape to exit");
            while ((key = Console.ReadKey()).Key != ConsoleKey.Escape)
            {
                /*
                if (config.MixEffect != null)
                {
                    foreach (KeyValuePair<MixEffectBlockId, Config.MixEffectConfig> me in config.MixEffect)
                    {
                        if (me.Value.Program != null && me.Value.Program.TryGetValue(key.KeyChar, out VideoSource src))
                            client.SendCommand(new ProgramInputSetCommand {Index = me.Key, Source = src});

                        if (me.Value.Preview != null && me.Value.Preview.TryGetValue(key.KeyChar, out src))
                            client.SendCommand(new PreviewInputSetCommand {Index = me.Key, Source = src});

                        if (me.Value.Cut == key.KeyChar)
                            client.SendCommand(new MixEffectCutCommand {Index = me.Key});
                        if (me.Value.Auto == key.KeyChar)
                            client.SendCommand(new MixEffectAutoCommand {Index = me.Key});
                    }
                }

                if (config.Auxiliary != null)
                {
                    foreach (KeyValuePair<AuxiliaryId, Dictionary<char, VideoSource>> aux in config.Auxiliary)
                    {
                        if (aux.Value == null)
                            continue;

                        if (aux.Value.TryGetValue(key.KeyChar, out VideoSource src))
                            client.SendCommand(new AuxSourceSetCommand {Id = aux.Key, Source = src});
                    }
                }

                if (config.SuperSource != null)
                {
                    foreach (KeyValuePair<SuperSourceBoxId, Dictionary<char, VideoSource>> box in config.SuperSource)
                    {
                        if (box.Value == null)
                            continue;

                        if (box.Value.TryGetValue(key.KeyChar, out VideoSource src))
                            client.SendCommand(new SuperSourceBoxSetCommand {Mask = SuperSourceBoxSetCommand.MaskFlags.Source, Index = box.Key, Source = src});
                    }
                }

*/
            }
        }
    }
}
