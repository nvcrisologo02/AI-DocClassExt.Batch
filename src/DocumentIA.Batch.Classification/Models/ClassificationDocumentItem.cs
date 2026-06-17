using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace DocumentIA.Batch.Classification.Models;

public class ClassificationDocumentItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _status = "Pendiente";
    private string _identificacionDocumento = string.Empty;
    private string _tipologiaIdentificada = string.Empty;
    private string _confianzaGlobal = string.Empty;
    private string _mensajeError = string.Empty;
    private string _resultadoEstado = string.Empty;
    private string _tdn1 = string.Empty;
    private string _tdn2 = string.Empty;
    private string _matricula = string.Empty;
    private string _classificationOnlyOutput = string.Empty;
    private string _clasificador = string.Empty;
    private string _fallbackLlm = string.Empty;
    private string _fallbackRazon = string.Empty;
    private string _resumen = string.Empty;
    private string _recorteAplicado = string.Empty;
    private string _paginasIncluidas = string.Empty;
    private string _markdownGenerado = string.Empty;
    private string _origenMarkdown = string.Empty;
    private string _modeloLlmUsado = string.Empty;
    private string _justificacionClasificacion = string.Empty;
    private string _statusQueryUri = string.Empty;
    private string _identificacionGuid = string.Empty;
    private string _tipologiaFamilia = string.Empty;
    private string _tipologiaVersion = string.Empty;
    private string _fechaProceso = string.Empty;
    private string _paginas = string.Empty;
    private string _tipologiaNombre = string.Empty;
    private string _tipologiaMgdcMatricula = string.Empty;
    private string _gdcTipoDocumento = string.Empty;
    private string _gdcSubtipoDocumento = string.Empty;
    private string _gdcSerie = string.Empty;
    private string _gptDescripcion = string.Empty;
    private string _actividadActual = string.Empty;
    private string _actividadesCompletadas = string.Empty;
    private string _actividadesTotales = string.Empty;
    private string _duracionTotalMs = string.Empty;
    private string _timelineActividades = string.Empty;
    private bool _reutilizadaPorDuplicado;
    private string _proveedor = string.Empty;
    private string _motivoDescarte = string.Empty;
    private string _mensajeReutilizacion = string.Empty;
    private ObservableCollection<PropuestaProveedor> _detalleProveedores = new();

    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string RuntimeStatus { get; set; } = string.Empty;
    public string OutputJsonPath { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string IdentificacionDocumento
    {
        get => _identificacionDocumento;
        set => SetField(ref _identificacionDocumento, value);
    }

    public string TipologiaIdentificada
    {
        get => _tipologiaIdentificada;
        set => SetField(ref _tipologiaIdentificada, value);
    }

    public string ConfianzaGlobal
    {
        get => _confianzaGlobal;
        set
        {
            if (SetField(ref _confianzaGlobal, value))
            {
                OnPropertyChanged(nameof(ConfidenceDisplay));
            }
        }
    }

    public string MensajeError
    {
        get => _mensajeError;
        set => SetField(ref _mensajeError, value);
    }

    public string ResultadoEstado
    {
        get => _resultadoEstado;
        set => SetField(ref _resultadoEstado, value);
    }

    public string Tdn1
    {
        get => _tdn1;
        set => SetField(ref _tdn1, value);
    }

    public string Tdn2
    {
        get => _tdn2;
        set => SetField(ref _tdn2, value);
    }

    public string Matricula
    {
        get => _matricula;
        set => SetField(ref _matricula, value);
    }

    public string ClassificationOnlyOutput
    {
        get => _classificationOnlyOutput;
        set => SetField(ref _classificationOnlyOutput, value);
    }

    public string Clasificador
    {
        get => _clasificador;
        set => SetField(ref _clasificador, value);
    }

    public string FallbackLlm
    {
        get => _fallbackLlm;
        set => SetField(ref _fallbackLlm, value);
    }

    public string FallbackRazon
    {
        get => _fallbackRazon;
        set => SetField(ref _fallbackRazon, value);
    }

    public string Resumen
    {
        get => _resumen;
        set => SetField(ref _resumen, value);
    }

    public string RecorteAplicado
    {
        get => _recorteAplicado;
        set => SetField(ref _recorteAplicado, value);
    }

    public string PaginasIncluidas
    {
        get => _paginasIncluidas;
        set => SetField(ref _paginasIncluidas, value);
    }

    public string MarkdownGenerado
    {
        get => _markdownGenerado;
        set => SetField(ref _markdownGenerado, value);
    }

    public string OrigenMarkdown
    {
        get => _origenMarkdown;
        set => SetField(ref _origenMarkdown, value);
    }

    public string ModeloLlmUsado
    {
        get => _modeloLlmUsado;
        set => SetField(ref _modeloLlmUsado, value);
    }

    public string JustificacionClasificacion
    {
        get => _justificacionClasificacion;
        set => SetField(ref _justificacionClasificacion, value);
    }

    public string StatusQueryUri
    {
        get => _statusQueryUri;
        set => SetField(ref _statusQueryUri, value);
    }

    public string IdentificacionGuid
    {
        get => _identificacionGuid;
        set => SetField(ref _identificacionGuid, value);
    }

    public string TipologiaFamilia
    {
        get => _tipologiaFamilia;
        set => SetField(ref _tipologiaFamilia, value);
    }

    public string TipologiaVersion
    {
        get => _tipologiaVersion;
        set => SetField(ref _tipologiaVersion, value);
    }

    public string FechaProceso
    {
        get => _fechaProceso;
        set => SetField(ref _fechaProceso, value);
    }

    public string Paginas
    {
        get => _paginas;
        set => SetField(ref _paginas, value);
    }

    public string TipologiaNombre
    {
        get => _tipologiaNombre;
        set => SetField(ref _tipologiaNombre, value);
    }

    public string TipologiaMgdcMatricula
    {
        get => _tipologiaMgdcMatricula;
        set => SetField(ref _tipologiaMgdcMatricula, value);
    }

    public string GdcTipoDocumento
    {
        get => _gdcTipoDocumento;
        set => SetField(ref _gdcTipoDocumento, value);
    }

    public string GdcSubtipoDocumento
    {
        get => _gdcSubtipoDocumento;
        set => SetField(ref _gdcSubtipoDocumento, value);
    }

    public string GdcSerie
    {
        get => _gdcSerie;
        set => SetField(ref _gdcSerie, value);
    }

    public string GptDescripcion
    {
        get => _gptDescripcion;
        set => SetField(ref _gptDescripcion, value);
    }

    public string ActividadActual
    {
        get => _actividadActual;
        set => SetField(ref _actividadActual, value);
    }

    public string ActividadesCompletadas
    {
        get => _actividadesCompletadas;
        set => SetField(ref _actividadesCompletadas, value);
    }

    public string ActividadesTotales
    {
        get => _actividadesTotales;
        set => SetField(ref _actividadesTotales, value);
    }

    public string DuracionTotalMs
    {
        get => _duracionTotalMs;
        set => SetField(ref _duracionTotalMs, value);
    }

    public string TimelineActividades
    {
        get => _timelineActividades;
        set => SetField(ref _timelineActividades, value);
    }

    public bool ReutilizadaPorDuplicado
    {
        get => _reutilizadaPorDuplicado;
        set
        {
            if (SetField(ref _reutilizadaPorDuplicado, value))
            {
                OnPropertyChanged(nameof(OrigenResultadoDisplay));
            }
        }
    }

    public string MensajeReutilizacion
    {
        get => _mensajeReutilizacion;
        set => SetField(ref _mensajeReutilizacion, value);
    }

    public string Proveedor
    {
        get => _proveedor;
        set => SetField(ref _proveedor, value);
    }

    public string MotivoDescarte
    {
        get => _motivoDescarte;
        set => SetField(ref _motivoDescarte, value);
    }

    public string OrigenResultadoDisplay => ReutilizadaPorDuplicado
        ? "Reutilizado"
        : "Procesado en esta ejecución";

    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }

    public string DurationDisplay => (FechaInicio.HasValue && FechaFin.HasValue)
        ? (FechaFin.Value - FechaInicio.Value).ToString(@"mm\:ss")
        : string.Empty;

    public string ConfidenceDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConfianzaGlobal))
            {
                return string.Empty;
            }

            if (!double.TryParse(ConfianzaGlobal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !double.TryParse(ConfianzaGlobal, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return ConfianzaGlobal;
            }

            if (value > 1d && value <= 100d)
            {
                value /= 100d;
            }

            return value.ToString("P1", CultureInfo.CurrentCulture);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public ObservableCollection<PropuestaProveedor> DetalleProveedores
    {
        get => _detalleProveedores;
        set => SetField(ref _detalleProveedores, value);
    }
}

public class PropuestaProveedor
{
    public string Proveedor { get; set; } = string.Empty;
    public string Tipologia { get; set; } = string.Empty;
    public double Confianza { get; set; }
    public string MotivoDescarte { get; set; } = string.Empty;
}