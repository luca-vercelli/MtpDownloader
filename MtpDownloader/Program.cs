using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MediaDevices;
using NDesk.Options;

/// <summary>
/// Transfer content fro portable devices to PC via MTP/WPD protocol.
/// 
/// MTP devices may have many "storage capabilities", each on with his own file system. 
/// Instead, MediaDevices library will show these file systems as different folders under the device root folder.
/// 
/// Each storage, each folder and each file is internally identified by an ID, which is quite useless for the user (e.g."o32D9").
/// 
/// Each device is identified by some terribly long DeviceId; we prefer identify them by Description.
/// 
/// </summary>
namespace MtpDownloader
{
    public enum Action { PrintHelp, PrintVersion, ListDevices, ListFiles, DownloadFiles, DeleteFiles, CountFiles }

    class Program
    {
        public const string PROGRAM_NAME = "MtpDownloader";
        public const string VERSION = "0.1";
        public const string LOGO_SUBFOLDER = "logo";
        public const string VIDEO_SUBFOLDER = "video";
        public const int MAX_FILES_PER_FOLDER = 200;

        //command line options
        OptionSet optionSet;
        public HashSet<Action> actions = new HashSet<Action>();
        public string deviceDescription = null;
        public string fileNamePattern = "*"; //FIXME *.* ?
        public bool recursive = false;
        public bool useMinDate = false;
        public DateTime minDate;
        public bool useMaxDate = false;
        public DateTime maxDate;
        public bool removeDuplicates = false;
        public bool splitFolders = false;
        public List<string> remoteFolders = new List<string>();
        public string localFolder = null;
        public bool errorsInCommandLine = false;

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

            using (var myDevice = (deviceDescription != null) ? devices.FirstOrDefault(d => d.Description == deviceDescription) : devices.First())
            {
                if (myDevice == null)
                {
                    if (deviceDescription != null)
                    {
                        Console.Error.WriteLine("Device unknown: " + deviceDescription);
                    }
                    else
                    {
                        Console.Error.WriteLine("Some I/O error occurerd while connecting to device"); //can ever happen?
                    }
                    return;

                }
                myDevice.Connect();

                // FIXME
                // Even if device is connected, it could have not authorized external access yet.
                // (Windows will show an empty device.)
                // In this case, "filenames = GetAllRemoteFilesNames" succedes, however  "foreach (var f in filenames)" will raise Exception:
                // System.Runtime.InteropServices.COMException: Libreria, unità o pool di supporti vuoto. (Eccezione da HRESULT: 0x800710D2)
                var filenames = GetAllRemoteFilesNames(myDevice, remoteFolders, recursive);

                if (actions.Contains(Action.CountFiles))
                {
                    //count is written /before/ the full list is written.
                    int count = GetAllRemoteFilesNames(myDevice, remoteFolders, recursive).Count();
                    Console.Error.WriteLine(count + " files found.");
                }

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
                    Console.Error.WriteLine("'cp' feature is in beta..."); //FIXME
                    if (removeDuplicates) Console.Error.WriteLine("'rd' feature is in beta..."); //FIXME

                    var database = new Dictionary<string, FileSpec>();

                    foreach (var filename in filenames) //FIXME is this correct? can iterator be used more than once?
                    {
                        var localFilename = Path.Combine(localFolder, filename.Substring(filename.LastIndexOf("\\") + 1));
                        Console.Error.WriteLine("writing: " + localFilename);
                        FileStream fs = File.Create(localFilename);
                        myDevice.DownloadFile(filename, fs);

                        FileSpec fspec = null;

                        if (removeDuplicates || splitFolders)
                        {
                            fspec = new FileSpec(localFilename);
                        }

                        if (removeDuplicates)
                        {
                            if (database.ContainsKey(fspec.ContentHash))
                            {
                                Console.Error.WriteLine("ignoring duplicate file: " + localFilename);
                                File.Delete(localFilename);
                            }
                            else
                            {
                                database[fspec.ContentHash] = fspec;
                            }
                        }
                    }

                    if (splitFolders)
                    {
                        Console.Error.WriteLine("'split' feature not implemented..."); //FIXME

                        MoveLogoFiles(database);
                        MoveVideoFiles(database);
                        SplitFilesInSmallFolders(database);
                    }
                }

