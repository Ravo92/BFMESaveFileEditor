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
    }
}
