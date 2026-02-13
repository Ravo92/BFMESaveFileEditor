using System.ComponentModel;

namespace BFMESaveFileEditor.Classes
{
    public sealed class EntryPropertyViewModel(string label, string originalValue, EntryViewModel source, bool isNew) : INotifyPropertyChanged
    {
        private string _displayValue = originalValue;
        private bool _isModified = false;

        public string Label { get; } = label;
        public string OriginalValue { get; } = originalValue;
        public EntryViewModel Source { get; } = source;
        public bool IsNew { get; } = isNew;

        public string DisplayValue
        {
            get { return _displayValue; }
            set
            {
                if (string.Equals(_displayValue, value, StringComparison.Ordinal))
                {
                    return;
                }

                _displayValue = value;
                OnPropertyChanged(nameof(DisplayValue));

                bool newIsModified = !string.Equals(_displayValue, OriginalValue, StringComparison.Ordinal);
                if (_isModified != newIsModified)
                {
                    _isModified = newIsModified;
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        public bool IsModified
        {
            get { return _isModified; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}