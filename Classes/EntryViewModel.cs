using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BFMESaveFileEditor.Classes
{
    public sealed class EntryViewModel(Entry entry) : INotifyPropertyChanged
    {
        public Entry Model { get; private set; } = entry;

        public EntryType Type
        {
            get { return Model.Type; }
        }

        public string Label
        {
            get { return Model.Label; }
        }

        public int Offset
        {
            get { return Model.Offset; }
        }

        public int Size
        {
            get { return Model.Size; }
        }

        public string DisplayValue
        {
            get { return Model.DisplayValue; }
            set
            {
                if (Model.DisplayValue != value)
                {
                    Model.DisplayValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public void OnRefresh()
        {
            OnPropertyChanged(nameof(DisplayValue));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
