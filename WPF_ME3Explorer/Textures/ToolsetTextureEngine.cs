﻿using CSharpImageLibrary;
using SaltTPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UsefulThings;
using WPF_ME3Explorer.Debugging;
using WPF_ME3Explorer.PCCObjectsAndBits;
using WPF_ME3Explorer.UI.ViewModels;
using static WPF_ME3Explorer.Textures.Texture2D;

namespace WPF_ME3Explorer.Textures
{
    public static class ToolsetTextureEngine
    {
        public const int ThumbnailSize = 128;

        public static bool TPFToolsModeEnabled = false;

        public static ImageEngineFormat ParseFormat(string formatString)
        {
            if (String.IsNullOrEmpty(formatString))
                return ImageEngineFormat.Unknown;

            if (formatString.Contains("normal", StringComparison.OrdinalIgnoreCase))
                return ImageEngineFormat.DDS_ATI2_3Dc;
            else
                return ImageFormats.FindFormatInString(formatString);
        }

        public static string StringifyFormat(ImageEngineFormat format)
        {
            return format.ToString().Replace("DDS_", "").Replace("_3Dc", "");
        }

        public static void ME1_SortTexturesPCCs(IEnumerable<TreeTexInfo> texes)
        {
            foreach (var tex in texes)
                tex.PCCs.Sort((x, y) => y.Name.Length.CompareTo(x.Name.Length));
        }

        public static MemoryStream GetThumbFromTex2D(Texture2D tex2D)
        {
            byte[] imgData = null;
            var size = tex2D.ImageList.Where(img => img.ImageSize.Width == ThumbnailSize && img.ImageSize.Height == ThumbnailSize);
            if (size.Count() == 0)
                imgData = tex2D.ExtractMaxImage(true);
            else
                imgData = tex2D.ExtractImage(size.First(), true);

            using (MemoryStream ms = new MemoryStream(imgData))
                return GenerateThumbnailToStream(ms, ThumbnailSize);
        }

        internal static MemoryStream GenerateThumbnailToStream(MemoryStream source, int maxDimension, ImageEngineFormat format = ImageEngineFormat.JPG)
        {
            using (ImageEngineImage img = new ImageEngineImage(source, maxDimension))
                return new MemoryStream(img.Save(format, MipHandling.KeepTopOnly, maxDimension));
        }

        /// <summary>
        /// Adds valid DDS header to DDS image data.
        /// </summary>
        /// <param name="imgBuffer"></param>
        /// <param name="imgInfo"></param>
        /// <param name="texFormat"></param>
        /// <returns></returns>
        public static byte[] AddDDSHeader(byte[] imgBuffer, ImageInfo imgInfo, ImageEngineFormat texFormat)
        {
            var header = new CSharpImageLibrary.Headers.DDS_Header(1, (int)imgInfo.ImageSize.Height, (int)imgInfo.ImageSize.Width, texFormat);

            byte[] destination = new byte[imgBuffer.Length + 128];  // 128 = general header size.
            header.WriteToArray(destination, 0);
            Array.Copy(imgBuffer, 0, destination, 128, imgBuffer.Length);
            return destination;
        }

        /// <summary>
        /// Removes DDS header from an image.
        /// </summary>
        /// <param name="imgData">DDS image with header.</param>
        /// <returns>DDS Data without header.</returns>
        public static byte[] RemoveDDSHeader(byte[] imgData)
        {
            byte[] noHeader = new byte[imgData.Length - 128];
            Array.Copy(imgData, 128, noHeader, 0, noHeader.Length);
            return noHeader;
        }

        /// <summary>
        /// Returns hash as a string in the 0xhash format.
        /// </summary>
        /// <param name="hash">Hash as a uint.</param>
        /// <returns>Hash as a string.</returns>
        public static string FormatTexmodHashAsString(uint hash)
        {
            return "0x" + System.Convert.ToString(hash, 16).PadLeft(8, '0').ToUpper();
        }

