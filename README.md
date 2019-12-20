# AtemUtils

This is a collection of tools to aid in reverse engineering the atem protocol for the LibAtem project, using the LibAtem library to do so.

## AtemProxy

This is a simple ATEM connection proxy server, that decodes commands to make it easy to watch what a client is sending to the ATEM.

Any command passing through this proxy is logged to the console, in both JSON and byte form. With grep, this can be filtered to watch for specific commands. This is essentially an alternative to using wireshark with the plugin.

Note: A connection to the ATEM is opened for each client connected to this proxy. It is essentially a plain UDP connection forwarder with some extra parsing on the side

## AtemMock

This is a simple fake ATEM, that can be used for reverse engineering.

At startup it reads in a wireshark capture of a connection being initialised, and it replays that back to any clients that connect.

There is a function that allows for mangling of this data, which can be useful to check what a certain byte of a command is.
Once a client is connected, the connection will be maintained, but any commands received will be ignored.

### Related Projects
* ATEM Client Library [LibAtem](https://github.com/LibAtem/LibAtem)

### Credits
This builds on the work done by the following projects:
* http://skaarhoj.com/fileadmin/BMDPROTOCOL.html
* https://github.com/peschuster/wireshark-atem-dissector

### License

LibAtem is distributed under the GNU Lesser General Public License LGPLv3 or higher, see the file LICENSE for details.


