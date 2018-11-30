using System;
using System.Collections.Generic;
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
    public enum Action { PrintHelp, PrintVersion, ListFiles, DownloadFiles }

    class Program
    {
        public const string PROGRAM_NAME = "MtpDownloader";
        public const string VERSION = "0.1";

        //command line options
        OptionSet optionSet;
        public Action action = Action.DownloadFiles;
        public bool deleteFiles = false;
        public bool errorsInCommandLine = false;
        public string deviceId = null;
        public string pattern = "*"; //FIXME *.* ?
        public long days = -1;
        public bool delete = false;
        public string remoteFolder = "\\\\";
        public string localFolder = null;

        /**
         * Entry point
         */
        static void Main(string[] args)
        {
            Program program = new Program(args);
            program.Run();
        }

        /**
         * Constructor
         */
        public Program(string[] args)
        {
            optionSet = CreateOptionSet();
            ParseCommandLine(optionSet, args);
        }

        /**
         * Program execution entry point.
         */
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

            //Connect to MTP devices and pick up the first one
            var devices = MediaDevice.GetDevices();

            if (devices.Count() == 0)
            {
                Console.WriteLine("No devices connected.");
                return;
            }
            else
            {
                Console.WriteLine(devices.Count() + " devices connected.");
            }

            using (var MyDevice = (deviceId != null) ? devices.First(d => d.DeviceId == deviceId) : devices.First())
            {
                MyDevice.Connect();
                /*
                Console.WriteLine("Drives:");
                Console.WriteLine(MyDevice.GetDrives());

                Console.WriteLine("ContentLocations for folders:");
                Console.WriteLine(MyDevice.GetContentLocations(ContentType.Folder));

                Console.WriteLine("ContentLocations for generic file:");
                Console.WriteLine(MyDevice.GetContentLocations(ContentType.GenericFile));

                Console.WriteLine("GetDirectoryInfo.Files:");
                Console.WriteLine(MyDevice.GetDirectoryInfo(remoteFolder).EnumerateFiles());
                */

                if (action == Action.ListFiles)
                {
                    Console.WriteLine("Reading directory...");
                    var DirectoryInfo = MyDevice.GetDirectoryInfo(remoteFolder);
                    var directories = DirectoryInfo.EnumerateDirectories();
                    foreach (var d in directories)
                    {
                        //Console.WriteLine("FOLDER Id=" + d.Id); s10001
                        Console.WriteLine(d.Name);
                    }

                    var files = DirectoryInfo.EnumerateFiles(pattern);
                    foreach (var d in files)
                    {
                        //TODO filter on (date of)d.CreationTime
                        Console.WriteLine(d.Name);
                    }
                    /*Console.WriteLine("'ls' feature not implemented yet...");
                    */

                }
                else if (action == Action.DownloadFiles)
                {
                    Console.WriteLine("'cp' feature not implemented yet...");
                }

                if (delete)
                {
                    Console.WriteLine("'delete' feature not implemented yet...");
                }
                Console.WriteLine("Done.");

                MyDevice.Disconnect();
            }
                

            /*
            //Finding neccessary folder inside the root
            var folder = (root.Files.FirstOrDefault() as PortableDeviceFolder).
                Files.FirstOrDefault(x => x.Name == "Folder") as PortableDeviceFolder;

            //Finding file inside the folder
            var file = (folder as PortableDeviceFolder)?.Files?.FirstOrDefault(x => x.Name == "File");

            //Transfering file into byte array
            var fileIntoByteArr = MyDevice.DownloadFileToStream(file as PortableDeviceFile);

            //Transfering file into file system
            MyDevice.DownloadFile(file as PortableDeviceFile, "\\LOCALPATH");

            //Transfering file rom file system into device folder
            MyDevice.TransferContentToDevice("\\LOCALPATH", folder.Id);

            //Transfering file from stream into device folder
            var imgPath = "\\LOCALPATH";
            var image = Image.FromFile(imgPath);
            byte[] imageB;
            using (var ms = new MemoryStream())
            {
                image.Save(ms, System.Drawing.Imaging.ImageFormat.Gif);
                imageB = ms.ToArray();
            }
            MyDevice.TransferContentToDeviceFromStream("FILE NAME", new MemoryStream(imageB), folder.Id);
            
             */
        }

        /**
         * Print program usage
         */
        void PrintUsage(OptionSet p)
        {
            Console.WriteLine("Usage: ");
            Console.WriteLine("   " + PROGRAM_NAME + " [-d DEVICEID] [-p PATTERN] [-days DAYS] [-delete] [-l remotepath|-cp remotepath localpath]");
            Console.WriteLine("   " + PROGRAM_NAME + " -l");
            Console.WriteLine("   " + PROGRAM_NAME + " -v");
            Console.WriteLine("   " + PROGRAM_NAME + " -h");
            Console.WriteLine("Download files from MTP source.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        /**
         * Print version
         */
        void PrintVersion()
        {
            Console.WriteLine(PROGRAM_NAME + " v." + VERSION);
        }

        /**
         * Create command line NDesk/Options
         */
        public OptionSet CreateOptionSet()
        {
            var p = new OptionSet() {
                { "d|device=", "Select device with guid {DEVICEID}", v => deviceId = v },
                { "p|pattern=", "Select only files matching given {PATTERN}", v => pattern = v },
                { "days=", "copy only files at most {DAYS} days old", (long v) => days = v },
                { "delete", "delete selected files", v => delete = v != null },
                { "cp|copy", "list device content and exit", v => action = Action.DownloadFiles },
                { "l|list|dir", "list device content and exit", v => action = Action.ListFiles },
                { "v|version", "print version and exit", v => action = Action.PrintVersion },
                { "h|help",  "show this message and exit", v => action = Action.PrintHelp },
                };

            return p;
        }

        /**
         * Parse command line NDesk/Options
         * @return null on error
         */
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

        /**
         * Recursively print folder content
         *
        public void DisplayResourceContents(PortableDeviceObject portableDeviceObject)
        {
            Console.WriteLine(portableDeviceObject.Name);
            if (portableDeviceObject is PortableDeviceFolder)
            {
                DisplayFolderContents((PortableDeviceFolder)portableDeviceObject, 0);
            }
        }

        /**
         * Recursively print folder content
         * @param level root folder level (should be 0)
         *
        public void DisplayFolderContents(PortableDeviceFolder folder, int level)
        {
            foreach (var item in folder.Files)
            {
                Console.WriteLine("" + level + " " + item.Name);

                if (item is PortableDeviceFolder)
                {
                    DisplayFolderContents((PortableDeviceFolder)item, level+1);
                }
            }
        }*/
    }
}
