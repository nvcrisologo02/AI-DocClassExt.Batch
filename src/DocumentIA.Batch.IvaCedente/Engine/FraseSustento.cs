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

        var frase = Iva(a.HuboIVA, a.TipoIVA, a.CuotaIVA) + Parentesis(a.DocIVA, a.Cedente, a.FechaAdquisicion);
        var deduccion = $"deducción {a.DeduccionIVA}" + Parentesis(a.DocDeduccion, "", "");
        var discrepan = a.Discrepancias.Count == 0 ? "" : "; discrepan: " + string.Join("; ", a.Discrepancias.Select(Entrada));
        return $"{frase}; {deduccion}{discrepan}";
    }

    /// <summary>"IVA sí 21% 1.200,00 €": tipo y cuota solo cuando el valor es sí y se conocen.</summary>
    private static string Iva(string hubo, double? tipo, double? cuota)
    {
        var partes = new List<string> { $"IVA {hubo}" };
        if (hubo == ValoresIva.Si)
        {
            if (tipo is { } t)
            {
                partes.Add(t.ToString("0.##", Es) + "%");
            }

            if (cuota is { } c)
            {
                partes.Add(c.ToString("N2", Es) + " €");
            }
        }

        return string.Join(" ", partes);
    }

    /// <summary>"doc 456 IVA sí 21% 1.200,00 €, deducción no": solo las partes en las que el título discrepa.</summary>
    private static string Entrada(Discrepancia d)
    {
        var partes = new List<string>();
        if (d.HuboIVA is { } hubo)
        {
            partes.Add(Iva(hubo, d.TipoIVA, d.CuotaIVA));
        }

        if (d.DeduccionIVA is { } deduccion)
        {
            partes.Add($"deducción {deduccion}");
        }

        return $"doc {d.ObjectId} " + string.Join(", ", partes);
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
