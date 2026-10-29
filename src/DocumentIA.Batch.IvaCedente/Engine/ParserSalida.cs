using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Engine;

/// <summary>Campos que interesan de la salida de DocumentIA.</summary>
public sealed record SalidaDocumentIa(string? EstadoContrato, string? Tdn2, string? ResultadoPrompt);

/// <summary>Lee la salida de DocumentIA y la respuesta JSON del modelo.</summary>
public static class ParserSalida
{
    /// <summary>Valor que emite el core. Se acepta también el nombre corto por compatibilidad.</summary>
    public const string EstadoSinContenido = "SIN_CONTENIDO_DOCUMENTO";

    public static bool EsSinContenido(string? estado)
        => string.Equals(estado, EstadoSinContenido, StringComparison.OrdinalIgnoreCase)
           || string.Equals(estado, "SIN_CONTENIDO", StringComparison.OrdinalIgnoreCase);

    public static SalidaDocumentIa Leer(JsonElement output)
    {
        var lite = LiteResultParser.Parse(output);
        string? resultadoPrompt = null;
        if (output.ValueKind == JsonValueKind.Object
            && Propiedad(output, "datosExtraidos") is { ValueKind: JsonValueKind.Object } datos
            && Propiedad(datos, "resultadoPrompt") is { } valor)
        {
            resultadoPrompt = TextoONulo(valor);
        }
        return new SalidaDocumentIa(lite.Estado, lite.Tdn2, resultadoPrompt);
    }

    public static RespuestaPrompt? ParsearRespuesta(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var ini = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (ini < 0 || fin <= ini) return null;
        try
        {
            using var doc = JsonDocument.Parse(texto.Substring(ini, fin - ini + 1));
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;
            return new RespuestaPrompt
            {
                EsTituloPrevio = LeerBool(Propiedad(raiz, "esTituloPrevio")),
                HuboIVA = NormalizarValor(TextoONulo(Propiedad(raiz, "huboIVA")), false),
                DeduccionIVA = NormalizarValor(TextoONulo(Propiedad(raiz, "deduccionIVA")), true),
                Cedente = TextoONulo(Propiedad(raiz, "cedente")),
                FechaAdquisicion = TextoONulo(Propiedad(raiz, "fechaAdquisicion")),
                TipoIVA = LeerImporte(Propiedad(raiz, "tipoIVA")),
                CuotaIVA = LeerImporte(Propiedad(raiz, "cuotaIVA")),
                Evidencia = TextoONulo(Propiedad(raiz, "evidencia")),
                Confianza = LeerNumero(Propiedad(raiz, "confianza"))
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string NormalizarValor(string? valor, bool permiteNoAplica)
    {
        var v = SinAcentos((valor ?? string.Empty).Trim()).ToLowerInvariant();
        return v switch
        {
            "si" or "true" or "s" => ValoresIva.Si,
            "no" or "false" => ValoresIva.No,
            "no aplica" or "n/a" when permiteNoAplica => ValoresIva.NoAplica,
            _ => ValoresIva.NoConsta
        };
    }

    private static JsonElement? Propiedad(JsonElement objeto, string nombre)
    {
        foreach (var p in objeto.EnumerateObject())
        {
            if (string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase)) return p.Value;
        }
        return null;
    }

    private static string? TextoONulo(JsonElement? e) => e is null ? null : e.Value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => e.Value.GetString(),
        _ => e.Value.GetRawText()
    };

    private static bool LeerBool(JsonElement? e)
    {
        if (e is null) return false;
        return e.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => SinAcentos(e.Value.GetString()!.Trim()).ToLowerInvariant() is "true" or "si",
            _ => false
        };
    }

    private static double? LeerNumero(JsonElement? e)
    {
        if (e is null) return null;
        if (e.Value.ValueKind == JsonValueKind.Number) return e.Value.GetDouble();
        if (e.Value.ValueKind == JsonValueKind.String
            && double.TryParse(e.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return n;
        return null;
    }

    /// <summary>
    /// Número que el modelo puede devolver como texto con símbolos ("21%", "64.050,00 €"). Se quitan
    /// símbolos y espacios; con coma decimal se eliminan los puntos de miles; un texto solo con grupos
    /// de tres cifras separados por puntos ("64.050") se toma como miles. Lo que no sea número queda null.
    /// </summary>
    private static double? LeerImporte(JsonElement? e)
    {
        if (e is null) return null;
        if (e.Value.ValueKind == JsonValueKind.Number) return e.Value.GetDouble();
        if (e.Value.ValueKind != JsonValueKind.String) return null;

        var t = e.Value.GetString()!
            .Replace("%", "").Replace("€", "").Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "").Replace(((char)0xA0).ToString(), "").Trim();
        if (t.Length == 0) return null;
        if (t.Contains(','))
        {
            t = t.Replace(".", "").Replace(',', '.');
        }
        else if (Regex.IsMatch(t, @"^\d{1,3}(\.\d{3})+$"))
        {
            t = t.Replace(".", "");
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static string SinAcentos(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
