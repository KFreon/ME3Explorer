﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;

namespace SaltTPF
{
    public static class ZipWriter
    {
        private class ZipEntry
        {
            public UInt16 Time;
            public UInt16 Date;
            public UInt32 CRC32;
            public UInt32 ComprLen;
            public UInt32 UncLen;
            public long MainOffset;
            public String Filename;

            public ZipEntry() { }
        }

        public const UInt16 MinVers = 0x14;
        public const UInt16 BitFlag = 0x9; // Set: Encrypted, Defer CRC
        public const UInt16 ComprMethod = 0x8;
        public static uint tpfxor = 0x3FA43FA4;
        public const UInt32 ExternalAttr = 0x81B40020;
        static CRC32 crc = new CRC32();

        static ZipEntry BuildEntry(FileStream fs, string file, Func<byte[]> dataGetter)
        {
            bool FileOnDisk = dataGetter == null;

            ZipEntry entry = new ZipEntry();
            entry.MainOffset = fs.Position;
            entry.Filename = FileOnDisk ? Path.GetFileName(file) : file;

            //Header
            fs.Write(BitConverter.GetBytes(ZipReader.filemagic), 0, 4);
            fs.Write(BitConverter.GetBytes(MinVers), 0, 2);
            fs.Write(BitConverter.GetBytes(BitFlag), 0, 2);
            fs.Write(BitConverter.GetBytes(ComprMethod), 0, 2);

            //Date/time
            DateTime date = FileOnDisk ? new FileInfo(file).LastWriteTime : DateTime.Now;
            ushort secs = 0;
            ushort mins = 0;
            ushort hours = 0;

            ushort day = 0;
            ushort month = 0;
            ushort year = 0;

            secs = (ushort)date.Second;
            mins = (ushort)date.Minute;
            hours = (ushort)date.Hour;
            
            ushort datetime = (ushort)(secs / 2);
            datetime |= (ushort)(mins >> 5);
            datetime |= (ushort)(hours >> 11);
            fs.Write(BitConverter.GetBytes(datetime), 0, 2);
            entry.Time = datetime;

            day = (ushort)date.Day;
            month = (ushort)date.Month;
            year = (ushort)date.Year;
            
            datetime = day;
            datetime &= (ushort)(month >> 5);
            datetime &= (ushort)(year >> 9);
            fs.Write(BitConverter.GetBytes(datetime), 0, 2);
            entry.Date = datetime;

            byte[] comprData;
            long temppos;
            Stream dataStream = null;
            if (FileOnDisk)
                dataStream = new FileStream(file, FileMode.Open, FileAccess.Read);
            else
                new MemoryStream(dataGetter());

            using (dataStream)
            {
                byte[] buff = new byte[dataStream.Length];
                dataStream.Read(buff, 0, buff.Length);

                entry.CRC32 = crc.BlockChecksum(buff);
                entry.UncLen = (UInt32)dataStream.Length;

                fs.Write(BitConverter.GetBytes(entry.CRC32), 0, 4);

                fs.Seek(4, SeekOrigin.Current); // Skip compressed size
                fs.Write(BitConverter.GetBytes(entry.UncLen), 0, 4);

                if (entry.Filename.ToLower().Contains("meresults"))
                    entry.Filename = "texmod.log";

                fs.Write(BitConverter.GetBytes((ushort)entry.Filename.Length), 0, 2);
                fs.Write(BitConverter.GetBytes((ushort)0), 0, 2);
                foreach (char c in entry.Filename)
                    fs.WriteByte((byte)c);
                temppos = fs.Position;

                dataStream.Seek(0, SeekOrigin.Begin); // Rewind
                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream deflator = new DeflateStream(ms, CompressionMode.Compress))
                        dataStream.CopyTo(deflator);

                    comprData = ms.ToArray();
                }
                entry.ComprLen = (uint)comprData.Length + 12;
            }

            fs.Seek(entry.MainOffset + 18, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(entry.ComprLen), 0, 4);
            fs.Seek(temppos, SeekOrigin.Begin);
            ZipCrypto.EncryptData(fs, comprData); // Encrypt and write data

