# WiFi Sitter
It's a babysitter for your WiFi NIC.  

This is born out of the need to have the wifi adapter disabled when Ethernet is
active, seems like this should be a solved problem but there are no good free
tools for this. Some PC vendors produce their own which provide this behavior
but they also try to be the one stop shop for all your WiFi management needs.
Windows does a fine time managing which hotspots you're connected to, this just
fills in one gap.

While running the application watches for IP changed and availability changed
events and enables/disables WiFi adapters accordingly. When Ethernet is active,
WiFi adapters are disabled if they've made a connection, if WiFi is active but
not connected it is left alone, when ethernet gets unplugged or the network
availability goes away all WiFi adapters are enabled. That is all it does,
there is no configuration, it ignores Bluetooth adapters and MS WiFi Direct
Virtual adapters. It compiles to a single .Net 4.0 executable and only makes
use of .Net and a few commands present in all standard Windows installs (not
tested on Windows embedded).

Roadmap:
* Install as Windows service
* Log to Windows Event Log
* Systray Icon w/status indicator
* Configurable NIC whitelist/blacklist
