using System.Windows;
using System.Windows.Controls;
using JolieCat.Shared.Enums;
using SkiaSharp;

namespace JolieCat.UI.Views
{
    /// <summary>
    /// File &gt; New: canvas size (with presets), background fill and project type
    /// (see <see cref="ProjectType"/>). Results are read from the properties below once
    /// <see cref="Window.DialogResult"/> is true.
    /// </summary>
    public partial class NewProjectDialog : Window
    {
        /// <summary>Largest side accepted - beyond this a single RGBA layer is over 1 GB.</summary>
        public const int MaxSide = 16000;

        private bool _applyingPreset;

        public ProjectType SelectedProjectType { get; private set; } = ProjectType.StandardImage;

        public int DocumentWidth { get; private set; } = 1600;

        public int DocumentHeight { get; private set; } = 1200;

        /// <summary>Background layer fill; null = transparent.</summary>
        public SKColor? BackgroundFill { get; private set; } = SKColors.White;

        public NewProjectDialog()
        {
            InitializeComponent();

            // The text boxes' initial values fire TextChanged during InitializeComponent,
            // which flips the preset to "Custom" - select the default preset afterwards.
            PresetBox.SelectedIndex = 1;
        }

        private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WidthBox is null || PresetBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

            var parts = tag.Split('x');
            _applyingPreset = true;
            WidthBox.Text = parts[0];
            HeightBox.Text = parts[1];
            _applyingPreset = false;
        }

        /// <summary>Typing a size by hand switches the preset to "Custom".</summary>
        private void Size_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_applyingPreset && PresetBox is not null) PresetBox.SelectedIndex = 0;
            if (ErrorText is not null) ErrorText.Text = string.Empty;
        }

        private void Swap_Click(object sender, RoutedEventArgs e) =>
            (WidthBox.Text, HeightBox.Text) = (HeightBox.Text, WidthBox.Text);

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(WidthBox.Text.Trim(), out var width) || !int.TryParse(HeightBox.Text.Trim(), out var height) ||
                width < 1 || height < 1 || width > MaxSide || height > MaxSide)
            {
                ErrorText.Text = $"Width and height must be whole numbers from 1 to {MaxSide}.";
                return;
            }

            DocumentWidth = width;
            DocumentHeight = height;
            BackgroundFill = BackgroundBox.SelectedIndex switch
            {
                1 => SKColors.Black,
                2 => null,
                _ => SKColors.White,
            };

            SelectedProjectType = SpriteSheetOption.IsChecked == true ? ProjectType.SpriteSheet
                : ClipbarAnimationOption.IsChecked == true ? ProjectType.ClipbarAnimation
                : ProjectType.StandardImage;

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
