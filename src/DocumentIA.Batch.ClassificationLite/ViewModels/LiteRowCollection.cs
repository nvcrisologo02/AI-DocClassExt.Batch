using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DocumentIA.Batch.ClassificationLite.ViewModels;

/// <summary>
/// Coleccion de filas de la rejilla que sabe aplicar altas y bajas en lote.
/// </summary>
public sealed class LiteRowCollection : ObservableCollection<LiteDocumentRow>
{
    /// <summary>
    /// Quita las filas de <paramref name="removedIndexes"/> (indices descendentes sobre la
    /// coleccion actual) y anade las de <paramref name="added"/>, emitiendo un unico Reset.
    ///
    /// ObservableCollection notifica una vez por elemento, y cada notificacion recorre la vista y
    /// obliga al DataGrid a recalcularse: anadir decenas de miles de filas de una en una se llevaba
    /// el hilo de interfaz durante minutos tras el escaneo. DeferRefresh() de la vista no sirve
    /// para esto: ListCollectionView lanza InvalidOperationException si la coleccion origen
    /// notifica mientras el refresco esta aplazado.
    /// </summary>
    public void ApplyBatch(IReadOnlyList<int> removedIndexes, IReadOnlyList<LiteDocumentRow> added)
    {
        if (removedIndexes.Count == 0 && added.Count == 0)
        {
            return;
        }

        CheckReentrancy();

        foreach (var index in removedIndexes)
        {
            Items.RemoveAt(index);
        }

        foreach (var row in added)
        {
            Items.Add(row);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
