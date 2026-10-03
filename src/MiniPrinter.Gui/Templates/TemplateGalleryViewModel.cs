using System.Collections.ObjectModel;
using MiniPrinter.Control;

namespace MiniPrinter.Gui;

/// <summary>Where the pictures of the templates come from (the service draws them).</summary>
public interface IThumbnailSource
{
    /// <summary>The PNG of a template at the given width, or null when there is none.</summary>
    Task<byte[]?> GetAsync(string name, int width);
}

public enum ThumbnailState
{
    Pending,
    Loading,
    Ready,
    Failed,
}

/// <summary>A card of the gallery: the title, where the template comes from and its picture (a marker until it arrives).</summary>
public sealed class TemplateCardViewModel : ObservableObject
{
    private TemplateDto _template;
    private byte[]? _thumbnail;
    private ThumbnailState _state;

    public TemplateCardViewModel(TemplateDto template)
    {
        _template = template;
    }

    public string Name => _template.Name;

    public string Title => _template.Title;

    public string Description => _template.Description;

    /// <summary>"Integrada", "Propia" or "Sustituye a la integrada".</summary>
    public string SourceText => _template.Source switch
    {
        "user" => Strings.Get("Templates.Source.User"),
        "override" => Strings.Get("Templates.Source.Override"),
        _ => Strings.Get("Templates.Source.BuiltIn"),
    };

    public byte[]? Thumbnail
    {
        get => _thumbnail;
        private set => Set(ref _thumbnail, value);
    }

    public ThumbnailState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
                OnPropertyChanged(nameof(Marker));
        }
    }

    /// <summary>What the card shows while it has no picture.</summary>
    public string Marker => State switch
    {
        ThumbnailState.Failed => Strings.Get("Templates.NoThumbnail"),
        ThumbnailState.Ready => "",
        _ => "…",
    };

    internal void Update(TemplateDto template)
    {
        if (template == _template)
            return;
        _template = template;
        OnPropertyChanged(string.Empty);
    }

    internal void MarkLoading() => State = ThumbnailState.Loading;

    internal void SetThumbnail(byte[]? png)
    {
        Thumbnail = png;
        State = png is null ? ThumbnailState.Failed : ThumbnailState.Ready;
    }

    internal void Forget()
    {
        Thumbnail = null;
        State = ThumbnailState.Pending;
    }
}

/// <summary>
/// The gallery of templates: cards with their picture, a search box, the recently used first, and the choice between gallery and
/// list (which is remembered). The pictures are asked for in the background, two at a time, so the tab opens at once.
/// </summary>
public sealed class TemplateGalleryViewModel : ObservableObject
{
    public const int ThumbnailWidth = 160;
    public const int MaxConcurrentThumbnails = 2;

    private readonly IThumbnailSource _thumbnails;
    private readonly List<TemplateCardViewModel> _all = [];
    private IReadOnlyList<string> _recent;
    private string _search = "";
    private TemplatesViewChoice _view;
    private string? _selectedName;

    public TemplateGalleryViewModel(IThumbnailSource thumbnails, TemplatesViewChoice view = TemplatesViewChoice.Gallery, IReadOnlyList<string>? recent = null)
    {
        _thumbnails = thumbnails;
        _view = view;
        _recent = recent ?? [];
        ShowGallery = new RelayCommand(() => View = TemplatesViewChoice.Gallery);
        ShowList = new RelayCommand(() => View = TemplatesViewChoice.List);
    }

    public RelayCommand ShowGallery { get; }

    public RelayCommand ShowList { get; }

    /// <summary>The cards that match the search, the recently used first.</summary>
    public ObservableCollection<TemplateCardViewModel> Cards { get; } = [];

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
                Rebuild();
        }
    }

    public TemplatesViewChoice View
    {
        get => _view;
        set
        {
            if (!Set(ref _view, value))
                return;
            OnPropertyChanged(nameof(IsGallery));
            OnPropertyChanged(nameof(IsList));
            ViewChanged?.Invoke(value);
        }
    }

    public bool IsGallery
    {
        get => _view == TemplatesViewChoice.Gallery;
        set => View = value ? TemplatesViewChoice.Gallery : TemplatesViewChoice.List;
    }

    public bool IsList
    {
        get => _view == TemplatesViewChoice.List;
        set => IsGallery = !value;
    }

    /// <summary>Raised when the user switches between gallery and list (to remember it).</summary>
    public event Action<TemplatesViewChoice>? ViewChanged;

    /// <summary>The template chosen (by its name).</summary>
    public string? SelectedName
    {
        get => _selectedName;
        set
        {
            if (!Set(ref _selectedName, value))
                return;
            OnPropertyChanged(nameof(SelectedCard));
            Chosen?.Invoke(value);
        }
    }

    /// <summary>Raised when the user picks a card.</summary>
    public event Action<string?>? Chosen;

    public TemplateCardViewModel? SelectedCard
    {
        get => Cards.FirstOrDefault(c => c.Name == _selectedName);
        set
        {
            if (value is not null)
                SelectedName = value.Name;
        }
    }

    public bool HasNoMatches => Cards.Count == 0 && _all.Count > 0;

    /// <summary>Takes the templates the service has now. Cards of templates that stay are reused (with their picture).</summary>
    public void SetTemplates(IReadOnlyList<TemplateDto> templates)
    {
        var byName = _all.ToDictionary(c => c.Name);
        _all.Clear();
        foreach (var template in templates)
        {
            if (byName.TryGetValue(template.Name, out var card))
                card.Update(template);
            else
                card = new TemplateCardViewModel(template);
            _all.Add(card);
        }
        Rebuild();
    }

    /// <summary>The templates used last, the most recent first.</summary>
    public void SetRecent(IReadOnlyList<string> recent)
    {
        _recent = recent;
        Rebuild();
    }

    private void Rebuild()
    {
        var query = SettingsSectionViewModel.Normalize(_search);
        bool Matches(TemplateCardViewModel c) =>
            query.Length == 0 || SettingsSectionViewModel.Normalize(c.Title + " " + c.Description).Contains(query, StringComparison.Ordinal);

        var rank = _recent.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var ordered = _all.Where(Matches)
            .Select((card, position) => (card, position))
            .OrderBy(x => rank.TryGetValue(x.card.Name, out var r) ? r : int.MaxValue)
            .ThenBy(x => x.position)
            .Select(x => x.card)
            .ToList();

        if (ordered.SequenceEqual(Cards))
            return;
        Cards.Clear();
        foreach (var card in ordered)
            Cards.Add(card);
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(SelectedCard));
    }

    /// <summary>Forgets the picture of a template (it changed or was saved) so that it is asked for again.</summary>
    public void Invalidate(string name)
    {
        _all.FirstOrDefault(c => c.Name == name)?.Forget();
    }

    /// <summary>Asks for the pictures that are missing, at most two at a time. Completes when all of them have been asked for.</summary>
    public async Task LoadThumbnailsAsync()
    {
        using var gate = new SemaphoreSlim(MaxConcurrentThumbnails);
        var pending = _all.Where(c => c.State == ThumbnailState.Pending).ToList();
        foreach (var card in pending)
            card.MarkLoading();
        await Task.WhenAll(pending.Select(async card =>
        {
            await gate.WaitAsync();
            try
            {
                card.SetThumbnail(await _thumbnails.GetAsync(card.Name, ThumbnailWidth));
            }
            catch (Exception)
            {
                card.SetThumbnail(null);
            }
            finally
            {
                gate.Release();
            }
        }));
    }
}