                if (actions.Contains(Action.DeleteFiles))
                {
                    //FIXME file should be deleted just after downloaded, if download is enabled
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
            Console.Error.WriteLine("Usage: ");
            Console.Error.WriteLine("   " + PROGRAM_NAME + " [-d DEVICE] [-p PATTERN] [-max-days DAYS|-s DATE] [-min-days DAYS|-b DATE] [-delete] [-r] [-l] remotepath1 [remotepath2 ...] [-cp localpath [-rd] [-sp]]");
            Console.Error.WriteLine("   " + PROGRAM_NAME + " -ld");
            Console.Error.WriteLine("   " + PROGRAM_NAME + " -v");
            Console.Error.WriteLine("   " + PROGRAM_NAME + " -h");
            Console.Error.WriteLine("Download files from MTP source.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Error);
        }

        /// <summary>
        /// Create command line NDesk/Options
        /// </summary>
        public OptionSet CreateOptionSet()
        {
            var p = new OptionSet() {
                { "d|device=", "Select device by {DESCRIPTION}", v => deviceDescription = v },
                { "p|pattern=", "Select only files matching given {PATTERN}", v => fileNamePattern = v },
                { "max-days=", "Select only files at most {DAYS} days old", (long v) => { minDate = DateTime.Today.AddDays(-v); useMinDate = true; } },
                { "s|since=", "Select only files not older than {DATE}", v => { minDate = DateTime.Parse(v); useMinDate = true;        }    },
                { "min-days=", "Select only files at least {DAYS} days old", (long v) => { maxDate = DateTime.Today.AddDays(-v); useMaxDate = true; } },
                { "b|before=", "Select only files not younger than {DATE}", v => { maxDate = DateTime.Parse(v); useMaxDate = true;        }    },
                { "r|recursive", "Recursive search", v => recursive = true  },
                { "sp|split", "Split destination folders", v => splitFolders = true  },
                { "rd|remove-duplicates", "Remove duplicates while downloading", v => removeDuplicates = true  },
                { "delete", "Delete selected files", v => actions.Add(Action.DeleteFiles) },
                { "l|list|dir", "List device content", v => actions.Add(Action.ListFiles) },
                { "ld|list-devices", "List devices (i.e. their Description)", v => actions.Add(Action.ListDevices) },
                { "cp|copy=", "Copy device files to PC folder", v => { actions.Add(Action.DownloadFiles); localFolder = v; } },
                { "c|count", "Print file count", v => actions.Add(Action.CountFiles) },
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

                if ((removeDuplicates || splitFolders) && !actions.Contains(Action.DownloadFiles))
                {
                    Console.Error.WriteLine("Options -rd and -sp are meaningless without -cp");
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
            foreach (var f in files)
            {
                if (useMinDate && f.CreationTime < minDate) continue;
                if (useMaxDate && f.CreationTime > maxDate) continue;
                yield return f.Name;
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
        /// Move under \logo images that are recognized as drawings
        /// </summary>
        void MoveLogoFiles(Dictionary<string, FileSpec> database)
        {
            Directory.CreateDirectory(Path.Combine(localFolder, LOGO_SUBFOLDER));
            foreach (var fileSpec in database.Values)
            {
                if (fileSpec.IsLogo())
                {
                    fileSpec.MoveUnder(LOGO_SUBFOLDER);
                }
            }
        }

        /// <summary>
        /// Move under \video files that are recognized as videos
        /// </summary>
        void MoveVideoFiles(Dictionary<string, FileSpec> database)
        {
            Directory.CreateDirectory(Path.Combine(localFolder, VIDEO_SUBFOLDER));
            foreach (var fileSpec in database.Values)
            {
                if (fileSpec.IsVideo)
                {
                    fileSpec.MoveUnder(VIDEO_SUBFOLDER);
                }
            }
        }

        /// <summary>
        /// Put files into many subdirectories, by date, with at most MAX_FILES_PRE_FOLDER files per each
        /// </summary>
        void SplitFilesInSmallFolders(Dictionary<string, FileSpec> database)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// This class is used internally to store file attributes
    /// </summary>
    public class FileSpec
    {
        public string FullFilename;
        public string ContentHash;
        public bool IsImage = false;
        public bool IsVideo = false;
        public int Width = -1;
        public int Height = -1;
        public int ColorDepth = -1;
        public int UsedColors = -1;

        //TODO what are used files extensions on smartphones?
        public static string[] imgExtensions = new string[] { "bmp", "jpg", "jpeg", "png", "gif" };
        public static string[] videoExtensions = new string[] { "mp4", "avi" };

        public FileSpec(string fullFilename)
        {
            FullFilename = fullFilename;

            CalculateIsImage();
            if (IsImage)
            {
                Image image = Image.FromFile(fullFilename);
                Width = image.Width;
                Height = image.Height;
                CalculateColorDepth(image);
            }
            else
            {
                CalculateIsVideo();
            }

            CalculateContentHash();
        }

        /// <summary>
        /// Return file extension only
        /// </summary>
        public string Extension()
        {
            return Path.GetExtension(FullFilename);
        }

        /// <summary>
        /// Return file name only
        /// </summary>
        public string Filename()
        {
            return Path.GetFileName(FullFilename);
        }

        /// <summary>
        /// Return the full path of the folder containing this file (right?)
        /// </summary>
        public string Folder()
        {
            return Path.GetDirectoryName(FullFilename);
        }

        /// <summary>
        /// Move file to another folder. The folder must exist.
        /// </summary>
        public void MoveTo(string newfolder)
        {
            var newFullname = Path.Combine(newfolder, Filename());
            File.Move(FullFilename, newFullname);
            FullFilename = newFullname;
        }

        /// <summary>
        /// Move file to a subfolder. The subfolder must exist.
        /// </summary>
        public void MoveUnder(string subfolder)
        {
            var newFullname = Path.Combine(Folder(), subfolder, Filename());
            File.Move(FullFilename, newFullname);
            FullFilename = newFullname;
        }

        /// <summary>
        /// Guess if this file is a video, looking at its extension.
        /// </summary>
        private void CalculateIsVideo()
        {
            IsVideo = videoExtensions.Contains(Extension());
        }

        /// <summary>
        /// Guess if this file is an image, looking at its extension.
        /// </summary>
        private void CalculateIsImage()
        {
            IsImage = imgExtensions.Contains(Extension());
        }

        /// <summary>
        /// Guess if this image is a drawing/logo instead of a photo. Algorithm is naive, should consider the real number of colors?
        /// </summary>
        public bool IsLogo()
        {
            return IsImage && ColorDepth <= 8;
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
                using (var stream = File.OpenRead(FullFilename))
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