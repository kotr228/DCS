using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace AsmodayCat.UI.Localization;

// {l:Tr chat.title} — a text that follows the interface language
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider sp) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(sp);
}

// {l:TrFormat fmt.mb, Path=RamFreeMb} — a bound value inside a translated format string ("{0:N0} МБ")
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrFormatExtension : MarkupExtension
{
    public TrFormatExtension() { }
    public TrFormatExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public string? Path { get; set; }

    public override object ProvideValue(IServiceProvider sp) =>
        LangBound.Create(Path, new FormatConverter(Key)).ProvideValue(sp);

    private sealed class FormatConverter(string key) : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object? p, CultureInfo c) =>
            Loc.F(key, values.Length > 0 ? values[0] : null);
        public object[] ConvertBack(object v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

// {l:TrLabel Path=Status} — an enum/status value shown as a word in the current language
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrLabelExtension : MarkupExtension
{
    public TrLabelExtension() { }
    public TrLabelExtension(string path) => Path = path;

    [ConstructorArgument("path")]
    public string? Path { get; set; }

    public override object ProvideValue(IServiceProvider sp) =>
        LangBound.Create(Path, new LabelConverter()).ProvideValue(sp);

    private sealed class LabelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object? p, CultureInfo c) =>
            Loc.Label(values.Length > 0 ? values[0] : null);
        public object[] ConvertBack(object v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

internal static class LangBound
{
    // The value binding plus Loc.Lang, so the text is rebuilt when the language changes
    public static MultiBinding Create(string? path, IMultiValueConverter converter)
    {
        var mb = new MultiBinding { Converter = converter, Mode = BindingMode.OneWay };
        mb.Bindings.Add(string.IsNullOrEmpty(path) ? new Binding() : new Binding(path));
        mb.Bindings.Add(new Binding(nameof(Loc.Lang)) { Source = Loc.Instance });
        return mb;
    }
}
