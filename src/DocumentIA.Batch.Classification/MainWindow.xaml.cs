using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using DocumentIA.Batch.Views;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.ViewModels;

namespace DocumentIA.Batch.Classification;

public partial class MainWindow : Window
{
    private readonly ClassificationMainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new ClassificationMainViewModel();
        DataContext = _viewModel;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        _viewModel.AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is not ClassificationDocumentItem item)
        {
            return;
        }

        var outputText = BuildOutputText(item);
        var dialog = new BatchOutputDialog(item.FileName, outputText)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private static string BuildOutputText(ClassificationDocumentItem item)
    {
        var summary = $$"""
        ==== SUMMARY ====
        FileName: {{item.FileName}}
        Status: {{item.Status}}
        RuntimeStatus: {{item.RuntimeStatus}}
        StatusQueryUri: {{item.StatusQueryUri}}
        OrigenResultado: {{item.OrigenResultadoDisplay}}
        MensajeReutilizacion: {{item.MensajeReutilizacion}}
        ResultadoEstado: {{item.ResultadoEstado}}
        DocumentId: {{item.IdentificacionDocumento}}
        Guid: {{item.IdentificacionGuid}}
        Typology: {{item.TipologiaIdentificada}}
        TipologiaFamilia: {{item.TipologiaFamilia}}
        TipologiaVersion: {{item.TipologiaVersion}}
        FechaProceso: {{item.FechaProceso}}
        Paginas: {{item.Paginas}}
        Tdn1: {{item.Tdn1}}
        Tdn2: {{item.Tdn2}}
        Matricula: {{item.Matricula}}
        TipologiaNombre: {{item.TipologiaNombre}}
        TipologiaMGDCMatricula: {{item.TipologiaMgdcMatricula}}
        GdcTipoDocumento: {{item.GdcTipoDocumento}}
        GdcSubtipoDocumento: {{item.GdcSubtipoDocumento}}
        GdcSerie: {{item.GdcSerie}}
        GptDescripcion: {{item.GptDescripcion}}
        ClassificationOnly: {{item.ClassificationOnlyOutput}}
        Clasificador: {{item.Clasificador}}
        Confidence: {{item.ConfidenceDisplay}} (raw: {{item.ConfianzaGlobal}})
        FallbackLLM: {{item.FallbackLlm}}
        FallbackRazon: {{item.FallbackRazon}}
        RecorteAplicado: {{item.RecorteAplicado}}
        PaginasIncluidas: {{item.PaginasIncluidas}}
        MarkdownGenerado: {{item.MarkdownGenerado}}
        OrigenMarkdown: {{item.OrigenMarkdown}}
        ModeloLLMUsado: {{item.ModeloLlmUsado}}
        ActividadActual: {{item.ActividadActual}}
        ActividadesCompletadas: {{item.ActividadesCompletadas}}
        ActividadesTotales: {{item.ActividadesTotales}}
        DuracionTotalMs: {{item.DuracionTotalMs}}
        Error: {{item.MensajeError}}
        OutputJsonPath: {{item.OutputJsonPath}}

        ==== JUSTIFICACION ====
        {{item.JustificacionClasificacion}}

        ==== TIMELINE ====
        {{item.TimelineActividades}}
        """;

        if (!string.IsNullOrWhiteSpace(item.OutputJsonPath) && File.Exists(item.OutputJsonPath))
        {
            var rawJson = File.ReadAllText(item.OutputJsonPath);
            return $"{summary}{Environment.NewLine}{Environment.NewLine}==== RAW JSON ===={Environment.NewLine}{rawJson}";
        }

        return summary;
    }
}