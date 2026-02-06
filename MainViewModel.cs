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

        public ObservableCollection<ChunkViewModel> Chunks { get; }

        public ICommand OpenCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand ApplyEditCommand { get; }

        public MainViewModel()
        {
            Chunks = [];
            _selectedEntryProperties = [];

            _status = "Ready";

            OpenCommand = new RelayCommand(OpenFile);
            SaveAsCommand = new RelayCommand(SaveAsFile, CanSave);
            ApplyEditCommand = new RelayCommand(ApplyEdit, CanApplyEdit);
        }

        public ChunkViewModel? SelectedChunk
        {
            get { return _selectedChunk; }
            set
            {
                if (!ReferenceEquals(_selectedChunk, value))
                {
                    _selectedChunk = value;
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
                EntryPropertyViewModel vm = new(entry.Label, entry.DisplayValue, entry);

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
                if (_selectedEntryProperties[i].IsModified)
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
                for (int i = 0; i < _selectedEntryProperties.Count; i++)
                {
                    EntryPropertyViewModel prop = _selectedEntryProperties[i];
                    if (!prop.IsModified)
                    {
                        continue;
                    }

                    Entry model = prop.Source.Model;
                    SaveGamePatcher.PatchAsciiZ(
                        _file.Raw,
                        model.Offset,
                        model.Size,
                        prop.DisplayValue);

                    model.DisplayValue = prop.DisplayValue;
                    model.RawHexPreview =
                        BinaryUtil.HexPreview(_file.Raw, model.Offset, Math.Min(32, model.Size));

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
            get
            {
                return _selectedChunk != null ? _selectedChunk.Entries : [];
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}