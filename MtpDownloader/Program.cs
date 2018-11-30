using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaDevices;
using NDesk.Options;

/**
 * MTP devices may have many "storage capabilities", each on with his own file system.
 * 
 * Instead, MediaDevices library will show these file systems as different folders under the device root folder.
 * 
 * Each storage, esch folder and each file is internally identified by an ID, which is quite useless for the user (e.g."o32D9")
 */
namespace MtpDownloader
{
    public enum Action { PrintHelp, PrintVersion, ListDevices, ListFiles, DownloadFiles }

    class Program
    {
        public const string PROGRAM_NAME = "MtpDownloader";
        public const string VERSION = "0.1";

        //command line options
        OptionSet optionSet;
        public Action action = Action.DownloadFiles;
        public string deviceId = null;
        public string fileNamePattern = "*"; //FIXME *.* ?
        public long maxDays = -1;
        public bool deleteFiles = false;
        public string remoteFolder = "\\\\";
        public string localFolder = null;
        public bool errorsInCommandLine = false;

        Ini inifile;

        /// <summary>
        /// Entry point
        /// </summary>
        static void Main(string[] args)
        {
            Program program = new Program(args);
            program.Run();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public Program(string[] args)
        {
            optionSet = CreateOptionSet();
            ParseCommandLine(optionSet, args);
            var inifilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), PROGRAM_NAME + ".ini");
            inifile = new Ini(inifilename);
        }

        /// <summary>
        /// Program execution entry point.
        /// </summary>
        public void Run()
        {
            if (action == Action.PrintHelp || errorsInCommandLine)
            {
                PrintUsage(optionSet);
                //TODO give some exit code if errorsInCommandLine
                return;
            }
            else if (action == Action.PrintVersion)
            {
                PrintVersion();
                return;
            }

            //Connect to MTP devices
            var devices = MediaDevice.GetDevices();

            if (devices.Count() == 0)
            {
                Console.WriteLine("No devices connected.");
                return;
            }
            else
            {
                if (action == Action.ListDevices)
                {
                    foreach (var device in devices)
                    {
                        Console.WriteLine(device.DeviceId);
                    }
                    return;
                }
                Console.WriteLine(devices.Count() + " devices connected.");
            }

            using (var MyDevice = (deviceId != null) ? devices.First(d => d.DeviceId == deviceId) : devices.First())
            {
                MyDevice.Connect();

                if (action == Action.ListFiles)
                {
                    Console.WriteLine("Reading directory...");

                    foreach (var d in GetRemoteFoldersNames(MyDevice))
                    {
                        Console.WriteLine(d);
                    }

                    foreach (var f in GetRemoteFilesNames(MyDevice))
                    {
                        Console.WriteLine(f);
                    }

                }
                else if (action == Action.DownloadFiles)
                {
                    Console.WriteLine("'cp' feature not implemented yet...");
                }

                if (deleteFiles)
                {
                    Console.WriteLine("'delete' feature not implemented yet...");
                }
                Console.WriteLine("Done.");

                MyDevice.Disconnect();
            }

        }

        /// <summary>
        /// Print program usage
        /// </summary>
        void PrintUsage(OptionSet p)
        {
            Console.WriteLine("Usage: ");
            Console.WriteLine("   " + PROGRAM_NAME + " [-d DEVICEID] [-p PATTERN] [-days DAYS] [-delete] [-l remotepath|-cp remotepath localpath]");
            Console.WriteLine("   " + PROGRAM_NAME + " -ld");
            Console.WriteLine("   " + PROGRAM_NAME + " -v");
            Console.WriteLine("   " + PROGRAM_NAME + " -h");
            Console.WriteLine("Download files from MTP source.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        /// <summary>
        /// Print version
        /// </summary>
        void PrintVersion()
        {
            Console.WriteLine(PROGRAM_NAME + " v." + VERSION);
        }

        /// <summary>
        /// Create command line NDesk/Options
        /// </summary>
        public OptionSet CreateOptionSet()
        {
            var p = new OptionSet() {
                { "d|device=", "Select device with guid {DEVICEID}", v => deviceId = v },
                { "p|pattern=", "Select only files matching given {PATTERN}", v => fileNamePattern = v },
                { "days=", "Select only files at most {DAYS} days old", (long v) => maxDays = v },
                { "delete", "Delete selected files", v => deleteFiles = v != null },
                { "cp|copy", "list device content and exit", v => action = Action.DownloadFiles },
                { "l|list|dir", "list device content and exit", v => action = Action.ListFiles },
                { "ld|list-devices", "list devices and exit", v => action = Action.ListDevices },
                { "v|version", "print version and exit", v => action = Action.PrintVersion },
                { "h|help",  "show this message and exit", v => action = Action.PrintHelp },
                };

            return p;
        }

        /// <summary>
        /// Parse command line NDesk/Options
        /// @return null on error
        /// </summary>
        public void ParseCommandLine(OptionSet optionSet, string[] args)
        {
            try
            {
                List<string> extraArgs = optionSet.Parse(args);

                if (action == Action.DownloadFiles && extraArgs.Count == 2)
                {
                    remoteFolder = extraArgs[0];
                    localFolder = extraArgs[1];
                }
                else if (action == Action.ListFiles && extraArgs.Count == 1)
                {
                    remoteFolder = extraArgs[0];
                }
                else if (extraArgs.Count > 0)
                {
                    Console.WriteLine("Unrecognized options:" + extraArgs.ToList());
                    errorsInCommandLine = true;
                }
            }
            catch (OptionException)
            {
                errorsInCommandLine = true;
            }
        }

        /// <summary>
        /// Return list of folders in remote folder. Currently, this is just EnumerateDirectories.
        /// </summary>
        public IEnumerable<string> GetRemoteFoldersNames(MediaDevice myDevice)
        {
            return myDevice.EnumerateDirectories(remoteFolder);
        }

        /// <summary>
        /// Return list of files in remote folder, using user defined filters
        /// </summary>
        public List<string> GetRemoteFilesNames(MediaDevice myDevice)
        {
            var list = new List<string>();
            var directoryInfo = myDevice.GetDirectoryInfo(remoteFolder);
            var files = directoryInfo.EnumerateFiles(fileNamePattern);
            if (maxDays < 0)
            {
                foreach (var f in files)
                {
                    list.Add(f.Name);
                }
            }
            else
            {
                var maxTime = DateTime.Today.AddDays(-maxDays);
                foreach (var f in files)
                {
                    if (f.CreationTime >= maxTime)
                        list.Add(f.Name);
                }
            }
            return list;
        }

        /// <summary>
        /// Create and save default INI file
        /// </summary>
        public void CreateDefaultIniFile()
        {
            inifile.WriteValue("localFolder", "main", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            inifile.Save();
        }
    }
}
