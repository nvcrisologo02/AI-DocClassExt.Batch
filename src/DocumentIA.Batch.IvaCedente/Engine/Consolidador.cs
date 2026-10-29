using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Agrega las filas por documento en una fila por activo.</summary>
public static class Consolidador
{
    public const string SinTitulo = "sin título";
    public const string MotivoSinResolver = "filas sin resolver";
    public const string MotivoIvaContradictorio = "IVA contradictorio";
    public const string MotivoDeduccionContradictoria = "deducción contradictoria";
    public const string MotivoCuotasDistintas = "cuotas de IVA distintas";

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
            FilaDocumento? tituloIva = null;
            FilaDocumento? tituloDeduccion = null;
            var motivos = new List<string>();
            // Un activo con alguna fila sin resolver (pendiente, error o timeout) puede tener un título sin leer.
            var sinResolver = docs.Count(f => f.Procesado is EstadosProcesado.Pendiente or EstadosProcesado.Error or EstadosProcesado.Timeout);
            if (sinResolver > 0)
            {
                motivos.Add($"{MotivoSinResolver} ({sinResolver})");
            }

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
                    var candidatos = conRespuesta.Where(f => f.HuboIVA == hubo).ToList();
                    soporte = string.Join(",", candidatos.Select(f => f.ObjectId).Distinct());
                    tituloIva = ElegirTitulo(candidatos);
                }

                if (deduccion is ValoresIva.Si or ValoresIva.No)
                {
                    var candidatos = conRespuesta.Where(f => f.DeduccionIVA == deduccion).ToList();
                    tituloDeduccion = tituloIva != null && candidatos.Contains(tituloIva) ? tituloIva : ElegirTitulo(candidatos);
                }

                if (Contradice(conRespuesta.Select(f => f.HuboIVA)))
                {
                    motivos.Add(MotivoIvaContradictorio);
                }

                if (Contradice(deducciones))
                {
                    motivos.Add(MotivoDeduccionContradictoria);
                }

                var cuotas = conRespuesta.Where(f => f.HuboIVA == ValoresIva.Si && f.CuotaIVA is not null).Select(f => f.CuotaIVA!.Value).Distinct().Count();
                if (cuotas > 1)
                {
                    motivos.Add(MotivoCuotasDistintas);
                }
            }

            var fila = new FilaActivo
            {
                IdSareb = grupo.Key,
                Documentos = docs.Count,
                Leidos = docs.Count(f => f.Procesado is EstadosProcesado.Ok or EstadosProcesado.NoParseable),
                ConRespuesta = conRespuesta.Count,
                HuboIVA = hubo,
                DeduccionIVA = deduccion,
                DocIVA = tituloIva?.ObjectId ?? "",
                TipoIVA = hubo == ValoresIva.Si ? tituloIva?.TipoIVA : null,
                CuotaIVA = hubo == ValoresIva.Si ? tituloIva?.CuotaIVA : null,
                Cedente = tituloIva?.Cedente ?? "",
                FechaAdquisicion = tituloIva?.FechaAdquisicion ?? "",
                DocDeduccion = tituloDeduccion?.ObjectId ?? "",
                ObjectIdsSoporte = soporte,
                Revisar = motivos.Count > 0 ? ValoresIva.Si : ValoresIva.No,
                MotivoRevisar = string.Join("; ", motivos),
            };
            fila.Sustento = FraseSustento.Componer(fila);
            resultado.Add(fila);
        }

        return resultado;
    }

    /// <summary>Título de sustento: el que tenga cuota, luego el que tenga tipo, luego mayor confianza, luego el primero.</summary>
    public static FilaDocumento? ElegirTitulo(IEnumerable<FilaDocumento> candidatos)
        => candidatos
            .OrderByDescending(f => f.CuotaIVA is not null)
            .ThenByDescending(f => f.TipoIVA is not null)
            .ThenByDescending(f => f.Confianza ?? -1)
            .FirstOrDefault();

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
