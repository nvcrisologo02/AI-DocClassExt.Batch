using System.Globalization;
using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Compone la frase legible de sustento de un activo a partir de sus columnas consolidadas.</summary>
public static class FraseSustento
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Componer(FilaActivo a)
    {
        if (a.HuboIVA == Consolidador.SinTitulo)
        {
            return "sin título previo";
        }

        var iva = new List<string> { $"IVA {a.HuboIVA}" };
        if (a.HuboIVA == ValoresIva.Si)
        {
            if (a.TipoIVA is { } tipo)
            {
                iva.Add(tipo.ToString("0.##", Es) + "%");
            }

            if (a.CuotaIVA is { } cuota)
            {
                iva.Add(cuota.ToString("N2", Es) + " €");
            }
        }

        var frase = string.Join(" ", iva) + Parentesis(a.DocIVA, a.Cedente, a.FechaAdquisicion);
        var deduccion = $"deducción {a.DeduccionIVA}" + Parentesis(a.DocDeduccion, "", "");
        return $"{frase}; {deduccion}";
    }

    private static string Parentesis(string doc, string cedente, string fecha)
    {
        if (string.IsNullOrWhiteSpace(doc))
        {
            return "";
        }

        var partes = new List<string> { $"doc {doc}" };
        if (!string.IsNullOrWhiteSpace(cedente))
        {
            partes.Add($"cedente {cedente}");
        }

        if (!string.IsNullOrWhiteSpace(fecha))
        {
            partes.Add(fecha);
        }

        return $" ({string.Join(", ", partes)})";
    }
}
