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
        
        public static void SetOptions(string[] args) {
            _options = new Dictionary<string, object>();

            bool showHelp = false;
            bool enableIPC = false;
            string mode = String.Empty;

            var opts = new OptionSet() {
                {"h|?|help", "Show this help and exit.",
                    v => showHelp = v != null },
                {"i|ipc", "Option to enable IPC communication for GUI.",
                    v => enableIPC = v != null},
                {"console|service", "Direct wifisitter mode of operation.",
                    v => mode = v.ToLower() },
                {"setupservice|install|uninstall|uninstallprompt", "Select wifisitter install/setup operation.",
                    v => mode = v.ToLower() }
            };
            try {
                opts.Parse(args);
            }
            catch (OptionException e) {
                ShowHelp(opts, 1);
                return;
            }

            if (showHelp) ShowHelp(opts);

            _options.Add("enable_ipc", enableIPC);
            _options.Add("operating_mode", mode);
        }

        public static object GetOption(string key) {
            if (!_options.ContainsKey(key)) return null;
            return _options[key];
        }

        public static bool IsOptionsSet { get { return _options != null; } }

        public static bool IsModeSet { get { if (IsOptionsSet) { return !String.IsNullOrEmpty((string)_options["operating_mode"]); }; return false; } }

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
}
