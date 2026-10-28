using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Io;

/// <summary>Lectura del CSV de entrada (ID_SAREB;Object ID del Documento;TDN2).</summary>
public static class EntradaCsv
{
    public const string CabeceraIdSareb = "ID_SAREB";
    public const string CabeceraObjectId = "Object ID del Documento";
    public const string CabeceraTdn2 = "TDN2";

    public static List<FilaEntrada> Leer(string path)
    {
        var filas = CsvUtil.ReadRows(path);
        if (filas.Count == 0)
        {
            throw new IvaCedenteUsageException($"El CSV de entrada '{path}' está vacío.");
        }

        var cabecera = filas[0].Select(Normalizar).ToList();
        var iId = Indice(cabecera, CabeceraIdSareb);
        var iObj = Indice(cabecera, CabeceraObjectId);
        var iTdn = Indice(cabecera, CabeceraTdn2);
        var maximo = Math.Max(iId, Math.Max(iObj, iTdn));

        var resultado = new List<FilaEntrada>();
        var vistos = new HashSet<(string, string)>();

        for (var n = 1; n < filas.Count; n++)
        {
            var celdas = filas[n];
            var numero = n + 1;

            if (celdas.Length <= maximo)
            {
                throw new IvaCedenteUsageException(
                    $"La fila {numero} del CSV tiene {celdas.Length} columnas y se necesitan al menos {maximo + 1}.");
            }

            var id = celdas[iId].Trim();
            var obj = celdas[iObj].Trim();
            var tdn = celdas[iTdn].Trim();

            // Un salto de línea en estos campos solo aparece si una comilla quedó sin cerrar y el
            // lector unió el resto del fichero en una fila.
            if (id.Contains('\n') || obj.Contains('\n') || tdn.Contains('\n'))
            {
                throw new IvaCedenteUsageException(
                    $"La fila {numero} del CSV contiene una comilla sin cerrar.");
            }

            if (id.Length == 0 && obj.Length == 0 && tdn.Length == 0)
            {
                continue;
            }

            if (obj.Length == 0)
            {
                throw new IvaCedenteUsageException($"La fila {numero} del CSV no tiene '{CabeceraObjectId}'.");
            }

            if (vistos.Add((id, obj)))
            {
                resultado.Add(new FilaEntrada(id, obj, tdn.ToUpperInvariant()));
            }
        }

        return resultado;
    }

    private static string Normalizar(string celda) => celda.Replace("\uFEFF", string.Empty).Trim();

    private static int Indice(List<string> cabecera, string nombre)
    {
        var i = cabecera.FindIndex(c => string.Equals(c, nombre, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
        {
            throw new IvaCedenteUsageException($"Al CSV de entrada le falta la cabecera '{nombre}'.");
        }

        return i;
    }
}
