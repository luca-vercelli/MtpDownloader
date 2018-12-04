using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Serialization;
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
        public static string XML_FILE_NAME = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PROGRAM_NAME + ".xml");

        //command line options
        OptionSet optionSet;
        public HashSet<Action> actions = new HashSet<Action>();
        public string deviceDescription = null;
        public string fileNamePattern = "*"; //FIXME *.* ?
        public Boolean recursive = false;
        public Boolean useMinDate = false;
        public DateTime minDate;
        public Boolean removeDuplicates = false;
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
                Console.Error.WriteLine("No devices connected.");
                return;
            }
            else
            {
                Console.Error.WriteLine(devices.Count() + " devices connected.");
                if (actions.Contains(Action.ListDevices))
                {
                    foreach (var device in devices)
                    {
                        Console.WriteLine(device.Description);
                    }
                    return;
                }
            }

            using (var myDevice = (deviceDescription != null) ? devices.First(d => d.Description == deviceDescription) : devices.First())
            {
                myDevice.Connect();

                var filenames = GetAllRemoteFilesNames(myDevice, remoteFolders, recursive);

                if (actions.Contains(Action.ListFiles))
                {
                    Console.Error.WriteLine("Reading directory...");
                    
                    foreach (var f in filenames)
                    {
                        Console.WriteLine(f);
                    }
                    if (!recursive)
                    {
                        foreach (var f in GetAllRemoteFoldersNames(myDevice, remoteFolders, recursive))
                        {
                            Console.WriteLine(f);
                        }
                    }
                }

                if (actions.Contains(Action.DownloadFiles))
                {
                    Console.Error.WriteLine("'cp' feature not implemented yet..."); //FIXME

                    foreach (var filename in filenames)
                    {
                        FileStream fs = File.Create(Path.Combine(localFolder, filename.Substring(filename.LastIndexOf("\\") + 1)));
                        myDevice.DownloadFile(filename, fs);
                    }
                }

                if (actions.Contains(Action.DeleteFiles))
                {
                    Console.Error.WriteLine("'delete' feature not implemented yet..."); //FIXME

                    foreach (var filename in filenames)
                    {
                        Console.Error.WriteLine("going to delete " + filename + "..."); //FIXME
                        // myDevice.DeleteFile(filename);
                    }
                }

                Console.Error.WriteLine("Done.");

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
            Console.WriteLine("   " + PROGRAM_NAME + " [-d DEVICE] [-p PATTERN] [-days DAYS] [-s DATE] [-delete] [-r] [-l] remotepath1 [remotepath2 ...] [-cp localpath [-rd]]");
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
                { "r|recursive", "Recursive search", v => recursive = true  },
                { "rd|remove-duplicates", "Remove duplicates while downloading", v => removeDuplicates = true  },
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
                Console.Error.WriteLine("DEBUG extraArgs=" + string.Join(", ", extraArgs));

                if (actions.Contains(Action.DownloadFiles) || actions.Contains(Action.ListFiles) || actions.Contains(Action.DeleteFiles))
                {
                    // at least 1 remote path required
                    if (extraArgs.Count >= 1)
                    {
                        remoteFolders.AddRange(extraArgs);
                    }
                    else
                    {
                        Console.Error.WriteLine("Missing argument.");
                        errorsInCommandLine = true;
                    }
                }
                else if (extraArgs.Count > 0)
                {
                    Console.Error.WriteLine("Unrecognized options:" + string.Join(", ", extraArgs));
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
        public IEnumerable<string> GetRemoteFoldersNames(MediaDevice myDevice, string remoteFolder, bool recursive)
        {
            if (!recursive)
            {
                return myDevice.EnumerateDirectories(remoteFolder);
            }
            else
            {
                return myDevice.EnumerateDirectories(remoteFolder, "*", SearchOption.AllDirectories);
            }
        }

        /// <summary>
        /// Return list of files in remote folder, using user defined filters
        /// 
        /// Return IEnumerable, not List! because MTP protocol is really slowly, objects are taken
        /// one per time
        /// </summary>
        public IEnumerable<string> GetRemoteFilesNames(MediaDevice myDevice, string remoteFolder, bool recursive)
        {
            var directoryInfo = myDevice.GetDirectoryInfo(remoteFolder);
            var files = directoryInfo.EnumerateFiles(fileNamePattern);
            if (useMinDate)
            {
                foreach (var f in files)
                {
                    if (f.CreationTime >= minDate)
                        yield return f.Name;
                }
            }
            else
            {
                foreach (var f in files)
                {
                    yield return f.Name;
                }
            }
            if (recursive)
            {
                var subfolders = GetRemoteFoldersNames(myDevice, remoteFolder, false);
                foreach (var subfolder in subfolders)
                {
                    foreach (var filename in GetRemoteFilesNames(myDevice, subfolder, recursive))
                    {
                        yield return filename;
                    }
                }
            }
        }

        /// <summary>
        /// Return list of files in remote folder, using user defined filters
        /// 
        /// Return IEnumerable, not List! because MTP protocol is really slowly, objects are taken
        /// one per time
        /// </summary>
        public IEnumerable<string> GetAllRemoteFilesNames(MediaDevice myDevice, List<string> remoteFolders, bool recursive)
        {
            foreach (var remoteFolder in remoteFolders)
            {
                foreach (var remoteFile in GetRemoteFilesNames(myDevice, remoteFolder, recursive))
                {
                    yield return remoteFile;
                }
            }
        }


        /// <summary>
        /// Return list of folders in remote folder
        /// 
        /// Return IEnumerable, not List! because MTP protocol is really slowly, objects are taken
        /// one per time
        /// </summary>
        public IEnumerable<string> GetAllRemoteFoldersNames(MediaDevice myDevice, List<string> remoteFolders, bool recursive)
        {
            foreach (var remoteFolder in remoteFolders)
            {
                foreach (var remoteSubfolder in GetRemoteFoldersNames(myDevice, remoteFolder, recursive))
                    yield return remoteSubfolder;
            }
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

    public class FileSpec
    {
        public string Filename;
        public string ContentHash;
        public int Width;
        public int Height;
        public int ColorDepth;
        public int UsedColors;

        //TODO what are used files extensions on smartphones?
        public static string[] imgExtensions = new string[] { "bmp", "jpg", "jpeg", "png", "gif" };
        public static string[] videoExtensions = new string[] { "mp4", "avi" };

        public FileSpec(string filename)
        {
            Filename = filename;

            if (IsImage())
            {
                Image image = Image.FromFile(filename);
                Width = image.Width;
                Height = image.Height;
                CalculateColorDepth(image);
            }

            CalculateContentHash();
        }

        public string Extension()
        {
            int idx = Filename.LastIndexOf(".");
            return (idx < 0) ? "" : Filename.Substring(idx + 1).ToLower();
        }

        public bool IsVideo()
        {
            return videoExtensions.Contains(Extension());
        }

        public bool IsImage()
        {
            return imgExtensions.Contains(Extension());
        }

        public bool IsLogo()
        {
            //this is naive, should consider the real number of colors?
            return IsImage() && ColorDepth <= 8;
        }

        private void CalculateColorDepth(Image image)
        {
            switch (image.PixelFormat)
            {
                case PixelFormat.Format64bppArgb:
                case PixelFormat.Format64bppPArgb:
                    ColorDepth = 64;
                    break;
                case PixelFormat.Format48bppRgb:
                    ColorDepth = 48;
                    break;
                case PixelFormat.Canonical:
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    ColorDepth = 32;
                    break;
                case PixelFormat.Format24bppRgb:
                    ColorDepth = 24;
                    break;
                case PixelFormat.Format16bppArgb1555:
                case PixelFormat.Format16bppGrayScale:
                case PixelFormat.Format16bppRgb555:
                case PixelFormat.Format16bppRgb565:
                    ColorDepth = 16;
                    break;
                case PixelFormat.Format8bppIndexed:
                    ColorDepth = 8;
                    break;
                case PixelFormat.Format4bppIndexed:
                    ColorDepth = 4;
                    break;
                case PixelFormat.Format1bppIndexed:
                    ColorDepth = 1;
                    break;
                default:
                    ColorDepth = -1;  //UNKNOWN
                    break;
            }
        }

        private void CalculateContentHash()
        {

            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(Filename))
                {
                    var hash = md5.ComputeHash(stream);
                    ContentHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        //TODO not used yet
        private void CalculateUsedColors(Bitmap image)
        {
            var cnt = new HashSet<System.Drawing.Color>();

            for (int x = 0; x < image.Width; x++)
                for (int y = 0; y < image.Height; y++)
                {
                    var pixel = image.GetPixel(x, y);
                    cnt.Add(pixel);
                }

            UsedColors = cnt.Count;
        }
    }
}
