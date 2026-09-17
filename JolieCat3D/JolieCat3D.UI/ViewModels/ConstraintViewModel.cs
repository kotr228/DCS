using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Constraints;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>
    /// Wraps one <see cref="Constraint"/> in a <see cref="NodeViewModel.Constraints"/>
    /// stack for the Constraints panel - the exact same "hand-written properties
    /// delegating to a wrapped Core model, rebuilt wholesale, WPF's own per-DataType
    /// DataTemplate selection" shape <see cref="ModifierViewModelBase"/> already
    /// established for <see cref="NodeViewModel.Modifiers"/>.
    /// </summary>
    public abstract class ConstraintViewModelBase : ObservableObject
    {
        private readonly Constraint _constraint;
        private readonly Action _onChanged;

        /// <summary>The wrapped constraint itself - <c>MainWindow</c> needs this to
        /// remove the right entry from <c>Node.Constraints</c> when the panel's own
        /// "Remove" button is clicked, the same role <see cref="ModifierViewModelBase.Underlying"/>
        /// plays for a modifier.</summary>
        public Constraint Underlying => _constraint;

        public string Name => _constraint.Name;

        public bool IsEnabled
        {
            get => _constraint.IsEnabled;
            set
            {
                if (_constraint.IsEnabled == value) return;
                _constraint.IsEnabled = value;
                OnPropertyChanged();
                _onChanged();
            }
        }

        protected ConstraintViewModelBase(Constraint constraint, Action onChanged)
        {
            _constraint = constraint ?? throw new ArgumentNullException(nameof(constraint));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        }

        protected void RaiseChanged() => _onChanged();
    }

    /// <summary>
    /// The Constraints panel's own view of a <see cref="TrackToConstraint"/> - needs a
    /// way to offer/resolve a REFERENCE to some other node in the scene
    /// (<see cref="TrackToConstraint.Target"/>), the exact same
    /// <c>allNodesProvider</c>-threaded-through-<see cref="NodeViewModel"/> shape
    /// <see cref="BooleanModifierViewModel"/> already established for its own Target.
    /// Unlike <see cref="BooleanModifierViewModel.AvailableTargets"/> (mesh-bearing
    /// nodes only, since only those have geometry a boolean op could combine with), ANY
    /// other node is a valid Track To target (a camera can track a mesh, a light, an
    /// empty pivot, ...), so this offers every other node with no such filter.
    /// </summary>
    public sealed class TrackToConstraintViewModel : ConstraintViewModelBase
    {
        private readonly TrackToConstraint _trackTo;
        private readonly NodeViewModel _owner;
        private readonly Func<IEnumerable<NodeViewModel>> _allNodesProvider;

        public TrackToConstraintViewModel(TrackToConstraint trackTo, Action onChanged, NodeViewModel owner, Func<IEnumerable<NodeViewModel>> allNodesProvider)
            : base(trackTo, onChanged)
        {
            _trackTo = trackTo;
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _allNodesProvider = allNodesProvider ?? throw new ArgumentNullException(nameof(allNodesProvider));
        }

        /// <summary>Every OTHER node currently in the scene this constraint could aim
        /// at - see this class's own remarks on why no mesh filter applies here, unlike
        /// <see cref="BooleanModifierViewModel.AvailableTargets"/>. This constraint's own
        /// owner node is excluded (aiming at yourself is a permanently-degenerate,
        /// zero-length-direction no-op per <see cref="TrackToConstraint.Apply"/>'s own
        /// remarks, never a useful choice). Re-evaluated every time it's READ, the same
        /// "always reflects the scene's current state" behavior
        /// <see cref="BooleanModifierViewModel.AvailableTargets"/> already has.</summary>
        public IEnumerable<NodeViewModel> AvailableTargets =>
            _allNodesProvider().Where(candidate => candidate != _owner);

        public NodeViewModel? SelectedTarget
        {
            get => _trackTo.Target is { } target ? _allNodesProvider().FirstOrDefault(candidate => candidate.UnderlyingNode == target) : null;
            set
            {
                if (SelectedTarget == value) return;
                _trackTo.Target = value?.UnderlyingNode;
                OnPropertyChanged();
                RaiseChanged();
            }
        }

        /// <summary>"PlusX"/"MinusX"/"PlusY"/"MinusY"/"PlusZ"/"MinusZ" - see
        /// <see cref="MirrorModifierViewModel.Axis"/>'s own remarks on why a plain
        /// string, not the <see cref="ConstraintAxis"/> enum itself.</summary>
        public string ForwardAxis
        {
            get => _trackTo.ForwardAxis.ToString();
            set
            {
                if (!Enum.TryParse<ConstraintAxis>(value, out var axis) || axis == _trackTo.ForwardAxis) return;
                _trackTo.ForwardAxis = axis;
                OnPropertyChanged();
                RaiseChanged();
            }
        }
    }
}
