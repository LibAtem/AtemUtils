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
                            var cmd = CommandParser.Parse(rawCmd);
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
