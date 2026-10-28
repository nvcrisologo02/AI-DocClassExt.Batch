using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Agrega las filas por documento en una fila por activo.</summary>
public static class Consolidador
{
    public const string SinTitulo = "sin título";

    public static List<FilaActivo> Consolidar(IEnumerable<FilaDocumento> filas)
    {
        var resultado = new List<FilaActivo>();
        foreach (var grupo in filas.GroupBy(f => f.IdSareb))
        {
            var docs = grupo.ToList();
            var conRespuesta = docs
                .Where(f => f.Procesado == EstadosProcesado.Ok && f.EsTituloPrevio == ValoresIva.Si)
                .ToList();

            string hubo;
            string deduccion;
            var soporte = "";
            var revisar = false;

            if (conRespuesta.Count == 0)
            {
                hubo = SinTitulo;
                deduccion = SinTitulo;
            }
            else
            {
                hubo = Resolver(conRespuesta.Select(f => f.HuboIVA));
                var deducciones = conRespuesta.Select(f => f.DeduccionIVA).Where(v => v != ValoresIva.NoAplica).ToList();
                deduccion = deducciones.Count == 0 ? ValoresIva.NoAplica : Resolver(deducciones);

                if (hubo is ValoresIva.Si or ValoresIva.No)
                {
                    soporte = string.Join(",", conRespuesta.Where(f => f.HuboIVA == hubo).Select(f => f.ObjectId).Distinct());
                }

                revisar = Contradice(conRespuesta.Select(f => f.HuboIVA)) || Contradice(deducciones);
            }

            resultado.Add(new FilaActivo
            {
                IdSareb = grupo.Key,
                Documentos = docs.Count,
                Leidos = docs.Count(f => f.Procesado is EstadosProcesado.Ok or EstadosProcesado.NoParseable),
                ConRespuesta = conRespuesta.Count,
                HuboIVA = hubo,
                DeduccionIVA = deduccion,
                ObjectIdsSoporte = soporte,
                Revisar = revisar ? ValoresIva.Si : ValoresIva.No,
            });
        }

        return resultado;
    }

    /// <summary>"sí" si alguno es "sí"; si no, "no" si alguno es "no"; si no, "no consta".</summary>
    public static string Resolver(IEnumerable<string> valores)
    {
        var lista = valores.ToList();
        if (lista.Contains(ValoresIva.Si))
        {
            return ValoresIva.Si;
        }

        return lista.Contains(ValoresIva.No) ? ValoresIva.No : ValoresIva.NoConsta;
    }

    private static bool Contradice(IEnumerable<string> valores)
    {
        var lista = valores.ToList();
        return lista.Contains(ValoresIva.Si) && lista.Contains(ValoresIva.No);
    }
}
