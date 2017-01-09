﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using GTANetworkShared;

namespace GTANetworkServer
{
    internal static class Program
    {
        private static object _filelock = new object();
        private static bool _log;

        public static long GetTicks()
        {
            return DateTime.Now.Ticks/10000;
        }

        public static void ToFile(string path, string str)
        {
            File.AppendAllText(path, "[" + DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss") + "] " + str + Environment.NewLine);
        }

        public static void Output(string str)
        {
            Console.WriteLine("[" + DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss") + "] " + str);

            if (_log)
            lock (_filelock)
            {
                File.AppendAllText("server.log", "[" + DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss") + "] " + str + Environment.NewLine);
            }
        }

        public static int GetHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;

            var bytes = Encoding.UTF8.GetBytes(input.ToLower().ToCharArray());
            uint hash = 0;

            for (int i = 0, length = bytes.Length; i < length; i++)
            {
                hash += bytes[i];
                hash += (hash << 10);
                hash ^= (hash >> 6);
            }

            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);

            return unchecked((int)hash);
        }

        public static string GetHashSHA256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            SHA256Managed hashstring = new SHA256Managed();
            byte[] hash = hashstring.ComputeHash(bytes);
            string hashString = string.Empty;
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }
            return hashString;
        }


        public static string Location { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        internal static GameServer ServerInstance { get; set; }
        internal static bool CloseProgram = false;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteFile(string name);

        static void Main(string[] args)
        {
            var settings = ServerSettings.ReadSettings(Program.Location + "settings.xml");
            
            _log = settings.LogToFile;

            if (_log)
                File.AppendAllText("server.log", "-> SERVER STARTED AT " + DateTime.Now);

            ParseableVersion serverVersion = ParseableVersion.FromAssembly(Assembly.GetExecutingAssembly());

            Console.WriteLine("=======================================================================");
            Console.WriteLine("= GRAND THEFT AUTO NETWORK v{0}", serverVersion);
            Console.WriteLine("=======================================================================");
            Console.WriteLine("= Server Name: " + settings.Name);
            Console.WriteLine("= Server Port: " + settings.Port);
            Console.WriteLine("= Server FQDN: " + settings.fqdn);
            Console.WriteLine("=");
            Console.WriteLine("= Player Limit: " + settings.MaxPlayers);
            Console.WriteLine("= Log Level: " + settings.LogLevel + " (1: ERROR, 2: DEBUG, 3: VERBOSE)");
            Console.WriteLine("=======================================================================");

            if (settings.Port != 4499)
                Output("WARN: Port is not the default one, players on your local network won't be able to automatically detect you!");

            Output("Starting...");

            //AppDomain.CurrentDomain.SetShadowCopyFiles();

            if (!Directory.Exists("resources"))
            {
                Output("ERROR: Necessary \"resources\" folder does not exist!");
                Console.Read();
                return;
            }

            ServerInstance = new GameServer(settings);
            ServerInstance.AllowDisplayNames = true;

            ServerInstance.Start(settings.Resources.Select(r => r.Path).ToArray());

            Output("Started! Waiting for connections.");

            if (Type.GetType("Mono.Runtime") == null)
            {
                SetConsoleCtrlHandler(new HandlerRoutine(ConsoleCtrlCheck), true);
            }
            else
            {
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    ConsoleCtrlCheck(CtrlTypes.CTRL_C_EVENT);
                };
            }

            while (!CloseProgram)
            {
                ServerInstance.Tick();
                Thread.Sleep(1000/settings.RefreshHz);
            }

        }


        #region unmanaged

        private static bool ConsoleCtrlCheck(CtrlTypes ctrType)
        {
            try
            {
                ServerInstance.IsClosing = true;
                Program.Output("Terminating...");
                DateTime start = DateTime.Now;
                while (!ServerInstance.ReadyToClose)
                {
                    Thread.Sleep(10);
                }
                CloseProgram = true;
            }
            catch (Exception e) { Program.Output("An exception has occured while closing server.\n" + e.ToString()); } //Proper fix is needed but this isn't problematic
            return true;
        }

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        // A delegate type to be used as the handler routine
        // for SetConsoleCtrlHandler.

        public delegate bool HandlerRoutine(CtrlTypes CtrlType);

        // An enumerated type for the control messages
        // sent to the handler routine.

        public enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        #endregion

    }
}
