using System.Text;

namespace BFMESaveFileEditor
{
    public enum EntryType
    {
        StringAsciiZ,
        StringAsciiLen8,
        StringUtf16LeLen8,
        UInt32,
        Int32,
        Byte,
        Unknown
    }

    public sealed class SaveGameFile(byte[] raw, List<Chunk> chunks)
    {
        public byte[] Raw { get; internal set; } = raw;
        public List<Chunk> Chunks { get; internal set; } = chunks;
    }

    public sealed class Chunk
    {
        public string Name { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public List<Entry> Entries { get; set; }

        public Chunk()
        {
            Name = "";
            Entries = [];
        }

        public override string ToString()
        {
            return Name + " @0x" + Offset.ToString("X") + " (" + Length + " bytes)";
        }
    }

    public sealed class Entry
    {
        public EntryType Type { get; set; }
        public string Label { get; set; }           // optional, e.g. "MapPath"
        public int Offset { get; set; }             // absolute offset in file
        public int Size { get; set; }               // bytes
        public string DisplayValue { get; set; }    // editable in UI
        public string? Owner { get; set; }
        public int OwnerIndex { get; set; }

        public Entry()
        {
            Label = "";
            DisplayValue = "";
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
    }
}
