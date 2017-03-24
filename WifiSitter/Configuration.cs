using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Mono.Options;


namespace WifiSitter
{
    public static class Configuration
    {
        private static Dictionary<string, object> _options;
        private static bool _optionsSet = false;

        public static void SetOptions(string[] args) {
            bool showHelp = false;
            bool enableIPC = false;
            var mode = OperatingMode.none;

            var opts = new OptionSet() {
                {"h|?|help", "Show this help and exit.",
                    v => showHelp = v != null },
                {"i|ipc", "Option to enable IPC communication for GUI.",
                    v => enableIPC = v != null},
                {"console|service", "Direct wifisitter mode of operation.",
                    v => {
                        switch (v.ToLower()) {
                            case "console":
                                mode = OperatingMode.console;
                                break;
                            case "service":
                                mode = OperatingMode.service;
                                break;
                            default:
                                mode = OperatingMode.none;
                                break;
                        }
                    }
                },
                {"setupservice|install|uninstall|uninstallprompt", "Select wifisitter install/setup operation.",
                    v => {
                        switch (v.ToLower()) {
                            case "setupservice":
                                mode = OperatingMode.setupservice;
                                break;
                            case "install":
                                mode = OperatingMode.install;
                                break;
                            case "uninstall":
                                mode = OperatingMode.uninstall;
                                break;
                            case "uninstallprompt":
                                mode = OperatingMode.uninstallprompt;
                                break;
                            default:
                                mode = OperatingMode.none;
                                break;
                        }
                    }
                }
            };
            try {
                opts.Parse(args);
            }
            catch (OptionException e) {
                ShowHelp(opts, 1);
                return;
            }

            if (showHelp) ShowHelp(opts);

            Properties.Settings.Default.enable_ipc = enableIPC;
            Properties.Settings.Default.operating_mode = (int)mode;
            _optionsSet = true;
        }
        
        public static bool IsOptionsSet { get { return _optionsSet; } }

        public static bool IsModeSet {
            get {
                if (IsOptionsSet) {
                    return (OperatingMode)Properties.Settings.Default.operating_mode != OperatingMode.none;
                };
                return false;
            }
        }

        public static void ShowHelp(OptionSet opts, int exitCode = 0) {
            Console.WriteLine("Usage: wifisitter.exe [option] [directive]");
            Console.WriteLine();
            Console.WriteLine("Wifisitter needs a directive to know whether it's");
            Console.WriteLine("running as a service or command line process.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            opts.WriteOptionDescriptions(Console.Out);

            Environment.Exit(exitCode);
        }
    }

    public enum OperatingMode
    {
        none = 0,
        console,
        service,
        setupservice,
        install,
        uninstall,
        uninstallprompt
    }
}
