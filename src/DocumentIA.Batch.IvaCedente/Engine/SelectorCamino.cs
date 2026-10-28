using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Camino que sigue un documento según su TDN2.</summary>
public enum Camino
{
    NoAplica,
    UnaLlamada,
    DosLlamadas
}

public static class SelectorCamino
{
    /// <summary>
    /// NoAplica si el TDN2 está en CodigosExcluidos; DosLlamadas (preclasificar antes) si está en
    /// CodigosPreclasificar; en cualquier otro caso, incluido vacío o desconocido, UnaLlamada.
    /// </summary>
    public static Camino Decidir(string tdn2, IvaConfig config)
    {
        var codigo = (tdn2 ?? string.Empty).Trim().ToUpperInvariant();

        if (Contiene(config.CodigosExcluidos, codigo))
        {
            return Camino.NoAplica;
        }

        return Contiene(config.CodigosPreclasificar, codigo) ? Camino.DosLlamadas : Camino.UnaLlamada;
    }

    private static bool Contiene(IEnumerable<string>? lista, string codigo)
        => codigo.Length > 0 && lista is not null
           && lista.Any(c => string.Equals(c?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
}
