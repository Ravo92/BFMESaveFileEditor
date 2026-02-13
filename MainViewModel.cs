using BFMESaveFileEditor.Classes;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BFMESaveFileEditor
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private SaveGameFile? _file;
        private string _status;

        private ChunkViewModel? _selectedChunk;
        private EntryViewModel? _selectedEntry;

        private readonly ObservableCollection<EntryPropertyViewModel> _selectedEntryProperties;
        private readonly ObservableCollection<EntryViewModel> _visibleChunkEntries;

        public ObservableCollection<ChunkViewModel> Chunks { get; }

        public ICommand OpenCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand ApplyEditCommand { get; }
        public ICommand AddUpgradeCommand { get; }

        public MainViewModel()
        {
            Chunks = [];
            _selectedEntryProperties = [];
            _visibleChunkEntries = [];

            _status = "Ready";

            OpenCommand = new RelayCommand(OpenFile);
            SaveAsCommand = new RelayCommand(SaveAsFile, CanSave);
            ApplyEditCommand = new RelayCommand(ApplyEdit, CanApplyEdit);
            AddUpgradeCommand = new RelayCommand(AddUpgrade, CanAddUpgrade);
        }

        public ChunkViewModel? SelectedChunk
        {
            get { return _selectedChunk; }
            set
            {
                if (!ReferenceEquals(_selectedChunk, value))
                {
                    _selectedChunk = value;
                    RebuildVisibleChunkEntries();
                    OnPropertyChanged(nameof(SelectedChunkEntries));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedChunkEntries));

                    SelectedEntry = null;
                }
            }
        }

        public EntryViewModel? SelectedEntry
        {
            get { return _selectedEntry; }
            set
            {
                if (!ReferenceEquals(_selectedEntry, value))
                {
                    _selectedEntry = value;
                    OnPropertyChanged();

                    RebuildRightProperties();

                    RelayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<EntryPropertyViewModel> SelectedEntryProperties
        {
            get { return _selectedEntryProperties; }
        }

        public static string[] AvailableUpgrades
        {
            get { return UpgradeCatalog.ObjectLevelUpgrades; }
        }

        private void RebuildRightProperties()
        {
            foreach (EntryPropertyViewModel prop in _selectedEntryProperties)
            {
                prop.PropertyChanged -= EntryProperty_PropertyChanged;
            }

            _selectedEntryProperties.Clear();

            if (_selectedEntry == null || _selectedChunk == null)
            {
                RelayCommand.RaiseCanExecuteChanged();
                return;
            }

            if (IsPropertiesGridSuppressed(_selectedChunk.Model.Name))
            {
                RelayCommand.RaiseCanExecuteChanged();
                return;
            }

            ObservableCollection<EntryViewModel> sourceProps = GetRawPropertiesForSelectedEntry();
            for (int i = 0; i < sourceProps.Count; i++)
            {
                EntryViewModel entry = sourceProps[i];
                EntryPropertyViewModel vm = new(entry.Label, entry.DisplayValue, entry, false);

                vm.PropertyChanged += EntryProperty_PropertyChanged;
                _selectedEntryProperties.Add(vm);
            }

            RelayCommand.RaiseCanExecuteChanged();
        }

        private static bool IsPropertiesGridSuppressed(string chunkName)
        {
            if (string.IsNullOrWhiteSpace(chunkName))
            {
                return false;
            }

            if (string.Equals(chunkName, "GLOBAL_SCIENCES", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(chunkName, "CHUNK_GameStateKOLB", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void RebuildVisibleChunkEntries()
        {
            _visibleChunkEntries.Clear();

            if (_selectedChunk == null)
            {
                return;
            }

            bool isHeroesChunk = IsCampaignHeroesChunk(_selectedChunk.Model.Name);

            if (!isHeroesChunk)
            {
                for (int i = 0; i < _selectedChunk.Entries.Count; i++)
                {
                    _visibleChunkEntries.Add(_selectedChunk.Entries[i]);
                }

                return;
            }

            // Heroes-Chunk: links nur Owner/Hero-Root-Einträge anzeigen
            for (int i = 0; i < _selectedChunk.Entries.Count; i++)
            {
                EntryViewModel e = _selectedChunk.Entries[i];
                if (string.Equals(e.Label, "Hero", StringComparison.OrdinalIgnoreCase))
                {
                    _visibleChunkEntries.Add(e);
                }
            }
        }

        private static bool IsCampaignHeroesChunk(string chunkName)
        {
            if (string.IsNullOrWhiteSpace(chunkName))
            {
                return false;
            }

            return chunkName.StartsWith("CHUNK_CampaignKOLB", StringComparison.OrdinalIgnoreCase);
        }


        private void EntryProperty_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EntryPropertyViewModel.IsModified))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        private void OpenFile()
        {
            OpenFileDialog dlg = new()
            {
                Filter = "BFME2 Savegame (*.BfME2Campaign)|*.BfME2Campaign"
            };

            bool? ok = dlg.ShowDialog();
            if (ok != true)
            {
                return;
            }

            byte[] raw = File.ReadAllBytes(dlg.FileName);
            _file = SaveGameParser.Parse(raw);

            Chunks.Clear();
            for (int i = 0; i < _file.Chunks.Count; i++)
            {
                Chunks.Add(new ChunkViewModel(_file.Chunks[i]));
            }

            SelectedChunk = Chunks.Count > 0 ? Chunks[0] : null;
            Status = "Loaded: " + Path.GetFileName(dlg.FileName);
        }

        private bool CanSave()
        {
            return _file != null && _file.Raw != null && _file.Raw.Length > 0;
        }

        private void SaveAsFile()
        {
            if (_file?.Raw == null)
            {
                Status = "Error: No file loaded.";
                return;
            }

            SaveFileDialog dlg = new()
            {
                Filter = "BFME2 Savegame (*.BfME2Campaign)|*.BfME2Campaign",
                FileName = "Edited.BfME2Campaign"
            };

            bool? ok = dlg.ShowDialog();
            if (ok != true)
            {
                return;
            }

            File.WriteAllBytes(dlg.FileName, _file.Raw);
            Status = "Saved: " + Path.GetFileName(dlg.FileName);
        }

        private bool CanApplyEdit()
        {
            if (_file == null)
            {
                return false;
            }

            for (int i = 0; i < _selectedEntryProperties.Count; i++)
            {
                EntryPropertyViewModel p = _selectedEntryProperties[i];
                if (p.IsNew || p.IsModified)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyEdit()
        {
            if (_file == null)
            {
                return;
            }

            try
            {
                // First: insert new upgrades so offsets become stable
                for (int i = 0; i < _selectedEntryProperties.Count; i++)
                {
                    EntryPropertyViewModel prop = _selectedEntryProperties[i];
                    if (!prop.IsNew)
                    {
                        continue;
                    }

                    Entry model = prop.Source.Model;

                    if (_selectedChunk == null || _selectedEntry == null)
                    {
                        continue;
                    }

                    if (!IsCampaignHeroesChunk(_selectedChunk.Model.Name))
                    {
                        continue;
                    }

                    if (!string.Equals(_selectedEntry.Label, "Hero", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int insertOffset = GetInsertionOffsetForHero(_selectedChunk.Model, _selectedEntry.DisplayValue);

                    int writtenOffset = SaveGamePatcher.InsertAsciiZ(ref _file, insertOffset, prop.DisplayValue);
                    model.Offset = writtenOffset;
                    model.Size = prop.DisplayValue.Length + 1;
                    model.DisplayValue = prop.DisplayValue;
                }

                // Second: patch existing strings in-place
                for (int i = 0; i < _selectedEntryProperties.Count; i++)
                {
                    EntryPropertyViewModel prop = _selectedEntryProperties[i];
                    if (prop.IsNew)
                    {
                        continue;
                    }

                    if (!prop.IsModified)
                    {
                        continue;
                    }

                    Entry model = prop.Source.Model;
                    SaveGamePatcher.PatchAscii(_file.Raw, model.Offset, model.Size, prop.DisplayValue);

                    model.DisplayValue = prop.DisplayValue;
                    prop.Source.OnRefresh();
                }

                RebuildRightProperties();
                Status = "Changes applied.";
            }
            catch (Exception ex)
            {
                Status = "Patch failed: " + ex.Message;
            }
        }

        private static int GetInsertionOffsetForHero(Chunk chunk, string heroName)
        {
            // Insert after the last entry belonging to the hero.
            // If no upgrades exist yet, insert right after the hero token itself.

            int bestEnd = -1;

            for (int i = 0; i < chunk.Entries.Count; i++)
            {
                Entry e = chunk.Entries[i];

                if (string.Equals(e.Label, "Hero", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.DisplayValue, heroName, StringComparison.OrdinalIgnoreCase))
                {
                    int end = e.Offset + Math.Max(e.Size, 0);
                    if (end > bestEnd)
                    {
                        bestEnd = end;
                    }
                }

                if (string.Equals(e.Owner, heroName, StringComparison.OrdinalIgnoreCase))
                {
                    int end = e.Offset + Math.Max(e.Size, 0);
                    if (end > bestEnd)
                    {
                        bestEnd = end;
                    }
                }
            }

            if (bestEnd < 0)
            {
                // Fallback: insert at chunk start if nothing matches (should not happen)
                return chunk.Offset;
            }

            return bestEnd;
        }

        public string Status
        {
            get { return _status; }
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<EntryViewModel> GetRawPropertiesForSelectedEntry()
        {
            ObservableCollection<EntryViewModel> result = [];

            if (_selectedChunk == null || _selectedEntry == null)
            {
                return result;
            }

            bool isHeroesChunk = IsCampaignHeroesChunk(_selectedChunk.Model.Name);

            if (isHeroesChunk)
            {
                // Rechts nur Upgrades/Properties, die dem selektierten Hero gehören
                if (!string.Equals(_selectedEntry.Label, "Hero", StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                string heroName = _selectedEntry.DisplayValue;

                for (int i = 0; i < _selectedChunk.Entries.Count; i++)
                {
                    EntryViewModel e = _selectedChunk.Entries[i];

                    if (string.Equals(e.Model.Owner, heroName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(e);
                    }
                }

                return result;
            }

            // Default: alles außer selected
            for (int i = 0; i < _selectedChunk.Entries.Count; i++)
            {
                EntryViewModel e = _selectedChunk.Entries[i];

                if (ReferenceEquals(e, _selectedEntry))
                {
                    continue;
                }

                result.Add(e);
            }

            return result;
        }

        public ObservableCollection<EntryViewModel> SelectedChunkEntries
        {
            get { return _visibleChunkEntries; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool CanAddUpgrade()
        {
            if (_file == null || _selectedChunk == null || _selectedEntry == null)
            {
                return false;
            }

            if (!IsCampaignHeroesChunk(_selectedChunk.Model.Name))
            {
                return false;
            }

            return string.Equals(_selectedEntry.Label, "Hero", StringComparison.OrdinalIgnoreCase);
        }

        private void AddUpgrade()
        {
            if (_file == null || _selectedChunk == null || _selectedEntry == null)
            {
                return;
            }

            if (!IsCampaignHeroesChunk(_selectedChunk.Model.Name))
            {
                return;
            }

            if (!string.Equals(_selectedEntry.Label, "Hero", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string heroName = _selectedEntry.DisplayValue;

            Entry newModel = new()
            {
                Type = EntryType.StringAsciiZ,
                Label = "Upgrade",
                Offset = -1,              // -1 indicates "not written to file yet"
                Size = 0,                 // will be set after insertion
                DisplayValue = "",        // start empty
                Owner = heroName,
                OwnerIndex = _selectedEntry.Model.OwnerIndex
            };

            EntryViewModel newVm = new(newModel);

            _selectedChunk.Model.Entries.Add(newModel);
            _selectedChunk.Entries.Add(newVm);

            // OriginalValue empty + DisplayValue empty
            EntryPropertyViewModel prop = new(newVm.Label, "", newVm, true);
            prop.PropertyChanged += EntryProperty_PropertyChanged;
            _selectedEntryProperties.Add(prop);

            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}