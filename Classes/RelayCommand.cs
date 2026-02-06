using System.Windows.Input;

namespace BFMESaveFileEditor.Classes
{
    public sealed class RelayCommand : ICommand
    {
        private static readonly List<RelayCommand> _allCommands = new();

        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;

            _allCommands.Add(this);
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public event EventHandler? CanExecuteChanged;

        private void RaiseCanExecuteChangedInternal()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public static void RaiseCanExecuteChanged()
        {
            for (int i = 0; i < _allCommands.Count; i++)
            {
                _allCommands[i].RaiseCanExecuteChangedInternal();
            }
        }
    }
}
