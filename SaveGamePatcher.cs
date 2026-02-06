using System.Text;

namespace BFMESaveFileEditor
{
    class SaveGamePatcher
    {
        public static void PatchAsciiZ(byte[] raw, int offset, int allocatedSize, string newValue)
        {
            ArgumentNullException.ThrowIfNull(raw);
            newValue ??= "";

            byte[] bytes = Encoding.ASCII.GetBytes(newValue);

            // allocatedSize beinhaltet terminierenden Nullbyte Platz (Entry.Size)
            int maxPayload = Math.Max(0, allocatedSize - 1);

            if (bytes.Length > maxPayload)
            {
                throw new InvalidOperationException("New string is too long. Max length: " + maxPayload + " chars.");
            }

            // clear
            for (int i = 0; i < allocatedSize; i++)
            {
                raw[offset + i] = 0;
            }

            // write
            for (int i = 0; i < bytes.Length; i++)
            {
                raw[offset + i] = bytes[i];
            }
        }
    }
}