        /// <summary>
        /// Returns a uint of a hash in string format. 
        /// </summary>
        /// <param name="line">String containing hash in texmod log. 0xhash.</param>
        /// <returns>Hash as a uint.</returns>
        public static uint FormatTexmodHashAsUint(string line)
        {
            uint hash = 0;
            int index = line.IndexOf("0x");
            if (index == -1)  // Not found
                return 0;

            string hashString = line.Substring(index + 2, 8);
            uint.TryParse(hashString, System.Globalization.NumberStyles.AllowHexSpecifier, null, out hash);
            return hash;
        }

        public static MemoryStream OverlayAndPickDetailed(MemoryStream sourceStream)
        {
            // testing 
            return sourceStream;



            /*BitmapSource source = UsefulThings.WPF.Images.CreateWPFBitmap(sourceStream);
            WriteableBitmap dest = new WriteableBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, System.Windows.Media.PixelFormats.Bgra32, source.Palette);

            // KFreon: Write onto black
            var overlayed = Overlay(dest, source);


            // KFreon: Choose the most detailed image between one with alpha merged and one without.
            JpegBitmapEncoder enc = new JpegBitmapEncoder();
            enc.QualityLevel = 90;
            enc.Frames.Add(BitmapFrame.Create(overlayed));

            MemoryStream mstest = new MemoryStream();
            enc.Save(mstest);

            MemoryStream jpg = new MemoryStream();
            using (ImageEngineImage img = new ImageEngineImage(sourceStream))
                img.Save(jpg, ImageEngineFormat.JPG, MipHandling.KeepTopOnly);

            if (jpg.Length > mstest.Length)
            {
                mstest.Dispose();
                return jpg;
            }
            else
            {
                jpg.Dispose();
                return mstest;
            }*/
        }


