using System.Globalization;
using System.Runtime.CompilerServices;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>A setting shown in a section: its name, its label and the words that find it in the search box.</summary>
public sealed record SettingsField(string Name, string LabelKey, string TermsKey);

/// <summary>
/// One section of the Settings window (Printing, Paper, Network…). It holds the values being edited, knows whether they differ
/// from the ones last loaded (<see cref="IsDirty"/>), checks them (<see cref="Errors"/>) and writes only its own settings into
/// the settings it is given, so that sections saved one at a time never overwrite each other.
/// </summary>
public abstract class SettingsSectionViewModel : ObservableObject
{
    private string[] _baseline = [];
    private bool _syncing;
    private bool _isDirty;
    private IReadOnlyDictionary<string, string> _errors = new Dictionary<string, string>();
    private string _query = "";

    protected SettingsSectionViewModel(string key, string titleKey, IReadOnlyList<SettingsField> fields, IReadOnlyList<string> covers)
    {
        Key = key;
        TitleKey = titleKey;
        Fields = fields;
        Covers = covers;
    }

    /// <summary>Stable name of the section (remembered between runs).</summary>
    public string Key { get; }

    public string TitleKey { get; }

    public string Title => Strings.Get(TitleKey);

    public IReadOnlyList<SettingsField> Fields { get; }

    /// <summary>The properties of <see cref="ServiceSettings"/> this section edits (each one belongs to exactly one section).</summary>
    public IReadOnlyList<string> Covers { get; }

    /// <summary>Sections that apply at once and have nothing to save (Appearance, General).</summary>
    public virtual bool IsInstant => false;

    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    public IReadOnlyDictionary<string, string> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    /// <summary>The message under a field, or empty.</summary>
    public string ErrorOf(string field) => _errors.GetValueOrDefault(field, "");

    /// <summary>The same as <see cref="ErrorOf"/>, for bindings: <c>{Binding [Port]}</c>.</summary>
    public string this[string field] => ErrorOf(field);

    /// <summary>The text typed in the search box, which the view uses to highlight matching settings.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value))
                OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>Whether a setting of this section matches the search (always false without a search).</summary>
    public bool Highlights(string field) =>
        _query.Length > 0 && Fields.FirstOrDefault(f => f.Name == field) is { } found && Matches(found, _query);

    internal bool MatchesSearch(string query) =>
        Normalize(Title).Contains(Normalize(query), StringComparison.Ordinal) || Fields.Any(f => Matches(f, query));

    private static bool Matches(SettingsField field, string query) =>
        Normalize(Strings.Get(field.LabelKey) + " " + Strings.Get(field.TermsKey)).Contains(Normalize(query), StringComparison.Ordinal);

    /// <summary>Lower case without accents, so that "bateria" finds "Batería".</summary>
    internal static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
    }

    // ---- what each section provides ----------------------------------------------------------------------------

    /// <summary>The values of this section as they are in <paramref name="settings"/>, in the same form as <see cref="Snapshot"/>.</summary>
    protected abstract string[] Values(ServiceSettings settings);

    /// <summary>The values being edited.</summary>
    protected abstract string[] Snapshot();

    /// <summary>Puts the settings into the fields.</summary>
    protected abstract void Read(ServiceSettings settings);

    /// <summary>The settings with this section's fields replaced by the ones being edited (only called when there are no errors).</summary>
    public abstract ServiceSettings Write(ServiceSettings settings);

    /// <summary>Reports the problems of the values being edited, one per field.</summary>
    protected abstract void Check(Action<string, string> report);

    // ---- state -------------------------------------------------------------------------------------------------

    /// <summary>Shows <paramref name="settings"/> and takes them as the saved state.</summary>
    public void Load(ServiceSettings settings)
    {
        _syncing = true;
        try
        {
            Read(settings);
        }
        finally
        {
            _syncing = false;
        }
        Saved = settings;
        _baseline = Values(settings);
        Refresh();
    }

    /// <summary>Goes back to the values last loaded.</summary>
    public void Discard()
    {
        if (Saved is { } settings)
            Load(settings);
    }

    /// <summary>The settings this section last loaded (null before the first load).</summary>
    public ServiceSettings? Saved { get; private set; }

    /// <summary>Whether this section's settings are different in the two given settings.</summary>
    public bool DiffersBetween(ServiceSettings a, ServiceSettings b) => !Values(a).SequenceEqual(Values(b));

    protected void Touch()
    {
        if (!_syncing)
            Refresh();
    }

    private void Refresh()
    {
        var errors = new Dictionary<string, string>();
        Check((field, message) => errors.TryAdd(field, message));
        _errors = errors;
        IsDirty = !Snapshot().SequenceEqual(_baseline);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Sets a field and, when it changed, updates the state of the section.</summary>
    protected bool Field<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name))
            return false;
        Touch();
        return true;
    }

    // ---- helpers for numbers typed in a box -----------------------------------------------------------------------

    protected static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    protected static bool TryNumber(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    /// <summary>Reports "not a number" or "out of range" for a numeric box.</summary>
    protected static void CheckNumber(Action<string, string> report, string field, string text, int min, int max)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            report(field, Strings.Get("Settings.Error.Number"));
        else if (value < min || value > max)
            report(field, Strings.Get("Settings.Error.Range", min, max));
    }
}
