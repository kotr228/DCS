using System.Windows;

namespace JolieCat.UI
{
    /// <summary>
    /// Message boxes in the interface language. Being in the JolieCat.UI namespace, this
    /// is the MessageBox every <c>MessageBox.Show(...)</c> in the app's code reaches, so
    /// each message and title goes through <see cref="Localization.Loc.T"/> (texts built
    /// with values in them stay English unless the dictionary has them).
    /// </summary>
    public static class MessageBox
    {
        private static string T(string? text) => Localization.Loc.T(text ?? string.Empty);

        public static MessageBoxResult Show(string text) => System.Windows.MessageBox.Show(T(text));

        public static MessageBoxResult Show(string text, string caption) => System.Windows.MessageBox.Show(T(text), T(caption));

        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button) =>
            System.Windows.MessageBox.Show(T(text), T(caption), button);

        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon) =>
            System.Windows.MessageBox.Show(T(text), T(caption), button, icon);

        public static MessageBoxResult Show(string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
            System.Windows.MessageBox.Show(T(text), T(caption), button, icon, defaultResult);

        public static MessageBoxResult Show(Window owner, string text) => System.Windows.MessageBox.Show(owner, T(text));

        public static MessageBoxResult Show(Window owner, string text, string caption) => System.Windows.MessageBox.Show(owner, T(text), T(caption));

        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button) =>
            System.Windows.MessageBox.Show(owner, T(text), T(caption), button);

        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon) =>
            System.Windows.MessageBox.Show(owner, T(text), T(caption), button, icon);

        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
            System.Windows.MessageBox.Show(owner, T(text), T(caption), button, icon, defaultResult);
    }
}
