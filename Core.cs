using System.Text;

namespace BFMESaveFileEditor
{
    public enum EntryType
    {
        StringAsciiZ,
        UInt32,
        Int32,
        Unknown
    }

    public sealed class SaveGameFile(byte[] raw, List<Chunk> chunks)
    {
        public byte[] Raw { get; private set; } = raw;
        public List<Chunk> Chunks { get; private set; } = chunks;
    }

    public sealed class Chunk
    {
        public string Name { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public List<Entry> Entries { get; set; }

        public Chunk()
        {
            this.Name = "";
            this.Entries = [];
        }

        public override string ToString()
        {
            return this.Name + " @0x" + this.Offset.ToString("X") + " (" + this.Length + " bytes)";
        }
    }

    public sealed class Entry
    {
        public EntryType Type { get; set; }
        public string Label { get; set; }           // optional, z.B. "MapPath"
        public int Offset { get; set; }             // absolute offset in file
        public int Size { get; set; }               // bytes
        public string DisplayValue { get; set; }    // editierbar im UI
        public string RawHexPreview { get; set; }   // quick debug
        public string? Owner { get; set; }
        public int OwnerIndex { get; set; }

        public Entry()
        {
            this.Label = "";
            this.DisplayValue = "";
            this.RawHexPreview = "";
        }
    }

    public static class BinaryUtil
    {
        public static bool StartsWithAscii(byte[] data, int offset, string ascii)
        {
            if (offset < 0) return false;
            if (offset + ascii.Length > data.Length) return false;

            for (int i = 0; i < ascii.Length; i++)
            {
                if (data[offset + i] != (byte)ascii[i]) return false;
            }
            return true;
        }

        public static string ReadAsciiZ(byte[] data, int offset, int maxLen)
        {
            int end = offset;
            int limit = Math.Min(data.Length, offset + maxLen);

            while (end < limit && data[end] != 0)
            {
                end++;
            }

            int len = end - offset;
            if (len <= 0) return "";

            return Encoding.ASCII.GetString(data, offset, len);
        }

        public static int FindAscii(byte[] data, string needle, int startOffset)
        {
            byte[] n = Encoding.ASCII.GetBytes(needle);
            for (int i = startOffset; i <= data.Length - n.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < n.Length; j++)
                {
                    if (data[i + j] != n[j])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return i;
            }
            return -1;
        }

        public static string HexPreview(byte[] data, int offset, int count)
        {
            int len = Math.Min(count, data.Length - offset);
            if (len <= 0) return "";

            StringBuilder sb = new(len * 3);
            for (int i = 0; i < len; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[offset + i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
