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
 * Each storage, each folder and each file is internally identified by an ID, which is quite useless for the user (e.g."o32D9")
 * 
 * Each device is identified by some terribly long DeviceId; we prefer identofy them by Description.
 * 
 * A configuration file is created and read from AppData\Local
 */
namespace MtpDownloader
{
    public enum Action { PrintHelp, PrintVersion, ListDevices, ListFiles, DownloadFiles, DeleteFiles }

    class Program
    {
        public const string PROGRAM_NAME = "MtpDownloader";
        public const string VERSION = "0.1";

        public static string INI_FILE_NAME = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PROGRAM_NAME + ".ini");

        //command line options
        OptionSet optionSet;
        public HashSet<Action> actions = new HashSet<Action>();
        public string deviceDescription = null;
        public string fileNamePattern = "*"; //FIXME *.* ?
        public Boolean useMinDate = false;
        public DateTime minDate;
        public List<string> remoteFolders = new List<string>();
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
            inifile = new Ini(INI_FILE_NAME);
            if (!File.Exists(INI_FILE_NAME))
                CreateDefaultIniFile();
        }

        /// <summary>
        /// Program execution entry point.
        /// </summary>
        public void Run()
        {
            if (actions.Contains(Action.PrintHelp) || errorsInCommandLine)
            {
                PrintUsage(optionSet);
                //TODO give some exit code if errorsInCommandLine
                return;
            }

            if (actions.Contains(Action.PrintVersion))
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
                if (actions.Contains(Action.ListDevices))
                {
                    foreach (var device in devices)
                    {
                        Console.WriteLine(device.Description);
                    }
                    return;
                }
                Console.WriteLine(devices.Count() + " devices connected.");
            }

            using (var myDevice = (deviceDescription != null) ? devices.First(d => d.Description == deviceDescription) : devices.First())
            {
                myDevice.Connect();

                var filenames = GetAllRemoteFilesNames(myDevice, remoteFolders);

                if (actions.Contains(Action.ListFiles))
                {
                    Console.WriteLine("Reading directory...");

                    foreach (var d in GetAllRemoteFoldersNames(myDevice, remoteFolders))
                    {
                        Console.WriteLine(d);
                    }

                    foreach (var f in filenames)
                    {
                        Console.WriteLine(f);
                    }
                }

                if (actions.Contains(Action.DownloadFiles))
                {
                    Console.WriteLine("'cp' feature not implemented yet..."); //FIXME

                    foreach (var filename in filenames)
                    {
                        FileStream fs = File.Create(Path.Combine(localFolder, filename.Substring(filename.LastIndexOf("\\") + 1)));
                        myDevice.DownloadFile(filename, fs);
                    }
                }

                if (actions.Contains(Action.DeleteFiles))
                {
                    Console.WriteLine("'delete' feature not implemented yet..."); //FIXME

                    foreach (var filename in filenames)
                    {
                        myDevice.DeleteFile(filename);
                    }
                }

                Console.WriteLine("Done.");

                myDevice.Disconnect();
            }

        }

        /// <summary>
        /// Print version
        /// </summary>
        void PrintVersion()
        {
            Console.WriteLine(PROGRAM_NAME + " v." + VERSION);
        }

        /// <summary>
        /// Print program usage
        /// </summary>
        void PrintUsage(OptionSet p)
        {
            Console.WriteLine("Usage: ");
            Console.WriteLine("   " + PROGRAM_NAME + " [-d DEVICE] [-p PATTERN] [-days DAYS] [-s DATE] [-delete] [-l] remotepath1 [remotepath2 ...] [-cp localpath]");
            Console.WriteLine("   " + PROGRAM_NAME + " -ld");
            Console.WriteLine("   " + PROGRAM_NAME + " -v");
            Console.WriteLine("   " + PROGRAM_NAME + " -h");
            Console.WriteLine("Download files from MTP source.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        /// <summary>
        /// Create command line NDesk/Options
        /// </summary>
        public OptionSet CreateOptionSet()
        {
            var p = new OptionSet() {
                { "d|device=", "Select device by {DESCRIPTION}", v => deviceDescription = v },
                { "p|pattern=", "Select only files matching given {PATTERN}", v => fileNamePattern = v },
                { "days=", "Select only files at most {DAYS} days old", (long v) => { minDate = DateTime.Today.AddDays(-v); useMinDate = true; } },
                { "s|since=", "Select only files not older than {DATE}", v => { minDate = DateTime.Parse(v); useMinDate = true;        }    },
                { "delete", "Delete selected files", v => actions.Add(Action.DeleteFiles) },
                { "l|list|dir", "List device content", v => actions.Add(Action.ListFiles) },
                { "ld|list-devices", "List devices (i.e. their Description)", v => actions.Add(Action.ListDevices) },
                { "cp|copy=", "Copy device files to PC folder", v => { actions.Add(Action.DownloadFiles); localFolder = v; } },
                { "v|version", "Print program version", v => actions.Add(Action.PrintVersion) },
                { "h|help",  "Print this message", v => actions.Add(Action.PrintHelp) },
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

                if (actions.Contains(Action.DownloadFiles) && extraArgs.Count >= 2)
                {
                    localFolder = extraArgs[extraArgs.Count - 1];
                    remoteFolders.AddRange(extraArgs);
                    remoteFolders.Remove(localFolder);
                }
                else if (actions.Contains(Action.ListFiles) && extraArgs.Count == 1)
                {
                    remoteFolders.Add(extraArgs[0]);
                }
                else if (extraArgs.Count > 0)
                {
                    Console.WriteLine("Unrecognized options:" + extraArgs.ToList());
                    errorsInCommandLine = true;
                }

                if (actions.Count == 0)
                {
                    actions.Add(Action.PrintHelp);
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
        public IEnumerable<string> GetRemoteFoldersNames(MediaDevice myDevice, string remoteFolder)
        {
            return myDevice.EnumerateDirectories(remoteFolder);
        }

        /// <summary>
        /// Return list of files in remote folder, using user defined filters
        /// </summary>
        public List<string> GetRemoteFilesNames(MediaDevice myDevice, string remoteFolder)
        {
            var list = new List<string>();
            var directoryInfo = myDevice.GetDirectoryInfo(remoteFolder);
            var files = directoryInfo.EnumerateFiles(fileNamePattern);
            if (useMinDate)
            {
                foreach (var f in files)
                {
                    if (f.CreationTime >= minDate)
                        list.Add(f.Name);
                }
            }
            else
            {
                foreach (var f in files)
                {
                    list.Add(f.Name);
                }
            }
            return list;
        }

        /// <summary>
        /// Return list of files in remote folder, using user defined filters
        /// </summary>
        public List<string> GetAllRemoteFilesNames(MediaDevice myDevice, List<string> remoteFolders)
        {
            var list = new List<string>();
            foreach (var remoteFolder in remoteFolders)
            {
                list.AddRange(GetRemoteFilesNames(myDevice, remoteFolder));
            }
            return list;
        }

        /// <summary>
        /// Return list of folders in remote folder
        /// </summary>
        public List<string> GetAllRemoteFoldersNames(MediaDevice myDevice, List<string> remoteFolders)
        {
            var list = new List<string>();
            foreach (var remoteFolder in remoteFolders)
            {
                list.AddRange(GetRemoteFoldersNames(myDevice, remoteFolder));
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
