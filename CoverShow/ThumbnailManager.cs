using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.IsolatedStorage;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.CoverShow.FlowComponent;
namespace Wpf.CoverShow
{
    public class ThumbnailManager : IThumbnailManager
    {
        #region Fields
        private readonly IsolatedStorageFile store;
        #endregion
        private static Image AmazonCut(Image image)
        {
            if (image.Width != image.Height)
                return image;
            var bmp = new Bitmap(image);
            int size = image.Height;
            int white = System.Drawing.Color.FromKnownColor(KnownColor.White).ToArgb();
            int i = 0;
            while (i < size / 2)
            {
                if (bmp.GetPixel(i, i).ToArgb() != white)
                    break;
                if (bmp.GetPixel(i, size - 1 - i).ToArgb() != white)
                    break;
                if (bmp.GetPixel(size - 1 - i, i).ToArgb() != white)
                    break;
                if (bmp.GetPixel(size - 1 - i, size - 1 - i).ToArgb() != white)
                    break;
                i++;
            }
            if (i > 0)
            {
                i += 8;
                var zone = new Rectangle(i, i, size - 2 * i, size - 2 * i);
                return bmp.Clone(zone, System.Drawing.Imaging.PixelFormat.DontCare);
            }
            return bmp;
        }
        private static bool IsMp3(string path)
        {
            return string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase);
        }

        private byte[] GetThumbnail(string path)
        {
            Image source = null;
            if (IsMp3(path))
            {
                source = TryExtractAlbumArt(path);
                if (source == null)
                {
                    // Fallback to generic icon if no album art found
                    try { source = Icon.ExtractAssociatedIcon(path)?.ToBitmap(); } catch { }
                }
                if (source == null)
                    throw new InvalidOperationException("No album art or icon available for mp3.");
            }
            else
            {
                source = Image.FromFile(path);
            }
            source = AmazonCut(source);
            int height = source.Height;
            int width = source.Width;
            int factor = (height - 1) / 250 + 1;
            int smallHeight = height / factor;
            int smallWidth = width / factor;
            Image thumb = source.GetThumbnailImage(smallWidth, smallHeight, null, IntPtr.Zero);
            using (var ms = new MemoryStream())
            {
                thumb.Save(ms, ImageFormat.Png);
                ms.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                var result = new byte[ms.Length];
                ms.Read(result, 0, (int)ms.Length);
                return result;
            }
        }
        private static int ReadSyncSafeInteger(byte[] buffer, int offset)
        {
            // 4 bytes syncsafe (7 bits each)
            return (buffer[offset] << 21) | (buffer[offset + 1] << 14) | (buffer[offset + 2] << 7) | buffer[offset + 3];
        }
        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
        }
        private static Image TryExtractAlbumArt(string path)
        {
            // Minimal ID3v2 APIC parser supporting v2.3 (big-endian size) and v2.4 (syncsafe)
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                if (fs.Length < 10)
                    return null;
                byte[] header = br.ReadBytes(10);
                if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
                    return null;
                int versionMajor = header[3];
                // header[4] minor not used
                byte flags = header[5];
                int tagSize = ReadSyncSafeInteger(header, 6);
                long tagEnd = 10 + tagSize;
                if ((flags & 0x40) != 0)
                {
                    // Extended header present; read its size and skip it
                    if (fs.Position + 4 > tagEnd) return null;
                    byte[] ext = br.ReadBytes(4);
                    int extSize = (versionMajor >= 4) ? ReadSyncSafeInteger(ext, 0) : ReadInt32BigEndian(ext, 0);
                    fs.Position += extSize - 4;
                }
                while (fs.Position + 10 <= tagEnd)
                {
                    byte[] frameHeader = br.ReadBytes(10);
                    if (frameHeader[0] == 0)
                        break; // padding
                    string frameId = Encoding.ASCII.GetString(frameHeader, 0, 4);
                    int frameSize = (versionMajor >= 4) ? ReadSyncSafeInteger(frameHeader, 4) : ReadInt32BigEndian(frameHeader, 4);
                    // skip flags (2 bytes)
                    if (frameSize <= 0 || fs.Position + frameSize > tagEnd)
                    {
                        fs.Position = tagEnd;
                        break;
                    }
                    if (frameId == "APIC")
                    {
                        byte[] data = br.ReadBytes(frameSize);
                        try
                        {
                            int idx = 0;
                            // text encoding 0: ISO-8859-1, 1: UTF-16, 2: UTF-16BE, 3: UTF-8
                            byte textEncoding = data[idx++];
                            // MIME type null-terminated (ISO-8859-1)
                            int mimeEnd = Array.IndexOf<byte>(data, 0, idx);
                            if (mimeEnd < 0) return null;
                            string mime = Encoding.ASCII.GetString(data, idx, mimeEnd - idx);
                            idx = mimeEnd + 1;
                            // picture type
                            byte picType = data[idx++];
                            // description (depends on encoding), null-terminated
                            if (textEncoding == 0 || textEncoding == 3)
                            {
                                int descEnd = Array.IndexOf<byte>(data, 0, idx);
                                if (descEnd >= 0) idx = descEnd + 1;
                            }
                            else
                            {
                                // UTF-16: look for 0x00 0x00
                                for (int i = idx; i + 1 < data.Length; i += 2)
                                {
                                    if (data[i] == 0 && data[i + 1] == 0)
                                    {
                                        idx = i + 2;
                                        break;
                                    }
                                }
                            }
                            int imageLen = data.Length - idx;
                            if (imageLen <= 0) return null;
                            using (var ms = new MemoryStream(data, idx, imageLen))
                            {
                                return Image.FromStream(ms, true, true);
                            }
                        }
                        catch { return null; }
                    }
                    else
                    {
                        fs.Position += frameSize;
                    }
                }
            }
            return null;
        }
        public ThumbnailManager()
        {
            store = IsolatedStorageFile.GetUserStoreForAssembly();
        }
        public ImageSource GetThumbnail(string host, string path)
        {
            string thumbName = Path.GetFileName(path);
            if (store.GetFileNames(thumbName).Length == 0)
            {
                using (var stream = new IsolatedStorageFileStream(thumbName, FileMode.CreateNew, store))
                {
                    byte[] data = GetThumbnail(path);
                    stream.Write(data, 0, data.Length);
                }
            }
            using (var stream = new IsolatedStorageFileStream(thumbName, FileMode.Open, store))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
