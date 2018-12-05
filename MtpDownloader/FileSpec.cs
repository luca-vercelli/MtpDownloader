using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MtpDownloader
{
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
        public DateTime? CreationTime = null;

        //TODO what are used files extensions on smartphones?
        public static string[] imgExtensions = new string[] { "bmp", "jpg", "jpeg", "png", "gif" };
        public static string[] videoExtensions = new string[] { "mp4", "avi" };

        /// <summary>
        /// Constructor
        /// </summary>
        public FileSpec(string fullLocalFilename, DateTime? creationTime)
        {
            FullFilename = fullLocalFilename;
            CreationTime = creationTime;

            CalculateIsImage();
            if (IsImage)
            {
                Image image = Image.FromFile(fullLocalFilename);
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