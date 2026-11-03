using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JolieCat.Core.Effects;
using JolieCat.UI.Media;
using SkiaSharp;

namespace JolieCat.UI.ViewModels
{
    /// <summary>One of the user's patterns in the manager.</summary>
    public partial class PatternItemViewModel : ObservableObject
    {
        public PatternItemViewModel(string name, BitmapSource thumbnail)
        {
            this.name = name;
            editName = name;
            Thumbnail = thumbnail;
        }

        [ObservableProperty] private string name;

        /// <summary>The name box's text (applied by Rename).</summary>
        [ObservableProperty] private string editName;

        public BitmapSource Thumbnail { get; }
    }

    /// <summary>
    /// Edit > Manage Patterns: the user's own patterns (from Define Pattern) with
    /// previews; rename or delete them. <see cref="Renamed"/> lets the app update
    /// layer styles that use a renamed pattern.
    /// </summary>
    public partial class PatternManagerViewModel : ObservableObject
    {
        public PatternManagerViewModel()
        {
            PatternLibrary.EnsureLoaded();
            foreach (var name in PatternTiles.Names.Where(PatternTiles.IsCustom))
                Patterns.Add(Item(name));
            selected = Patterns.FirstOrDefault();
        }

        public ObservableCollection<PatternItemViewModel> Patterns { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RenameCommand), nameof(DeleteCommand))]
        private PatternItemViewModel? selected;

        [ObservableProperty]
        private string message = string.Empty;

        public bool IsEmpty => Patterns.Count == 0;

        /// <summary>Raised after a rename, with the old and new names.</summary>
        public event Action<string, string>? Renamed;

        private static PatternItemViewModel Item(string name)
        {
            using var image = PatternTiles.Create(name, SKColors.Black);
            return new PatternItemViewModel(name, SkiaImageConverter.ToThumbnail(image, 64));
        }

        private bool HasSelection() => Selected is not null;

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Rename()
        {
            if (Selected is not { } item) return;
            var newName = item.EditName.Trim();
            if (PatternLibrary.Rename(item.Name, newName) is { } error)
            {
                Message = error;
                return;
            }
            var oldName = item.Name;
            item.Name = newName;
            item.EditName = newName;
            Message = $"Renamed to \"{newName}\".";
            if (oldName != newName) Renamed?.Invoke(oldName, newName);
        }

        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void Delete()
        {
            if (Selected is not { } item) return;
            var index = Patterns.IndexOf(item);
            PatternLibrary.Delete(item.Name);
            Patterns.Remove(item);
            Selected = Patterns.Count == 0 ? null : Patterns[Math.Min(index, Patterns.Count - 1)];
            Message = $"Deleted \"{item.Name}\". Layer styles that used it now show Checker.";
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