        /// <summary>
        /// Overlays one image on top of another.
        /// Both images MUST be the same size.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="overlay"></param>
        /// <returns></returns>
        static BitmapSource Overlay(BitmapSource source, BitmapSource overlay)
        {
            if (source.PixelWidth != overlay.PixelWidth || source.PixelHeight != overlay.PixelHeight)
                throw new InvalidDataException("Source and overlay must be the same dimensions.");

            var drawing = new DrawingVisual();
            var context = drawing.RenderOpen();
            context.DrawImage(source, new System.Windows.Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(overlay, new System.Windows.Rect(0, 0, overlay.PixelWidth, overlay.PixelHeight));

            context.Close();
            var overlayed = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
            overlayed.Render(drawing);

            return overlayed;
        }

        

        internal static bool ChangeTexture(TreeTexInfo tex, string newTextureFileName)
        {
            // Get Texture2D
            Texture2D tex2D = GetTexture2D(tex);

            byte[] imgData = File.ReadAllBytes(newTextureFileName);

            // Do stuff different for TPFToolsMode
            if (TPFToolsModeEnabled)
            {
                return true;
            }

            // Update Texture2D
            // KFreon: No format checks required. Auto conversion switched on.
            using (ImageEngineImage img = new ImageEngineImage(imgData))
                tex2D.OneImageToRuleThemAll(img);
                

            // Ensure tex2D is part of the TreeTexInfo for later use.
            tex.ChangedAssociatedTexture = tex2D;
            tex.HasChanged = true;

            return true;
        }

        internal static bool ChangeTexture(TreeTexInfo tex, ImageEngineImage newImage)
        {
            Texture2D tex2D = GetTexture2D(tex);

            // Do stuff different for TPFToolsMode
            if (TPFToolsModeEnabled)
            {
                return true;
            }

            tex2D.OneImageToRuleThemAll(newImage);

            // Ensure tex2D is part of the TreeTexInfo for later use.
            tex.ChangedAssociatedTexture = tex2D;
            tex.HasChanged = true;

            return true;
        }

        static Texture2D GetTexture2D(TreeTexInfo tex)
        {
            if (tex.PCCs?.Count < 1)
                throw new IndexOutOfRangeException($"Tex: {tex.TexName} has no PCC's.");

            if (tex.GameVersion < 1 || tex.GameVersion > 3)
                throw new IndexOutOfRangeException($"Tex: {tex.TexName}'s game version is out of range. Value: {tex.GameVersion}.");

            // Read new texture file
            Texture2D tex2D = null;

            string pccPath = tex.PCCs[0].Name;
            int expID = tex.PCCs[0].ExpID;

            // Texture object has already been created - likely due to texture being updated previously in current session
            if (tex.ChangedAssociatedTexture != null)
                tex2D = tex.ChangedAssociatedTexture;
            else
            {
                PCCObject pcc = null;

                // Create PCCObject
                if (!File.Exists(pccPath))
                    throw new FileNotFoundException($"PCC not found at: {pccPath}.");

                pcc = new PCCObject(tex.PCCs[0].Name, tex.GameVersion);

                if (expID >= pcc.Exports.Count)
                    throw new IndexOutOfRangeException($"Given export ID ({expID}) is out of range. PCC Export Count: {pcc.Exports.Count}.");

                ExportEntry export = pcc.Exports[expID];
                if (!export.ValidTextureClass())
                    throw new InvalidDataException($"Export {expID} in {pccPath} is not a texture. Class: {export.ClassName}, Object Name:{export.ObjectName}.");

                // Create Texture2D
                tex2D = new Texture2D(pcc, expID, new MEDirectories.MEDirectories(tex.GameVersion), tex.Hash);

                pcc?.Dispose(); // Texture2D doesn't need this anymore
            }

            return tex2D;
        }

        internal static void ExtractTexture(TreeTexInfo tex, string filename, bool BuildMips = true, ImageEngineFormat format = ImageEngineFormat.Unknown)
        {
            // Get Texture2D
            Texture2D tex2D = GetTexture2D(tex);

            // Extract texture
            // Convert if requested
            if (format != ImageEngineFormat.Unknown)
            {
                byte[] data = tex2D.ExtractMaxImage(true);
                using (ImageEngineImage img = new ImageEngineImage(data))
                    img.Save(filename, format, BuildMips ? MipHandling.GenerateNew : MipHandling.KeepTopOnly);
            }
            else
                tex2D.ExtractMaxImage(filename);

            // Cleanup if required
            if (tex.ChangedAssociatedTexture != tex2D)
                tex2D.Dispose();
        }

        internal static byte[] ExtractTexture(TreeTexInfo tex)
        {
            // Get Texture2D
            Texture2D tex2D = GetTexture2D(tex);

            // Extract texture
            byte[] data = tex2D.ExtractMaxImage(true);

            // Cleanup if required
            if (tex.ChangedAssociatedTexture != tex2D)
                tex2D.Dispose();

            return data;
        }

        internal static void ME1_LowResFix(TreeTexInfo tex)
        {
            // Get Texture2D
            Texture2D tex2D = GetTexture2D(tex);

            // Apply fix
            tex2D.LowResFix();

            // Cleanup if required
            if (tex.ChangedAssociatedTexture != tex2D)
                tex2D.Dispose();
        }

        public static Task InstallTextures<T>(int NumThreads, MEViewModelBase<T> vm, MEDirectories.MEDirectories GameDirecs, CancellationTokenSource cts, params AbstractTexInfo[] texes) where T : AbstractTexInfo
        {
            vm.Progress = 1;
            vm.MaxProgress = texes.Length + 1;

            /* Save changes per file, so we don't go opening a pcc 5000000 times to change each of it's textures */
            BufferBlock<Tuple<PCCObject, IGrouping<string, AbstractTexInfo>>> pccReadBuffer = new BufferBlock<Tuple<PCCObject, IGrouping<string, AbstractTexInfo>>>(new DataflowBlockOptions { BoundedCapacity = 10 });
            var tex2DSaver = new TransformBlock<Tuple<PCCObject, IGrouping<string, AbstractTexInfo>>, PCCObject>(pccBits => SaveTex2DToPCC(pccBits, GameDirecs), new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = NumThreads, BoundedCapacity = NumThreads });
            var pccFileSaver = new ActionBlock<PCCObject>(pcc =>
            {
                vm.Status = $"Saving file: {Path.GetFileName(pcc.pccFileName)}. {vm.Progress} / {vm.MaxProgress}";
                pcc.SaveToFile(pcc.pccFileName);
                vm.Progress++;
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });

            pccReadBuffer.LinkTo(tex2DSaver, new DataflowLinkOptions { PropagateCompletion = true });
            tex2DSaver.LinkTo(pccFileSaver, new DataflowLinkOptions { PropagateCompletion = true });

            // Start producing
            PCCProducer(pccReadBuffer, texes, GameDirecs.GameVersion, cts);

            return pccFileSaver.Completion;
        }

