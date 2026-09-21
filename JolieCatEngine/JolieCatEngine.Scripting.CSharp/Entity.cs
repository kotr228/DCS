using System.ComponentModel;

namespace JolieCatEngine.Scripting.CSharp
{
    public class Entity : INotifyPropertyChanged
    {
        private string _name;

        public Entity(string name)
        {
            _name = name;
            Transform = new TransformComponent();
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        public TransformComponent Transform { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
