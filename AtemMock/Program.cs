using System;
using System.Collections.Generic;
using System.Linq;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Net;
using Newtonsoft.Json;
using PcapngFile;

namespace AtemMock
{
    class Program
    {
        static void Main(string[] args)
        {
            /*
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
            if (!logRepository.Configured) // Default to all on the console
                BasicConfigurator.Configure(logRepository);
            */
            
            var version = ProtocolVersion.V8_0_1;
            var initPackets = ParseCommands(version, "atem-constellation-8.0.2.pcapng");
            Console.WriteLine("Loaded {0} packets", initPackets.Count);

            var moddedPackets = initPackets.Select(pkt =>
            {
                return pkt.Commands.SelectMany(cmd =>
                {
                    var builder = new CommandBuilder(cmd.Name);
                    builder.AddByte(cmd.Body);

                    // TODO - do any mods here
                    if (cmd.Name == "_top")
                    {
                        // builder.SetByte(0, new byte[] { 0x01 }); // ME
                        // builder.SetByte(2, new byte[] { 0x02 }); // DSK
                        //builder.SetByte(19, new byte[] { 0x00 });
                        //builder.SetByte(18, new byte[] { 0x00 });
                        //builder.SetByte(4, new byte[] { 0x9 });
                        //builder.SetByte(9, new byte[] { 0x00 });
                        //builder.SetByte(17, new byte[] { 0x00 }); // Camera control
                        //builder.SetByte(21, new byte[] { 0x00 });
                        //builder.SetByte(20, new byte[] { 0x00 });
                        
                        var cmd2 = CommandParser.Parse(version, cmd);
                        
                        Console.WriteLine("TopologyCommand: {0}", JsonConvert.SerializeObject(cmd2));
                    } else if (cmd.Name == "_MvC")
                    {
                        //builder.SetByte(2, new byte[] { 0x00 });
                        
                        var cmd2 = CommandParser.Parse(version, cmd);
                        
                        Console.WriteLine("MultiViwers: {0}", JsonConvert.SerializeObject(cmd2));
                    } else if (cmd.Name == "TDvP")
                    {
                        Console.WriteLine("TDvP: {0}", JsonConvert.SerializeObject(cmd));

                    }

                    return builder.ToByteArray();
                }).ToArray();
            }).ToList();

            var server = new AtemServer(moddedPackets);
            server.StartReceive();
            server.StartPingTimer();
            
            Console.WriteLine("Press any key to terminate...");
            Console.ReadKey(); // Pause until keypress
        }
        
        
        private static List<ReceivedPacket> ParseCommands(ProtocolVersion version, string filename)
        {
            var res = new List<ReceivedPacket>();

            using (var reader = new Reader(filename))
            {
                foreach (var readBlock in reader.EnhancedPacketBlocks)
                {
                    var pkt = ParseEnhancedBlock(version, readBlock as EnhancedPacketBlock);
                    if (pkt != null)
                    {
                        res.Add(pkt);
                        if (pkt.Commands.Any(cmd => cmd.Name == "InCm"))
                        {
                            // Init complete
                            break;
                        }

                    }
                }
            }

            return res;
        }

        private static ReceivedPacket ParseEnhancedBlock(ProtocolVersion version, EnhancedPacketBlock block)
        {
            byte[] data = block.Data;

            // Perform some basic checks, to ensure data looks like it could be ATEM
            if (data[23] != 17)
                throw new ArgumentOutOfRangeException("Found packet that appears to not be UDP");
            if ((data[36] << 8) + data[37] != 9910 && (data[34] << 8) + data[35] != 9910)
                throw new ArgumentOutOfRangeException("Found packet that has wrong UDP port");

            data = data.Skip(42).ToArray();
            var packet = new ReceivedPacket(data);
            if (!packet.CommandCode.HasFlag(ReceivedPacket.CommandCodeFlags.AckRequest))
                return null;

            return packet;
        }

    }
}