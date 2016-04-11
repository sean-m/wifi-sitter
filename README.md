# WiFi Sitter
It's a babysitter for your WiFi NIC.  

This is born out of the need to have the wifi adapter disabled when Ethernet is
active, seems like this should be a solved problem but there are no good free
tools for this. Some PC vendors produce their own which provide this behavior
but they also try to be the one stop shop for all your WiFi management needs.
Windows does a fine job managing which hotspots you're connected to, this just
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

-  [x] Install as Windows service
-  [x] Log to Windows Event Log
-  [ ] Systray Icon w/status indicator
-  [x] Configurable NIC whitelist/blacklist
-  [x] Prepackaged builds

## Usage

Wifi-Sitter can be installed from the command line. First place the exe where
you'd like it to stay (if you move it, the service will break), then run like
so from an admin shell:  
  
`WifiSitter.exe /install`  
  
The service is configured to start automatically but will not be started after
if you're automating a deployment, you'll need to run `net start wifisitter`
or use your service starting command of choice. 
  
Similarly, uninstall like so:  
  
`WifiSitter.exe /uninstall`  
  
It can be run as a console application for debugging purposes by running:  
  
`WifiSitter.exe /console` 


## Configuration

There isn't much to configure in WifiSitter but there is one tunable to configure,
there may be some network adapters you want ignored complete, Microsoft WiFi 
Direct for example. Network adapters are named "Ethernet" or "WiFi", names are
too generic so the whitelist is made up of the network adapter descriptions.
They are string values located at:
HKLM\SYSTEM\CurrentControlSet\services\WifiSitter\NicWhiteList  
Keys are ignored entirely, they are only used to reference the values and can be
anything, incrementing numbers are used by default. Regular expressions were
overkill for my needs so matching is done by a case-insensitive .StartsWith().
Note, these values are removed when uninstalling.

## Notes

*Many thanks to Matt Davis for  [this](http://stackoverflow.com/a/4865893/977627) answer
and Samuael Neff for [this](http://stackoverflow.com/a/12282179/977627). They helped a lot with converting to a service.*


