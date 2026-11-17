namespace GrayCat.UI.Localization;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

/// <summary>{l:Tr 'Run App'} — a text that follows the interface language.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    // Bound to Loc.Lang with the key in a converter: an indexer path "[key]" would split texts at commas
    // (WPF reads "[a, b]" as two indexer arguments), leaving any text with a comma empty.
    public override object ProvideValue(IServiceProvider sp) =>
        new Binding(nameof(Loc.Lang)) { Source = Loc.Instance, Mode = BindingMode.OneWay, Converter = new KeyConverter(Key) }.ProvideValue(sp);

    private sealed class KeyConverter(string key) : IValueConverter
    {
        public object Convert(object value, Type t, object? p, CultureInfo c) => Loc.T(key);
        public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

/// <summary>{l:TrFormat 'Blocks: {0}', Path=Blocks.Count} — a bound value inside a translated format text.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrFormatExtension : MarkupExtension
{
    public TrFormatExtension() { }
    public TrFormatExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public string? Path { get; set; }

    public override object ProvideValue(IServiceProvider sp)
    {
        // The value binding plus Loc.Lang, so the text is rebuilt when the language changes.
        var mb = new MultiBinding { Converter = new FormatConverter(Key), Mode = BindingMode.OneWay };
        mb.Bindings.Add(string.IsNullOrEmpty(Path) ? new Binding() : new Binding(Path));
        mb.Bindings.Add(new Binding(nameof(Loc.Lang)) { Source = Loc.Instance });
        return mb.ProvideValue(sp);
    }

    private sealed class FormatConverter(string key) : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object? p, CultureInfo c) =>
            Loc.F(key, values.Length > 0 ? values[0] : null);

        public object[] ConvertBack(object v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

/// <summary>{l:TrBlock Path=Type} — a block type shown by its name in the current language.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrBlockExtension : MarkupExtension
{
    public string? Path { get; set; }

    public override object ProvideValue(IServiceProvider sp)
    {
        var mb = new MultiBinding { Converter = new BlockConverter(), Mode = BindingMode.OneWay };
        mb.Bindings.Add(string.IsNullOrEmpty(Path) ? new Binding() : new Binding(Path));
        mb.Bindings.Add(new Binding(nameof(Loc.Lang)) { Source = Loc.Instance });
        return mb.ProvideValue(sp);
    }

    private sealed class BlockConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object? p, CultureInfo c) => Loc.Block(values.Length > 0 ? values[0]?.ToString() : null);
        public object[] ConvertBack(object v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}

/// <summary>Shows an option value translated (combo boxes in the properties panel).</summary>
public sealed class TrValueConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) => Loc.Value(value?.ToString());
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Shows a value (e.g. a template name) translated; for ItemTemplates.</summary>
public sealed class TrConverter : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) => Loc.T(value?.ToString() ?? string.Empty);
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
