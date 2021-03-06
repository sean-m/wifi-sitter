using System;
using System.Collections;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WifiSitter.Helpers
{
    public abstract class AbstractService : ServiceBase
    {
        public static AbstractService Current { get; private set; }

        protected virtual string HelpTextPattern
        {
            get
            {
                #region Help Text

                return
                    @"
USAGE

    {0} [command]

    WHERE [command] is one of

        /console   - run as a console application, for debugging
        /service   - run as a windows service
        /install   - install as a windows service
        /uninstall - uninstall windows service

";

                #endregion
            }
        }

        public abstract string DisplayName { get; }

        public abstract string ServiceDesc { get; }

        public ServiceExecutionMode ServiceExecutionMode { get; private set; }

        protected abstract Guid UninstallGuid { get; }

        protected virtual string UninstallRegKeyPath
        {
            get
            {
                return @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            }
        }

        protected AbstractService(string serviceName)
        {
            ServiceName = serviceName;
            if (Current != null)
            {
                throw new InvalidOperationException(String.Format(
                         "Service {0} is instantiating but service {1} is already instantiated as current.  References to AbstractService.Current will only point to the first service.",
                         GetType().FullName,
                         Current.GetType().FullName));
            }
            Current = this;
        }

        public void Run(string[] args)
        {
            Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

            if (!Configuration.IsModeSet && Debugger.IsAttached)
            {
                Configuration.SetOptions( new[] { "--console" } );
            }
            else if (!Configuration.IsModeSet) {
                Configuration.SetOptions(new[] { "-h" });
            }

            var mode = (OperatingMode)Properties.Settings.Default.operating_mode;

            switch (mode)
            {
                case OperatingMode.service:
                    ServiceExecutionMode = ServiceExecutionMode.Service;
                    CanStop = true;
                    Run(new[] { this });
                    break;

                case OperatingMode.setupservice:
                    ServiceExecutionMode = ServiceExecutionMode.Install;
                    SetupService();
                    break;

                case OperatingMode.console:
                    ServiceExecutionMode = ServiceExecutionMode.Console;
                    Console.WriteLine("Starting Service...");
                    OnStart(args);
                    OnStartCommandLine();
                    Stop();
                    break;

                case OperatingMode.install:
                    ServiceExecutionMode = ServiceExecutionMode.Install;
                    InstallService();
                    break;

                case OperatingMode.uninstall:
                    ServiceExecutionMode = ServiceExecutionMode.Uninstall;
                    UninstallService();
                    break;

                case OperatingMode.uninstallprompt:
                    ServiceExecutionMode = ServiceExecutionMode.Uninstall;
                    if (ConfirmUninstall())
                    {
                        UninstallService();
                        InformUninstalled();
                    }
                    break;
                default:
                    Configuration.SetOptions(new[] { "-h" });
                    break;
            }
        }

        protected override void OnStart(string[] args)
        {
            OnStartImpl(args);

            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        }

        protected abstract void OnStartCommandLine();

        protected abstract void OnStartImpl(string[] args);

        void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // do something useful here, log it..
        }

        protected override void OnShutdown()
        {
            Stop();
        }

        protected override void OnStop()
        {
            OnStopImpl();
        }

        protected abstract void OnStopImpl();

        protected virtual bool OnCustomCommandLine(string[] args)
        {
            // for extension
            return false;
        }

        private void InstallService()
        {
            GetInstaller(".InstallLog").Install(new Hashtable());
            InstallServiceCommandLine();
            CreateRegKeys();
            CreateUninstaller();
        }

        private void SetupService() {
            GetInstaller(".InstallLog").Install(new Hashtable());
            InstallServiceCommandLine();
            CreateRegKeys();
        }

        internal abstract void CreateRegKeys();

        private void InstallServiceCommandLine()
        {
            string keyParent = @"SYSTEM\CurrentControlSet\Services\" + ServiceName;
            const string VALUE_NAME = "ImagePath";

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyParent, true))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException("Service not found in registry.");
                    }

                    var origPath = key.GetValue(VALUE_NAME) as string;
                    if (origPath == null)
                    {
                        throw new Exception("HKLM\\" + keyParent + "\\" + VALUE_NAME + " does not exist but was expected.");
                    }

                    var opt = " /service";
                    if (Properties.Settings.Default.enable_ipc) opt += " /i";
                    key.SetValue(VALUE_NAME, origPath.Replace("\"\"", "\"") + opt);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "Error updating service command line after installation.  Unable to write to HKLM\\" + keyParent, ex);
            }
        }

        private void CreateUninstaller()
        {
            using (RegistryKey parent = Registry.LocalMachine.OpenSubKey(UninstallRegKeyPath, true))
            {
                if (parent == null)
                {
                    throw new Exception(String.Format("Uninstall registry key '{0}' not found.", UninstallRegKeyPath));
                }
                try
                {
                    RegistryKey key = null;

                    try
                    {
                        string guidText = UninstallGuid.ToString("B");
                        key = parent.OpenSubKey(guidText, true) ??
                              parent.CreateSubKey(guidText);

                        if (key == null)
                        {
                            throw new Exception(String.Format("Unable to create uninstaller '{0}\\{1}'", UninstallRegKeyPath, guidText));
                        }

                        Assembly asm = GetType().Assembly;
                        Version v = asm.GetName().Version;
                        string exe = "\"" + asm.CodeBase.Substring(8).Replace("/", "\\\\") + "\"";

                        key.SetValue("DisplayName", DisplayName);
                        key.SetValue("ApplicationVersion", v.ToString());
                        key.SetValue("Publisher", "Sean McArdle");
                        key.SetValue("DisplayIcon", exe);
                        key.SetValue("DisplayVersion", v.ToString(2));
                        key.SetValue("URLInfoAbout", "https://github.com/sean-m/wifi-sitter");
                        key.SetValue("Contact", "sean@mcardletech.com");
                        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                        key.SetValue("UninstallString", exe + " /uninstallprompt");
                    }
                    finally
                    {
                        if (key != null)
                        {
                            key.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        "An error occurred writing uninstall information to the registry.  The service is fully installed but can only be uninstalled manually through the command line.",
                        ex);
                }
            }
        }

        private bool ConfirmUninstall()
        {
            string title = "Uninstall " + DisplayName;
            string text = "Are you sure you want to remove " + DisplayName + " from your computer?";
            return DialogResult.Yes ==
                   MessageBox.Show(text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                   MessageBoxDefaultButton.Button2);
        }

        private void InformUninstalled()
        {
            string title = "Uninstall " + DisplayName;
            string text = DisplayName + " has been uninstalled.";
            MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UninstallService()
        {
            try {
                GetInstaller(".UninstallLog").Uninstall(null);
            }
            catch (Exception e) {
                if (e?.InnerException?.Message == "The specified service does not exist as an installed service") {
                    /* Service not installed, we're uninstalling so that's what we want anyhow. */
                }
                else {
                    throw e;
                }
            }
            RemoveRegKeys();
            RemoveUninstaller();
        }

        internal abstract void RemoveRegKeys();

        private TransactedInstaller GetInstaller(string logExtension)
        {
            var ti = new TransactedInstaller();

            ti.Installers.Add(new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem
            });

            ti.Installers.Add(new ServiceInstaller {
                DisplayName = DisplayName,
                ServiceName = ServiceName,
                StartType = ServiceStartMode.Automatic,
                Description = ServiceDesc
            });

            string basePath = Assembly.GetEntryAssembly().Location;
            String path = String.Format("/assemblypath=\"{0}\"", basePath);
            ti.Context = new InstallContext(Path.ChangeExtension(basePath, logExtension), new[] { path });

            return ti;
        }

        private void RemoveUninstaller()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(UninstallRegKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }
                try
                {
                    string guidText = UninstallGuid.ToString("B");
                    RegistryKey child = key.OpenSubKey(guidText);
                    if (child != null)
                    {
                        child.Close();
                        key.DeleteSubKey(guidText);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        "An error occurred removing uninstall information from the registry.  The service was uninstalled will still show up in the add/remove program list.  To remove it manually delete the entry HKLM\\" +
                        UninstallRegKeyPath + "\\" + UninstallGuid, ex);
                }
            }
        }
    }

    public enum ServiceExecutionMode
    {
        Unknown,
        Service,
        Console,
        Install,
        Uninstall,
        Custom
    }
}
