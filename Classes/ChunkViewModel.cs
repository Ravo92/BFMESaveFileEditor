using System.Collections.ObjectModel;

namespace BFMESaveFileEditor.Classes
{
    public sealed class ChunkViewModel
    {
        public Chunk Model { get; private set; }

        public ObservableCollection<EntryViewModel> Entries { get; private set; }

        public string DisplayName
        {
            get
            {
                string name = Model.Name;

                string title;
                if (ChunkNameMap.ChunkNameDictionary.TryGetValue(name, out string? friendlyName) &&
                    !string.IsNullOrWhiteSpace(friendlyName))
                {
                    title = friendlyName;
                }
                else
                {
                    title = name;
                }

                return title + " (" + Model.Entries.Count + " entries)";
            }
        }

        public ChunkViewModel(Chunk chunk)
        {
            Model = chunk;
            Entries = [];

            for (int i = 0; i < chunk.Entries.Count; i++)
            {
                Entries.Add(new EntryViewModel(chunk.Entries[i]));
            }
        }
    }
}
