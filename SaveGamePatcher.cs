using System.Text;

namespace BFMESaveFileEditor
{
    class SaveGamePatcher
    {
        public static void PatchAscii(byte[] raw, int offset, int allocatedSize, string? newValue)
        {
            ArgumentNullException.ThrowIfNull(raw);

            newValue ??= "";

            byte[] bytes = Encoding.ASCII.GetBytes(newValue);

            if (bytes.Length > allocatedSize)
            {
                throw new InvalidOperationException($"New string is too long. Max length: {allocatedSize} bytes.");
            }

            Span<byte> target = raw.AsSpan(offset, allocatedSize);

            target.Clear();
            bytes.CopyTo(target);
        }

        public static int InsertAsciiZ(ref SaveGameFile file, int insertOffset, string? value)
        {
            ArgumentNullException.ThrowIfNull(file);

            value ??= "";

            byte[] payload = BuildAsciiZ(value);
            int delta = payload.Length;

            if (insertOffset < 0 || insertOffset > file.Raw.Length)
            {
                throw new InvalidOperationException("Insert offset is out of range.");
            }

            byte[] newRaw = new byte[file.Raw.Length + delta];

            Buffer.BlockCopy(file.Raw, 0, newRaw, 0, insertOffset);
            Buffer.BlockCopy(payload, 0, newRaw, insertOffset, delta);
            Buffer.BlockCopy(file.Raw, insertOffset, newRaw, insertOffset + delta, file.Raw.Length - insertOffset);

            file.Raw = newRaw;

            FixupOffsetsAfterInsert(file, insertOffset, delta);

            return insertOffset;
        }

        private static byte[] BuildAsciiZ(string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            byte[] result = new byte[bytes.Length + 1];

            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            result[^1] = 0x00;

            return result;
        }

        private static void FixupOffsetsAfterInsert(SaveGameFile file, int insertOffset, int delta)
        {
            for (int c = 0; c < file.Chunks.Count; c++)
            {
                Chunk chunk = file.Chunks[c];

                if (chunk.Offset >= insertOffset && chunk.Offset >= 0)
                {
                    chunk.Offset += delta;
                }

                for (int i = 0; i < chunk.Entries.Count; i++)
                {
                    Entry e = chunk.Entries[i];
                    if (e.Offset >= insertOffset && e.Offset >= 0)
                    {
                        e.Offset += delta;
                    }
                }
            }
        }
    }
}