            fs.Write(BitConverter.GetBytes(ZipReader.datadescriptormagic), 0, 4);
            fs.Write(BitConverter.GetBytes(entry.CRC32), 0, 4);
            fs.Write(BitConverter.GetBytes(entry.ComprLen), 0, 4);
            fs.Write(BitConverter.GetBytes(entry.UncLen), 0, 4);
            return entry;
        }

        static void WriteTPF(FileStream fs, string Author, string Comment, List<ZipEntry> Entries)
        {
            uint cdlen = 0;
            uint cdpos = (uint)fs.Position;
            for (int i = 0; i < Entries.Count; i++)
            {
                long start = fs.Position;
                fs.Write(BitConverter.GetBytes(ZipReader.dirfileheadermagic), 0, 4);
                fs.Write(BitConverter.GetBytes(ZipWriter.MinVers), 0, 2);
                fs.Write(BitConverter.GetBytes(ZipWriter.MinVers), 0, 2);
                fs.Write(BitConverter.GetBytes(ZipWriter.BitFlag), 0, 2);
                fs.Write(BitConverter.GetBytes(ZipWriter.ComprMethod), 0, 2);

                ZipWriter.ZipEntry entry = Entries[i];
                fs.Write(BitConverter.GetBytes(entry.Time), 0, 2);
                fs.Write(BitConverter.GetBytes(entry.Date), 0, 2);
                fs.Write(BitConverter.GetBytes(entry.CRC32), 0, 4);
                fs.Write(BitConverter.GetBytes(entry.ComprLen), 0, 4);
                fs.Write(BitConverter.GetBytes(entry.UncLen), 0, 4);
                fs.Write(BitConverter.GetBytes((ushort)entry.Filename.Length), 0, 2);
                fs.Write(BitConverter.GetBytes(0), 0, 4); // 0 for extra field, 0 for comment
                fs.Write(BitConverter.GetBytes(0), 0, 4); // 0 for disk no, 0 for internal attributes
                fs.Write(BitConverter.GetBytes(0x81B40020), 0, 4);
                fs.Write(BitConverter.GetBytes((int)entry.MainOffset), 0, 4);
                foreach (char c in entry.Filename)
                    fs.WriteByte((byte)c);
                long end = fs.Position;
                cdlen += (uint)(end - start);
            }

            fs.Write(BitConverter.GetBytes(ZipReader.endofdirmagic), 0, 4);
            fs.Write(BitConverter.GetBytes(0), 0, 4);
            fs.Write(BitConverter.GetBytes((ushort)Entries.Count), 0, 2);
            fs.Write(BitConverter.GetBytes((ushort)Entries.Count), 0, 2);
            fs.Write(BitConverter.GetBytes(cdlen), 0, 4);
            fs.Write(BitConverter.GetBytes(cdpos), 0, 4);

            // KFreon: This is the created by/comment section. Seems to be required for Texmod.
            //fs.Write(BitConverter.GetBytes(0), 0, 2);  
            string createdByANDComment = Author + "\n" + Comment;  // KFreon: Newline required by Texmod to seperate author and comment.
            byte[] bytes = Encoding.Default.GetBytes(createdByANDComment);
            fs.Write(bytes, 0, bytes.Length);


            long streamlen = fs.Length;
            fs.Seek(0, SeekOrigin.Begin); // XOR the file

            while (fs.Position < fs.Length)
            {
                int count = (fs.Position + 10000 <= fs.Length) ? 10000 : (int)(fs.Length - fs.Position);

                byte[] buff2 = BuffXOR(fs, count);
                fs.Seek(-count, SeekOrigin.Current);
                fs.Write(buff2, 0, count);
            }
        }

        public static void Repack(String filename, List<String> files, string Author = "", string Comment = "")
        {
            List<ZipEntry> Entries = new List<ZipEntry>();
            foreach (String file in files)
            {
                if (!File.Exists(file))
                    throw new FileNotFoundException(Path.GetFileName(file) + " was not found");
            }

            using (FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
            {
                foreach (String file in files)
                {
                    var entry = BuildEntry(fs, file, null);
                    Entries.Add(entry);
                }

                WriteTPF(fs, Author, Comment, Entries);
            }
        }

        public static void Repack(string destination, List<Tuple<string, Func<byte[]>>> FilenamesAndDataGetters, string Author = "", string Comment = "")
        {
            List<ZipEntry> Entries = new List<ZipEntry>();
            using (FileStream fs = new FileStream(destination, FileMode.Create))
            {
                foreach (var tex in FilenamesAndDataGetters)
                {
                    var entry = BuildEntry(fs, tex.Item1, tex.Item2);
                    Entries.Add(entry);
                }

                WriteTPF(fs, Author, Comment, Entries);
            }
        }

        private static byte[] BuffXOR(Stream buffstream, int count)
        {
            if (buffstream.Position + count > buffstream.Length)
                throw new IndexOutOfRangeException("Filestream not long enough to XOR");
            byte[] buff = new byte[count];
            UInt32 tempxor = tpfxor;
            int mod = (int)buffstream.Position % 4;
            int buffpos = 0;
            if (mod != 0)
            {
                for (int i = 0; i < mod; i++)
                    tempxor >>= 8;
                for (int i = 0; i < (4 - mod); i++)
                {
                    buff[buffpos++] = (byte)((byte)buffstream.ReadByte() ^ (byte)tempxor);
                    tempxor >>= 8;
                }
                tempxor = tpfxor;
            }
            else
                mod = 4;
            int streampos = (int)buffstream.Position;
            int iters = (count - (4 - mod) - ((streampos + count - (4 - mod)) % 4)) / 4;
            for (int i = 0; i < iters; i++)
            {
                byte[] tempbyte = new byte[4];
                buffstream.Read(tempbyte, 0, 4);
                uint tempval = BitConverter.ToUInt32(tempbyte, 0);
                tempval ^= tpfxor;
                tempbyte = BitConverter.GetBytes(tempval);
                for (int j = 0; j < 4; j++)
                    buff[buffpos++] = tempbyte[j];
            }
            mod = 0;
            while (buffpos != count)
            {
                byte temp = (byte)buffstream.ReadByte();
                temp ^= (byte)tempxor;
                buff[buffpos++] = temp;
                tempxor >>= 8;
                mod++;
            }
            if (mod >= 4)
                throw new Exception();
            return buff;
        }
    }
}
