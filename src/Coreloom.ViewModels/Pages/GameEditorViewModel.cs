using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Gaming;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>
/// The card for adding a game by hand, or changing or removing one added before: its name, its program, launch options
/// and a picture (the program's icon unless one is chosen). Saved games join the library like any other: Play, the
/// overlay, the frame limiter's per-game limits and adaptive pacing all work with them.
/// </summary>
public sealed class GameEditorViewModel : ObservableObject
{
    private readonly GamingViewModel _page;
    private readonly IGamingService _service;
    private CustomGame? _editing;
    private bool _isOpen;
    private string _name = string.Empty;
    private string _executable = string.Empty;
    private string _arguments = string.Empty;
    private string? _picture;
    private string? _preview;
    private string? _error;

    public GameEditorViewModel(GamingViewModel page, IGamingService service)
    {
        _page = page;
        _service = service;
        ChangeProgramCommand = new RelayCommand(ChangeProgram);
        ChoosePictureCommand = new RelayCommand(ChoosePicture);
        UseProgramIconCommand = new RelayCommand(() => Picture = null, () => Picture is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(Close);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => !IsNew);
    }

    public IRelayCommand ChangeProgramCommand { get; }

    public IRelayCommand ChoosePictureCommand { get; }

    public IRelayCommand UseProgramIconCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool IsNew => _editing is null;

    public string Title => IsNew ? "Add a game" : "Edit game";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                Error = null;
                UpdatePreview();
            }
        }
    }

    /// <summary>The game's program (full path).</summary>
    public string Executable
    {
        get => _executable;
        private set
        {
            if (SetProperty(ref _executable, value))
            {
                UpdatePreview();
            }
        }
    }

    /// <summary>Launch options (optional), e.g. "-windowed".</summary>
    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value ?? string.Empty);
    }

    /// <summary>A chosen picture, or null for the program's icon.</summary>
    public string? Picture
    {
        get => _picture;
        private set
        {
            if (SetProperty(ref _picture, value))
            {
                OnPropertyChanged(nameof(HasChosenPicture));
                UseProgramIconCommand.NotifyCanExecuteChanged();
                UpdatePreview();
            }
        }
    }

    public bool HasChosenPicture => Picture is not null;

    /// <summary>The picture shown in the card: the chosen one, else the program's icon.</summary>
    public string? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => Preview is not null;

    public string Initials => GameLibrary.Initials(string.IsNullOrWhiteSpace(Name) ? "?" : Name);

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Picks a program, then opens the card with a suggested name (nothing happens when the pick is cancelled).</summary>
    public void Add()
    {
        if (_service.PickProgram() is not { } exe)
        {
            return;
        }

        _editing = null;
        _arguments = string.Empty;
        _picture = null;
        _executable = exe;
        _name = Suggest(exe);
        Error = Problem(exe);
        Refresh();
        IsOpen = true;
    }

    /// <summary>Opens the card for a game added before.</summary>
    public void Edit(CustomGame game)
    {
        _editing = game;
        _name = game.Name;
        _executable = game.Executable;
        _arguments = game.Arguments ?? string.Empty;
        _picture = game.Picture;
        Error = null;
        Refresh();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Error = null;
    }

    private void ChangeProgram()
    {
        string? folder = CustomGames.DirectoryOf(Executable);
        if (_service.PickProgram(folder) is not { } exe)
        {
            return;
        }

        bool renamed = string.IsNullOrWhiteSpace(Name) || Name == Suggest(Executable);
        Executable = exe;
        if (renamed)
        {
            Name = Suggest(exe);
        }

        Error = Problem(exe);
    }

    private void ChoosePicture()
    {
        if (_service.PickPicture() is { } picture)
        {
            Picture = CustomGames.IsPicture(picture) ? picture : null;
            Error = Picture is null ? "Choose a .png, .jpg, .bmp or .ico picture." : null;
        }
    }

    private async Task SaveAsync()
    {
        string name = Name.Trim();
        if (name.Length == 0)
        {
            Error = "Give the game a name.";
            return;
        }

        if (Problem(Executable) is { } problem)
        {
            Error = problem;
            return;
        }

        var now = _page.Now();
        var game = new CustomGame
        {
            Id = _editing?.Id ?? CustomGames.NewId(Executable, now),
            Name = name,
            Executable = Executable,
            Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim(),
            Picture = Picture,
            Added = _editing?.Added ?? now,
            LastPlayed = _editing?.LastPlayed,
        };
        Close();
        await _page.SaveCustomGameAsync(game);
    }

    private async Task RemoveAsync()
    {
        if (_editing is not { } game)
        {
            return;
        }

        Close();
        await _page.RemoveCustomGameAsync(game);
    }

    private string? Problem(string exe) =>
        !CustomGames.IsProgram(exe) ? "Pick the game's program (an .exe file)."
        : _service.DescribeProgram(exe) is null ? "That program can't be found. Pick it again."
        : null;

    private string Suggest(string exe)
    {
        var info = _service.DescribeProgram(exe);
        return CustomGames.SuggestName(exe, info?.ProductName, info?.Description);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Executable));
        OnPropertyChanged(nameof(Arguments));
        OnPropertyChanged(nameof(Picture));
        OnPropertyChanged(nameof(HasChosenPicture));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Title));
        UseProgramIconCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        OnPropertyChanged(nameof(Initials));
        if (Picture is not null)
        {
            Preview = Picture;
            return;
        }

        if (!CustomGames.IsProgram(Executable))
        {
            Preview = null;
            return;
        }

        var entry = CustomGames.ToEntry(new CustomGame { Id = CustomGames.IdPrefix + "preview", Name = Name, Executable = Executable });
        var art = _service.AddLocalPictures([entry]).FirstOrDefault()?.Artwork;
        Preview = art?.Cover ?? art?.Banner ?? art?.Icon;
    }
}
