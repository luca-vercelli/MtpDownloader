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
        public DateTime? CreationTime = null;
        public string _ContentHash = null;
        public bool? _IsImage = null;
        public bool? _IsDrawImage = null;
        public bool? _IsVideo = null;
        public int? _Width = null;
        public int? _Height = null;
        public int? _ColorDepth = null;
        public int? _UsedColors = null;

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
        public string DirectoryName()
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
            var newFullname = Path.Combine(DirectoryName(), subfolder, Filename());
            File.Move(FullFilename, newFullname);
            FullFilename = newFullname;
        }

        /// <summary>
        /// Guess if this file is a video, looking at its extension.
        /// </summary>
        public bool IsVideo()
        {
            if (_IsVideo == null)
            {
                _IsVideo = videoExtensions.Contains(Extension());
                if (_IsVideo.Value)
                {
                    _IsImage = false;
                }
            }
            return _IsVideo.Value;
        }

        /// <summary>
        /// Guess if this file is an image, looking at its extension.
        /// </summary>
        public bool IsImage()
        {
            if (_IsImage == null)
            {
                _IsVideo = imgExtensions.Contains(Extension());
                if (_IsImage.Value)
                {
                    _IsVideo = false;
                }
            }
            return _IsImage.Value;
        }

        /// <summary>
        /// Populate _Width, _Height, _ColorDepth.
        /// </summary>
        private void CalculateImageProperties()
        {
            Image image = Image.FromFile(FullFilename);
            _Width = image.Width;
            _Height = image.Height;

            //Now calculate _ColorDepth
            switch (image.PixelFormat)
            {
                case PixelFormat.Format64bppArgb:
                case PixelFormat.Format64bppPArgb:
                    _ColorDepth = 64;
                    break;
                case PixelFormat.Format48bppRgb:
                    _ColorDepth = 48;
                    break;
                case PixelFormat.Canonical:
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    _ColorDepth = 32;
                    break;
                case PixelFormat.Format24bppRgb:
                    _ColorDepth = 24;
                    break;
                case PixelFormat.Format16bppArgb1555:
                case PixelFormat.Format16bppGrayScale:
                case PixelFormat.Format16bppRgb555:
                case PixelFormat.Format16bppRgb565:
                    _ColorDepth = 16;
                    break;
                case PixelFormat.Format8bppIndexed:
                    _ColorDepth = 8;
                    break;
                case PixelFormat.Format4bppIndexed:
                    _ColorDepth = 4;
                    break;
                case PixelFormat.Format1bppIndexed:
                    _ColorDepth = 1;
                    break;
                default:
                    _ColorDepth = -1;
                    break;
            }
            //TODO what is the correct way to force loaded image into "garbage" ?
        }

        /// <summary>
        /// Guess if this image is a drawing/logo instead of a photo. Algorithm is naive, should consider the real number of colors?
        /// </summary>
        public bool IsLogo()
        {
            if (_IsDrawImage == null)
            {
                _IsDrawImage = IsImage() && ColorDepth() <= 8 && ColorDepth() > 8;
            }
            return _IsDrawImage.Value;
        }

        /// <summary>
        /// Number of color bits, currently in range [1-64]. -1 means "unknown".
        /// </summary>
        public int ColorDepth()
        {
            if (_ColorDepth == null)
            {
                CalculateImageProperties();
            }
            return _ColorDepth.Value;
        }

        /// <summary>
        /// Image width.
        /// </summary>
        public int Width()
        {
            if (_Width == null)
            {
                CalculateImageProperties();
            }
            return _Width.Value;
        }

        /// <summary>
        /// Image height.
        /// </summary>
        public int Height()
        {
            if (_Height == null)
            {
                CalculateImageProperties();
            }
            return _Height.Value;
        }

        /// <summary>
        /// MD5 hash of full file content.
        /// </summary>
        public string ContentHash()
        {
            if (_ContentHash == null)
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(FullFilename))
                    {
                        var hash = md5.ComputeHash(stream);
                        _ContentHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            return _ContentHash;
        }

        /// <summary>
        /// MD5 hash of full file content.
        /// 
        /// Not used yet.
        /// </summary>
        public int UsedColors(Bitmap image)
        {
            if (_UsedColors == null)
            {
                var cnt = new HashSet<System.Drawing.Color>();

                for (int x = 0; x < image.Width; x++)
                    for (int y = 0; y < image.Height; y++)
                    {
                        var pixel = image.GetPixel(x, y);
                        cnt.Add(pixel);
                    }

                _UsedColors = cnt.Count;
            }
            return _UsedColors.Value;
        }
    }
}