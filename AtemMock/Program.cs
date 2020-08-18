using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibAtem;
using LibAtem.Commands;
using LibAtem.Commands.DeviceProfile;
using LibAtem.Net;
using LibAtem.Util;
using Newtonsoft.Json;

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

            //var initPackets = ParseCommands("mini-v8.1.data");
            //var initPackets = ParseCommands("tvshd-v8.1.0.data");
            //var initPackets = ParseCommands("tvshd-v8.2_new.data");
            //var initPackets = ParseCommands("2me4k-v8.0.1.data");
            //var initPackets = ParseCommands("mini-pro-v8.2.data");
            var initPackets = ParseCommands("Constellation-8.2.3.data");
            //var initPackets = ParseCommands("constellation-v8.0.2.data");
            //var initPackets = ParseCommands(version, "2me-v8.1.data");
            Console.WriteLine("Loaded {0} packets", initPackets.Count);

            ParsedCommandSpec rawNameCommand = initPackets.SelectMany(pkt => pkt.Where(cmd => cmd.Name == "_pin")).Single();
            ProductIdentifierCommand nameCommand = (ProductIdentifierCommand)CommandParser.Parse(ProtocolVersion.Minimum, rawNameCommand);

            ParsedCommandSpec rawVersionCommand = initPackets.SelectMany(pkt => pkt.Where(cmd => cmd.Name == "_ver")).Single();
            VersionCommand versionCommand = (VersionCommand)CommandParser.Parse(ProtocolVersion.Minimum, rawVersionCommand);
            ProtocolVersion version = versionCommand.ProtocolVersion;

            var moddedPackets = initPackets.Select(pkt =>
            {
                return pkt.SelectMany(cmd =>
                {
                    var builder = new CommandBuilder(cmd.Name);
                    builder.AddByte(cmd.Body);

                    if (cmd.Name == "FASP")
                    {
                        // TODO - Fairlight AFV hard cut vs transition when switching
                        //builder.SetByte(0, new byte[] { 0x01 });
                        //builder.SetByte(16, new byte[] { 0x10 }); // some kind of type column. This hides all the faders...
                        //builder.SetByte(19, new byte[] { 0x03 });
                        //builder.SetByte(18, new byte[] { 0x10 }); // Current delay
                        //builder.SetByte(24, new byte[] { 0x01 });
                    }
                    else

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
                        //builder.SetByte(6, new byte[] { 0x00 });
                        
                        var cmd2 = CommandParser.Parse(version, cmd);
                        
                        Console.WriteLine("TopologyCommand: {0}", JsonConvert.SerializeObject(cmd2));
                    } else if (cmd.Name == "_MvC")
                    {
                        //builder.SetByte(4, new byte[] { 0x01 });
                        
                        var cmd2 = CommandParser.Parse(version, cmd);
                        
                        Console.WriteLine("MultiViwers: {0}", JsonConvert.SerializeObject(cmd2));
                    } else if (cmd.Name == "TDvP")
                    {
                        Console.WriteLine("TDvP: {0}", JsonConvert.SerializeObject(cmd));

                    }
                    else if (cmd.Name == "AuxS")
                    {
                        //builder.SetByte(2, new byte[] { 0x00, 0x01 });
                        var cmd2 = CommandParser.Parse(version, cmd);
                        Console.WriteLine("AuxS: {0}", JsonConvert.SerializeObject(cmd2));

                    }

                    return builder.ToByteArray();
                }).ToArray();
            }).ToList();

            var server = new AtemServer(moddedPackets);
            server.StartReceive();
            server.StartPingTimer();

            var modelNameAndVersion =
                $"{nameCommand.Name} {versionCommand.ProtocolVersion.ToVersionString().Replace('.', '-')}";
            server.StartAnnounce(modelNameAndVersion, modelNameAndVersion.GetHashCode().ToString());


            Console.WriteLine("Press any key to terminate...");
            Console.ReadKey(); // Pause until keypress
        }

        private static List<List<ParsedCommandSpec>> ParseCommands(string filename)
        {
            var res = new List<List<ParsedCommandSpec>>();

            using (var reader = new StreamReader(filename))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var commands = ReceivedPacket.ParseCommands(line.HexToByteArray());
                    res.Add(commands.ToList());
                }
            }

            return res;
        }
    }
}