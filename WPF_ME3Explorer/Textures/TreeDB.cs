﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UsefulThings.WPF;
using WPF_ME3Explorer.Debugging;
using UsefulThings;
using CSharpImageLibrary;

namespace WPF_ME3Explorer.Textures
{
    public class TreeDB : ViewModelBase
    {
        public MTRangedObservableCollection<TreeTexInfo> Textures { get; set; }
        readonly object Locker = new object();
        public MTRangedObservableCollection<string> ScannedPCCs { get; set; }
        MEDirectories.MEDirectories GameDirecs = null;

        public string TreeVersion { get; set; }

        public string TreePath
        {
            get
            {
                return Path.Combine(MEDirectories.MEDirectories.StorageFolder, "Trees", $"ME{GameVersion}.tree");
            }
        }

        public int GameVersion
        {
            get
            {
                return GameDirecs.GameVersion;
            }
        }

        bool valid = false;
        public bool Valid
        {
            get
            {
                return valid;
            }
            set
            {
                SetProperty(ref valid, value);
            }
        }

        public TreeDB(int gameversion)
        {
            GameDirecs = new MEDirectories.MEDirectories(gameversion);
            Textures = new MTRangedObservableCollection<TreeTexInfo>();
            ScannedPCCs = new MTRangedObservableCollection<string>();
        }

        public TreeDB(List<string> givenPCCs, int gameversion) : this(gameversion)
        {
            ScannedPCCs.AddRange(givenPCCs);
        }

        public void AddTexture(TreeTexInfo tex)
        {
            lock (Locker)
            {
                if (!Textures.Contains<TreeTexInfo>(tex))  // Enable comparison by IEquatable interface
                    Textures.Add(tex);
                else
                {
                    var existing = Textures[Textures.IndexOf(tex)];
                    existing.Update(tex);
                    tex.GenerateThumbnail = null; // clear generation code - frees up many large objects for GC.
                    return;
                }
            }

            // Generate thumbnail if new texture
            tex.GenerateThumbnail();
            tex.GenerateThumbnail = null; // clear generation code - frees up many large objects for GC.
        }

        public bool ReadFromFile(string fileName = null)
        {
            string tempFilename = fileName;
            if (fileName == null)
                tempFilename = TreePath;

            if (!File.Exists(tempFilename))
                return false;

            try
            {
                using (FileStream fs = new FileStream(tempFilename, FileMode.Open))
                {
                    using (GZipStream compressed = new GZipStream(fs, CompressionMode.Decompress))  // Compressed for nice small trees
                    {
                        using (BinaryReader bin = new BinaryReader(compressed))
                        {
                            // Check tree is suitable for this version
                            int magic = bin.ReadInt32();
                            if (magic != 631991)
                            {
                                DebugOutput.PrintLn("Tree too old. Delete and rebuild tree.");
                                return false;
                            }

                            // Tree is suitable. Begin reading
                            int gameVersion = bin.ReadInt32();
                            if (GameDirecs.GameVersion != GameVersion)
                                throw new InvalidOperationException($"Incorrect Tree Loaded. Expected: ME{GameDirecs.GameVersion}, Got: {GameVersion}");

                            TreeVersion = bin.ReadString();
                            int texCount = bin.ReadInt32();
                            for (int i = 0; i < texCount; i++)
                            {
                                TreeTexInfo tex = new TreeTexInfo();
                                tex.TexName = bin.ReadString();
                                tex.Hash = bin.ReadUInt32();
                                tex.StorageType = (Texture2D.storage)bin.ReadInt32();
                                tex.FullPackage = bin.ReadString();
                                tex.Format = (ImageEngineFormat)bin.ReadInt32();

                                Thumbnail thumb = new Thumbnail(GameDirecs.ThumbnailCachePath);
                                thumb.Offset = bin.ReadInt64();
                                thumb.Length = bin.ReadInt32();
                                tex.Thumb = thumb;

                                int numPccs = bin.ReadInt32();
                                for (int j = 0; j < numPccs; j++)
                                {
                                    string userAgnosticPath = bin.ReadString();
                                    int ExpID = bin.ReadInt32();
                                    tex.PCCS.Add(new PCCEntry(Path.Combine(GameDirecs.BasePath, userAgnosticPath), ExpID));
                                }

                                Textures.Add(tex);
                            }
                        }
                    }   
                }
            }
            catch (Exception e)
            {
                DebugOutput.PrintLn($"Failed to load tree: {fileName}. Reason: {e.ToString()}");
                return false;
            }

            Valid = true;
            return true;
        }

        public void SaveToFile(string fileName = null)
        {
            string tempFilename = fileName;

            if (fileName == null)
                tempFilename = TreePath;

            Directory.CreateDirectory(TreePath.GetDirParent()); // Create Trees directory if required

            using (FileStream fs = new FileStream(tempFilename, FileMode.Create))
            {
                using (GZipStream compressed = new GZipStream(fs, CompressionMode.Compress))  // Compress for nice small trees
                {
                    using (BinaryWriter bw = new BinaryWriter(compressed))
                    {
                        bw.Write(631991);
                        bw.Write(GameVersion);
                        bw.Write(ToolsetInfo.Version);
                        bw.Write(Textures.Count);

                        foreach (TreeTexInfo tex in Textures)
                        {
                            bw.Write(tex.TexName);
                            bw.Write(tex.Hash);
                            bw.Write((int)tex.StorageType);
                            bw.Write(tex.FullPackage);
                            bw.Write((int)tex.Format);
                            bw.Write(tex.Thumb.Offset);
                            bw.Write(tex.Thumb.Length);
                            bw.Write(tex.PCCS.Count);
                            foreach (PCCEntry pcc in tex.PCCS)
                            {
                                string tempPath = pcc.Name.Remove(0, GameDirecs.BasePath.Length + 1);
                                bw.Write(tempPath);
                                bw.Write(pcc.ExpID);
                            }
                        }
                    }
                }
            }
        }

        public void Clear(bool ClearPCCs = false)
        {
            Textures?.Clear();

            if (ClearPCCs)
                ScannedPCCs?.Clear();
        }
    }
}