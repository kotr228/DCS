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

    /// <summary>
    /// The Modifiers panel's own view of a <see cref="BooleanModifier"/> - unlike
    /// <see cref="MirrorModifierViewModel"/>/<see cref="SubdivisionSurfaceModifierViewModel"/>
    /// (whose settings are all self-contained), this one also needs a way to offer/resolve
    /// a REFERENCE to some other node in the scene (<see cref="BooleanModifier.Target"/>) -
    /// its own constructor's <c>allNodesProvider</c> (threaded down from
    /// <see cref="SceneViewModel"/> via <see cref="NodeViewModel"/>) is what makes that
    /// possible without this class needing a direct reference to the whole scene/view-model
    /// tree itself.
    /// </summary>
    public sealed class BooleanModifierViewModel : ModifierViewModelBase
    {
        private readonly BooleanModifier _boolean;
        private readonly NodeViewModel _owner;
        private readonly Func<IEnumerable<NodeViewModel>> _allNodesProvider;

        public BooleanModifierViewModel(BooleanModifier boolean, Action onChanged, NodeViewModel owner, Func<IEnumerable<NodeViewModel>> allNodesProvider)
            : base(boolean, onChanged)
        {
            _boolean = boolean;
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _allNodesProvider = allNodesProvider ?? throw new ArgumentNullException(nameof(allNodesProvider));
        }

        /// <summary>"Union"/"Difference"/"Intersection" - see
        /// <see cref="MirrorModifierViewModel.Axis"/>'s own remarks on why a plain string,
        /// not the <see cref="BooleanOperation"/> enum itself.</summary>
        public string Operation
        {
            get => _boolean.Operation.ToString();
            set
            {
                if (!Enum.TryParse<BooleanOperation>(value, out var operation) || operation == _boolean.Operation) return;
                _boolean.Operation = operation;
                OnPropertyChanged();
                RaiseChanged();
            }
        }

        /// <summary>Every OTHER mesh-bearing node currently in the scene this modifier
        /// could combine with - a mesh-less node (a camera, a light, a pure pivot) has no
        /// geometry <see cref="JolieCat3D.Core.Geometry.CsgSolid.Combine"/> could ever do anything with,
        /// and this modifier's own owner node is excluded too (targeting yourself is
        /// well-defined - see <see cref="BooleanModifier.Apply"/>'s own remarks on reading
        /// <see cref="Core.Scene.Node.Mesh"/>, not the evaluated result, so there's no
        /// actual recursion risk - but it's never a useful choice, so the picker doesn't
        /// offer it). Re-evaluated every time it's READ (an <c>IEnumerable</c>, not a
        /// snapshot list), so a node added/renamed/removed elsewhere in the scene since
        /// this panel was last shown is always reflected the next time the ComboBox's own
        /// dropdown opens.</summary>
        public IEnumerable<NodeViewModel> AvailableTargets =>
            _allNodesProvider().Where(candidate => candidate != _owner && candidate.UnderlyingNode.Mesh is not null);

        /// <summary>The <see cref="NodeViewModel"/> wrapping <see cref="BooleanModifier.Target"/> -
        /// null with no target picked yet (or if the previously-picked target node was
        /// since removed from the scene entirely, in which case <see cref="BooleanModifier.Apply"/>
        /// already tolerates the dangling reference as "nothing to combine with" - see its
        /// own remarks). Setting this to null (nothing selected in the ComboBox) clears
        /// <see cref="BooleanModifier.Target"/> the same way.</summary>
        public NodeViewModel? SelectedTarget
        {
            get => _boolean.Target is { } target ? _allNodesProvider().FirstOrDefault(candidate => candidate.UnderlyingNode == target) : null;
            set
            {
                if (SelectedTarget == value) return;
                _boolean.Target = value?.UnderlyingNode;
                OnPropertyChanged();
                RaiseChanged();
            }
        }
    }
}
