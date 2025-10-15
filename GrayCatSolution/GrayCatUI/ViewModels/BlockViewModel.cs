using GrayCat.Shared.Models;

namespace GrayCat.UI.ViewModels
{
    public class BlockViewModel : BaseViewModel
    {
        private BlockModel _model;
        public BlockModel Model => _model;

        public BlockViewModel(BlockModel model)
        {
            _model = model;
        }

        public System.Guid Id => _model.Id;
        public string Type => _model.Type;

        public double PositionX
        {
            get => _model.PositionX;
            set
            {
                _model.PositionX = value;
                OnPropertyChanged();
            }
        }

        public double PositionY
        {
            get => _model.PositionY;
            set
            {
                _model.PositionY = value;
                OnPropertyChanged();
            }
        }

        public string Content
        {
            get => _model.Content;
            set
            {
                _model.Content = value;
                OnPropertyChanged();
            }
        }
    }
}