        static async Task PCCProducer(BufferBlock<Tuple<PCCObject, IGrouping<string, AbstractTexInfo>>> pccBuffer, AbstractTexInfo[] texes, int GameVersion, CancellationTokenSource cts)
        {
            // Gets all distinct pcc's being altered.
            var pccTexGroups =
                from tex in texes
                from pcc in tex.PCCs
                where pcc.IsChecked
                group tex by pcc.Name;

            // Send each unique PCC to get textures saved to it.
            foreach (var texGroup in pccTexGroups)
            {
                if (cts.IsCancellationRequested)
                    break;

                string pcc = texGroup.Key;
                PCCObject pccobj = new PCCObject(pcc, GameVersion);
                await pccBuffer.SendAsync(new Tuple<PCCObject, IGrouping<string, AbstractTexInfo>>(pccobj, texGroup));
            }

            pccBuffer.Complete();
        }

        static PCCObject SaveTex2DToPCC(Tuple<PCCObject, IGrouping<string, AbstractTexInfo>> pccBits, MEDirectories.MEDirectories GameDirecs)
        {
            var pcc = pccBits.Item1;
            var texGroup = pccBits.Item2;

            // Loop over changed textures, installing to pccobj
            foreach (var tex in texGroup)
            {
                Texture2D newTex2D = null;
                var texType = tex as TreeTexInfo;
                if (texType != null)
                    newTex2D = texType.ChangedAssociatedTexture;
                else
                    newTex2D = new Texture2D(pcc, 0, GameDirecs); // TODO TPFTools

                // Loop over texture's pcc entries to install desired ones.
                foreach (var entry in tex.PCCs.Where(p => p.Name == pcc.pccFileName))
                {
                    // TODO Get old tex2D  WHYYYY
                    Texture2D oldTex2D = new Texture2D(pcc, entry.ExpID, GameDirecs);
                    oldTex2D.CopyImgList(newTex2D, pcc);

                    ExportEntry export = pcc.Exports[entry.ExpID];
                    export.Data = oldTex2D.ToArray(export.DataOffset, pcc);
                }
            }
            return pcc;
        }

        public static string EnsureHashInFilename(string FileName, uint Hash)
        {
            var hash = ToolsetTextureEngine.FormatTexmodHashAsString(Hash);
            return EnsureHashInFilename(FileName, hash);
        }

        public static string EnsureHashInFilename(string FileName, string Hash)
        {
            string ext = Path.GetExtension(FileName);
            string Name = Path.GetFileNameWithoutExtension(FileName);
            string hashedFilename = $"{Name.Replace(ext, "")}{(Name.Contains(Hash) ? "" : "_" + Hash)}{ext}";
            return Path.Combine(Path.GetDirectoryName(FileName), hashedFilename);
        }

        public static List<string> GetHashesAndNamesFromTPF(ZipReader zippy)
        {
            byte[] data = zippy.Entries.Last().Extract(true);
            char[] chars = Array.ConvertAll(data, item => (char)item);

            // Fix formatting, fix case, remove duplpicates, remove empty entries.
            return new string(chars).Replace("\r", "").Replace("_0X", "_0x").Split('\n').Distinct().Where(s => s != "\0").ToList();
        }
    }
}
