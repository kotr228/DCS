using CommunityToolkit.Mvvm.ComponentModel;
using JolieCat3D.Core.Geometry;

namespace JolieCat3D.UI.ViewModels
{
    /// <summary>
    /// Wraps one entry of a <see cref="Mesh.MaterialSlots"/> list for the Material
    /// Inspector's own "Material Slots" section - Multi-Material Support's UI half. Only
    /// ever rebuilt wholesale by <see cref="NodeViewModel.RefreshMaterialSlots"/> (the
    /// same "rebuilt, not incrementally patched" convention <see cref="NodeViewModel.Modifiers"/>
    /// already uses), so <see cref="SlotIndex"/> is fixed for this instance's own
    /// lifetime - a slot removed elsewhere in the list simply means this exact view
    /// model gets discarded and replaced by a fresh set reflecting the new numbering,
    /// never silently pointing at the wrong slot.
    /// </summary>
    public sealed partial class MaterialSlotViewModel : ObservableObject
    {
        private readonly Mesh _mesh;
        private readonly Action _onChanged;

        /// <summary>This slot's own position in <see cref="Mesh.MaterialSlots"/> - what
        /// <c>MainWindow</c>'s own "Assign to Selection"/"Remove" button handlers read
        /// directly off this view model's <c>DataContext</c> to call
        /// <c>Engine.Editing.MeshEditSession.AssignMaterialSlotToSelectedFace"/>/
        /// <see cref="Core.Geometry.Mesh.RemoveMaterialSlot"/> with.</summary>
        public int SlotIndex { get; }

        public MaterialSlotViewModel(Mesh mesh, int slotIndex, Action onChanged)
        {
            _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
            SlotIndex = slotIndex;
        }

        public string Name
        {
            get => _mesh.MaterialSlots[SlotIndex].Name;
            set
            {
                _mesh.MaterialSlots[SlotIndex].Name = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }
}
