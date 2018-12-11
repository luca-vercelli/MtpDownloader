using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaDevices;
using NDesk.Options;

namespace MtpDownloader
{
    /// <summary>
    /// Transfer content from portable devices to PC via MTP/WPD protocol.
    /// 
    /// MTP devices may have many "storage capabilities", each on with his own file system. 
    /// Instead, MediaDevices library will show these file systems as different folders under the device root folder.
    /// 
    /// Each storage, each folder and each file is internally identified by an ID, which is quite useless for the user (e.g."o32D9").
    /// 
    /// Each device is identified by some terribly long DeviceId; we prefer identify them by Description.
    /// 
    /// </summary>
    class Program
    {
        public const string PROGRAM_NAME = "MtpDownloader";
        public const string VERSION = "0.1";
        public const string LOGO_SUBFOLDER = "logo";
        public const string VIDEO_SUBFOLDER = "video";
        public const int MIN_FILES_PER_FOLDER = 20;
        public const string NODATE_SUBFOLDER = "nodate";

        //command line options
        OptionSet optionSet;
        public HashSet<Action> actions = new HashSet<Action>();
        public string deviceDescription = null;
        public string fileNamePattern = "*";
        public bool recursive = false;
        public DateTime? minDate = null;
        public DateTime? maxDate = null;
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
                var files = GetAllRemoteFilesNames(myDevice, remoteFolders, recursive);

                if (actions.Contains(Action.CountFiles))
                {
                    //count is written /before/ the full list is written.
                    int count = GetAllRemoteFilesNames(myDevice, remoteFolders, recursive).Count();
                    Console.Error.WriteLine(count + " files found.");
                }

                if (actions.Contains(Action.ListFiles))
                {
                    Console.Error.WriteLine("Reading directory...");

                    foreach (var f in files)
                    {
                        Console.WriteLine(f.Name);
                    }
                    if (!recursive)
                    {
                        foreach (var foldername in GetAllRemoteFoldersNames(myDevice, remoteFolders, recursive))
                        {
                            Console.WriteLine(foldername);
                        }
                    }
                }

                if (actions.Contains(Action.DownloadFiles))
                {
                    Console.Error.WriteLine("'cp' feature is in beta..."); //FIXME
                    if (removeDuplicates) Console.Error.WriteLine("'rd' feature is in beta..."); //FIXME

                    var database = new Dictionary<string, FileSpec>();

                    foreach (var f in files) //FIXME is this correct? can iterator be used more than once?
                    {
                        var localFilename = Path.Combine(localFolder, f.Name.Substring(f.Name.LastIndexOf("\\") + 1));
                        Console.Error.WriteLine("writing: " + localFilename);
                        FileStream fs = File.Create(localFilename);
                        myDevice.DownloadFile(f.Name, fs);

                        FileSpec fspec = null;

                        if (removeDuplicates || splitFolders)
                        {
                            fspec = new FileSpec(localFilename, f.CreationTime);
                        }

                        if (removeDuplicates)
                        {
                            if (database.ContainsKey(fspec.ContentHash()))
                            {
                                Console.Error.WriteLine("ignoring duplicate file: " + localFilename);
                                File.Delete(localFilename);
                            }
                            else
                            {
                                database[fspec.ContentHash()] = fspec;
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

                    foreach (var filename in files)
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
                { "max-days=", "Select only files at most {DAYS} days old", (long v) => minDate = DateTime.Today.AddDays(-v) },
                { "s|since=", "Select only files not older than {DATE}", v => minDate = DateTime.Parse(v)    },
                { "min-days=", "Select only files at least {DAYS} days old", (long v) => maxDate = DateTime.Today.AddDays(-v) },
                { "b|before=", "Select only files not younger than {DATE}", v => maxDate = DateTime.Parse(v)    },
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
                    else if (actions.Count == 1 && actions.Contains(Action.ListFiles))
                    {
                        // useful exception
                        remoteFolders.Add("\\");
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
        public IEnumerable<MediaFileInfo> GetRemoteFilesNames(MediaDevice myDevice, string remoteFolder, bool recursive)
        {
            var directoryInfo = myDevice.GetDirectoryInfo(remoteFolder);
            var files = directoryInfo.EnumerateFiles(fileNamePattern);
            foreach (var f in files)
            {
                if (minDate != null && f.CreationTime < minDate) continue;
                if (maxDate != null && f.CreationTime > maxDate) continue;
                yield return f;
            }
            if (recursive)
            {
                var subfolders = GetRemoteFoldersNames(myDevice, remoteFolder, false);
                foreach (var subfolder in subfolders)
                {
                    foreach (var f in GetRemoteFilesNames(myDevice, subfolder, recursive))
                    {
                        yield return f;
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
        public IEnumerable<MediaFileInfo> GetAllRemoteFilesNames(MediaDevice myDevice, List<string> remoteFolders, bool recursive)
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
                if (fileSpec.IsVideo())
                {
                    fileSpec.MoveUnder(VIDEO_SUBFOLDER);
                }
            }
        }

        /// <summary>
        /// Put files into many subdirectories, by date, with at least MIN_FILES_PER_FOLDER files per each
        /// </summary>
        void SplitFilesInSmallFolders(Dictionary<string, FileSpec> database)
        {
            List<FileSpec> filesToMove = new List<FileSpec>();
            foreach (var fileSpec in database.Values)
            {
                if (!fileSpec.IsVideo() && !fileSpec.IsLogo())
                {
                    filesToMove.Add(fileSpec);
                }
            }

            if (filesToMove.Count > 0)
            {
                filesToMove.Sort((x, y) => x.CreationTime == null ? (y.CreationTime == null ? 0 : +1) : (y.CreationTime == null ? -1 : x.CreationTime.Value.CompareTo(y.CreationTime.Value)));

                var curSubFolder = "";
                var numFilesInCurFolder = MIN_FILES_PER_FOLDER + 1;
                foreach (var fileSpec in filesToMove)
                {
                    var nextSubfolder = GuessSubfolderName(fileSpec);
                    if (numFilesInCurFolder > MIN_FILES_PER_FOLDER && nextSubfolder != curSubFolder)
                    {
                        Directory.CreateDirectory(Path.Combine(fileSpec.DirectoryName(), curSubFolder));
                        curSubFolder = nextSubfolder;
                        numFilesInCurFolder = 0;
                    }
                    fileSpec.MoveUnder(curSubFolder);
                    ++numFilesInCurFolder;
                }
            }
        }

        private string GuessSubfolderName(FileSpec f)
        {
            if (f.CreationTime == null) return Program.NODATE_SUBFOLDER; //could this really happen?
            return f.CreationTime.Value.ToString("yyyy-MM-DD");
        }
    }

    /// <summary>
    /// Actions supported by main program
    /// </summary>
    public enum Action { PrintHelp, PrintVersion, ListDevices, ListFiles, DownloadFiles, DeleteFiles, CountFiles }

}