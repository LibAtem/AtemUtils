# AtemProxy

This is a simple ATEM connection proxy server, that decodes commands to make it easy to watch what a client is sending to the ATEM.

Any command passing through this proxy is logged to the console, in both JSON and byte form. With grep, this can be filtered to watch for specific commands. This is essentially an alternative to using wireshark with the plugin.

Note: A connection to the ATEM is opened for each client connected to this proxy. It is essentially a plain UDP connection forwarder with some extra parsing on the side

### Related Projects
* ATEM Client Library [LibAtem](https://github.com/LibAtem/LibAtem)
* ATEM Client XML Parsing [LibAtem.XmlState](https://github.com/LibAtem/LibAtem.XmlState)
* ATEM Client profile helper [LibAtem.DeviceProfile](https://github.com/LibAtem/LibAtem.DeviceProfile)
* ATEM Client comparison tests [LibAtem.ComparisonTests](https://github.com/LibAtem/LibAtem.ComparisonTests)

### Credits
This builds on the work done by the following projects:
* http://skaarhoj.com/fileadmin/BMDPROTOCOL.html
* https://github.com/peschuster/wireshark-atem-dissector

### License

LibAtem is distributed under the GNU Lesser General Public License LGPLv3 or higher, see the file LICENSE for details.


