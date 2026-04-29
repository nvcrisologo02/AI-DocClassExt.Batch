using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using DocumentIA.Batch.Models;
using DocumentIA.Batch.Services;
using DocumentIA.Batch.Views;
using Microsoft.Win32;

namespace DocumentIA.Batch.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    private string _backendUrl = string.Empty;
    private string _functionKey = string.Empty;
    private TipologiaOption? _selectedTipologia;
    private bool _promptingEnabled;
    private int _umbralConfianza;
    private int _numeroColas;
    private bool _ejecutarConAssetResolver;
    private bool _subirAGdc;
    private Dictionary<string, PromptOverride> _promptOverrides = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel() : this(new SettingsService())
    {
    }

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;

        Files = new ObservableCollection<BatchFileItem>();
        AvailableTipologias = new ObservableCollection<TipologiaOption>(
            new[]
            {
                new TipologiaOption { Code = "nota.simple.1_0" },
                new TipologiaOption { Code = "nota.simple.1_2" },
                new TipologiaOption { Code = "nota.simple.1_3" },
                new TipologiaOption { Code = "nota.simple.1_4" }
            });

        RemoveFileCommand = new RelayCommand(RemoveFile);
        PickFilesCommand = new RelayCommand(_ => PickFiles());
        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        EditPromptCommand = new RelayCommand(_ => EditPrompt(), _ => SelectedTipologia is not null);

        FilesView = CollectionViewSource.GetDefaultView(Files);

        LoadConfig();
    }

    public ObservableCollection<BatchFileItem> Files { get; }

    public ICollectionView FilesView { get; }

    public ObservableCollection<TipologiaOption> AvailableTipologias { get; }

    public RelayCommand RemoveFileCommand { get; }

    public RelayCommand PickFilesCommand { get; }

    public RelayCommand SaveConfigCommand { get; }

    public RelayCommand EditPromptCommand { get; }

    public string BackendUrl
    {
        get => _backendUrl;
        set => SetProperty(ref _backendUrl, value);
    }

    public string FunctionKey
    {
        get => _functionKey;
        set => SetProperty(ref _functionKey, value);
    }

    public TipologiaOption? SelectedTipologia
    {
        get => _selectedTipologia;
        set
        {
            if (SetProperty(ref _selectedTipologia, value))
            {
                EditPromptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPromptOverride => SelectedTipologia is not null &&
        _promptOverrides.ContainsKey(SelectedTipologia.Code);

    public bool PromptingEnabled
    {
        get => _promptingEnabled;
        set => SetProperty(ref _promptingEnabled, value);
    }

    public int UmbralConfianza
    {
        get => _umbralConfianza;
        set => SetProperty(ref _umbralConfianza, value);
    }

    public int NumeroColas
    {
        get => _numeroColas;
        set => SetProperty(ref _numeroColas, value);
    }

    public bool EjecutarConAssetResolver
    {
        get => _ejecutarConAssetResolver;
        set => SetProperty(ref _ejecutarConAssetResolver, value);
    }

    public bool SubirAGdc
    {
        get => _subirAGdc;
        set => SetProperty(ref _subirAGdc, value);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Files.Any(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileInfo = new FileInfo(path);
            Files.Add(new BatchFileItem
            {
                FileName = fileInfo.Name,
                FullPath = fileInfo.FullName,
                SizeBytes = fileInfo.Length,
                Estado = "Pendiente"
            });
        }
    }

    private void PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void RemoveFile(object? parameter)
    {
        if (parameter is BatchFileItem item)
        {
            Files.Remove(item);
        }
    }

    private void LoadConfig()
    {
        var config = _settingsService.Load();
        BackendUrl = config.BackendUrl;
        FunctionKey = config.FunctionKey;
        PromptingEnabled = config.PromptingEnabled;
        UmbralConfianza = config.UmbralConfianza;
        NumeroColas = config.NumeroColas;
        EjecutarConAssetResolver = config.EjecutarConAssetResolver;
        SubirAGdc = config.SubirAGdc;
        _promptOverrides = new Dictionary<string, PromptOverride>(config.PromptOverrides, StringComparer.OrdinalIgnoreCase);

        SelectedTipologia = AvailableTipologias.FirstOrDefault(x =>
            string.Equals(x.Code, config.SelectedTipologia, StringComparison.OrdinalIgnoreCase))
            ?? AvailableTipologias.Last();
        OnPropertyChanged(nameof(HasPromptOverride));
    }

    private void SaveConfig()
    {
        if (UmbralConfianza < 0 || UmbralConfianza > 100)
        {
            MessageBox.Show("El umbral debe estar entre 0 y 100.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NumeroColas < 1 || NumeroColas > 10)
        {
            MessageBox.Show("El número de colas debe estar entre 1 y 10.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var config = new BatchConfig
        {
            BackendUrl = BackendUrl,
            FunctionKey = FunctionKey,
            SelectedTipologia = SelectedTipologia?.Code ?? "nota.simple.1_4",
            PromptingEnabled = PromptingEnabled,
            UmbralConfianza = UmbralConfianza,
            NumeroColas = NumeroColas,
            EjecutarConAssetResolver = EjecutarConAssetResolver,
            SubirAGdc = SubirAGdc,
            PromptOverrides = new Dictionary<string, PromptOverride>(_promptOverrides, StringComparer.OrdinalIgnoreCase)
        };

        _settingsService.Save(config);
        MessageBox.Show("Configuración guardada correctamente.", "DocumentIA.Batch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void EditPrompt()
    {
        if (SelectedTipologia is null)
        {
            return;
        }

        _promptOverrides.TryGetValue(SelectedTipologia.Code, out var currentOverride);

        var dialog = new PromptEditorDialog(SelectedTipologia.Code, currentOverride)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.PromptOverride is null)
        {
            _promptOverrides.Remove(SelectedTipologia.Code);
        }
        else
        {
            _promptOverrides[SelectedTipologia.Code] = dialog.PromptOverride;
        }

        OnPropertyChanged(nameof(HasPromptOverride));
        SaveConfig();
    }
}
