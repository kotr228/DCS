using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Modifiers;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>
    /// Wraps one <see cref="Modifier"/> in a <see cref="NodeViewModel.Modifiers"/> stack
    /// for the Modifiers panel - the same "hand-written properties delegating to a
    /// wrapped Core model" convention <see cref="NodeViewModel"/> itself uses for
    /// <c>Node</c>. Concrete subclasses (<see cref="MirrorModifierViewModel"/>,
    /// <see cref="SubdivisionSurfaceModifierViewModel"/>) add whichever properties are
    /// specific to their own modifier type - WPF's implicit per-<c>DataType</c>
    /// <c>DataTemplate</c> selection (the same mechanism the Scene Outliner's own
    /// <c>HierarchicalDataTemplate</c> already uses) picks the right one for each item
    /// in an <c>ItemsControl</c> bound to a mixed <see cref="ModifierViewModelBase"/>
    /// collection with no manual type-switching in code.
    /// </summary>
    public abstract class ModifierViewModelBase : ObservableObject
    {
        private readonly Modifier _modifier;
        private readonly Action _onChanged;

        /// <summary>The wrapped modifier itself - <c>MainWindow</c> needs this to
        /// remove the right entry from <c>Node.Modifiers</c> when the panel's own
        /// "Remove" button is clicked (an <c>ItemsControl</c> item's own DataContext IS
        /// this view model, not the underlying <see cref="Modifier"/>).</summary>
        public Modifier Underlying => _modifier;

        public string Name => _modifier.Name;

        public bool IsEnabled
        {
            get => _modifier.IsEnabled;
            set
            {
                if (_modifier.IsEnabled == value) return;
                _modifier.IsEnabled = value;
                OnPropertyChanged();
                _onChanged();
            }
        }

        protected ModifierViewModelBase(Modifier modifier, Action onChanged)
        {
            _modifier = modifier ?? throw new ArgumentNullException(nameof(modifier));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        }

        /// <summary>Invokes the same "something changed" callback <see cref="IsEnabled"/>'s
        /// own setter uses - a derived class's own property setters (Mirror's Axis,
        /// Subsurf's Iterations) call this after applying their change, so every
        /// modifier-panel edit re-renders the viewport the same way.</summary>
        protected void RaiseChanged() => _onChanged();
    }

    public sealed class MirrorModifierViewModel : ModifierViewModelBase
    {
        private readonly MirrorModifier _mirror;

        public MirrorModifierViewModel(MirrorModifier mirror, Action onChanged) : base(mirror, onChanged) =>
            _mirror = mirror;

        /// <summary>"X"/"Y"/"Z" - a string, not the <see cref="MirrorAxis"/> enum
        /// itself, so a plain <c>ComboBox</c> with hardcoded <c>ComboBoxItem</c>
        /// Content strings (<c>SelectedValuePath="Content"</c>) can bind directly with
        /// no enum-to-string converter needed.</summary>
        public string Axis
        {
            get => _mirror.Axis.ToString();
            set
            {
                if (!Enum.TryParse<MirrorAxis>(value, out var axis) || axis == _mirror.Axis) return;
                _mirror.Axis = axis;
                OnPropertyChanged();
                RaiseChanged();
            }
        }
    }

    public sealed class SubdivisionSurfaceModifierViewModel : ModifierViewModelBase
    {
        private readonly SubdivisionSurfaceModifier _subsurf;

        public SubdivisionSurfaceModifierViewModel(SubdivisionSurfaceModifier subsurf, Action onChanged) : base(subsurf, onChanged) =>
            _subsurf = subsurf;

        /// <summary>0-4 - see <see cref="SubdivisionSurfaceModifier.Iterations"/>'s own
        /// remarks on the clamp and why.</summary>
        public int Iterations
        {
            get => _subsurf.Iterations;
            set
            {
                if (_subsurf.Iterations == value) return;
                _subsurf.Iterations = value;
                OnPropertyChanged();
                RaiseChanged();
            }
        }
    }
}
