using DocumentIA.Batch.Markdown;
using System.Text.Json;
using System.Text.Json.Serialization;

// -----------------------------------------------------------------------
// DocumentIA.Batch.MarkdownExtractor
// Genera un corpus JSONL a partir de PDFs organizados por carpetas.
// Cada línea del JSONL es un documento con su tipología (ruta relativa)
// y el markdown extraído con PdfPig, listo para alimentar un agente LLM.
//
// Uso:
//   dotnet run --project <proyecto> -- <inputFolder> [outputFile] [workers]
//
// Ejemplo:
//   dotnet run --project src/DocumentIA.Batch.MarkdownExtractor -- C:\PDFs\clasificados corpus.jsonl 4
// -----------------------------------------------------------------------

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Uso: <inputFolder> [outputFile=corpus.jsonl] [workers=4] [stripPrefix=]");
    Console.WriteLine();
    Console.WriteLine("  inputFolder  Carpeta raíz con PDFs organizados en subcarpetas por tipología.");
    Console.WriteLine("  outputFile   Fichero JSONL de salida (defecto: corpus.jsonl en directorio actual).");
    Console.WriteLine("  workers      Paralelismo (defecto: 4). Ajustar según memoria disponible.");
    Console.WriteLine("  stripPrefix  Prefijo a eliminar de cada segmento de ruta (ej: AI-01-).");
    return 0;
}

var inputFolder  = Path.GetFullPath(args[0]);
var outputFile   = args.Length > 1 ? args[1] : "corpus.jsonl";
var maxWorkers   = args.Length > 2 && int.TryParse(args[2], out var w) ? w : 4;
var stripPrefix  = args.Length > 3 ? args[3] : null;

if (!Directory.Exists(inputFolder))
{
    Console.Error.WriteLine($"ERROR: La carpeta de entrada no existe: {inputFolder}");
    return 1;
}

var pdfFiles = Directory.GetFiles(inputFolder, "*.pdf", SearchOption.AllDirectories);

if (pdfFiles.Length == 0)
{
    Console.Error.WriteLine("No se encontraron ficheros PDF en la carpeta indicada.");
    return 0;
}

Console.Error.WriteLine($"Procesando {pdfFiles.Length} PDFs desde: {inputFolder}");
Console.Error.WriteLine($"Salida: {Path.GetFullPath(outputFile)}  |  Workers: {maxWorkers}");
if (!string.IsNullOrEmpty(stripPrefix))
    Console.Error.WriteLine($"Strip prefix: '{stripPrefix}'");
Console.Error.WriteLine(new string('-', 72));

var generator  = new PdfPigMarkdownGenerator();
var semaphore  = new SemaphoreSlim(maxWorkers, maxWorkers);
var processed  = 0;
var failed     = 0;
var skipped    = 0;

var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

// Usar un canal de escritura secuencial para evitar entrelazado en el JSONL
var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(
    new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });

// Tarea de escritura secuencial
var writerTask = Task.Run(async () =>
{
    await using var fileWriter = new StreamWriter(outputFile, append: false, System.Text.Encoding.UTF8);
    await foreach (var line in channel.Reader.ReadAllAsync())
    {
        await fileWriter.WriteLineAsync(line);
    }
});

// Procesamiento paralelo de PDFs
var tasks = pdfFiles.Select(async pdfPath =>
{
    await semaphore.WaitAsync();
    try
    {
        var relativePath = Path.GetRelativePath(inputFolder, pdfPath).Replace('\\', '/');
        var tipologiaRaw = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty;
        var tipologia    = string.IsNullOrEmpty(stripPrefix)
            ? tipologiaRaw
            : string.Join('/', tipologiaRaw.Split('/').Select(s =>
                s.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase) ? s[stripPrefix.Length..] : s));
        var bytes        = await File.ReadAllBytesAsync(pdfPath);
        var result       = await generator.GenerateAsync(bytes);

        var isSkipped = result.Characters == 0;
        if (isSkipped) Interlocked.Increment(ref skipped);

        var record = new DocumentRecord(
            Tipologia:  tipologia,
            File:       Path.GetFileName(pdfPath),
            Path:       relativePath,
            Pages:      result.Pages,
            Chars:      result.Characters,
            Skipped:    isSkipped,
            Markdown:   result.Markdown
        );

        var json = JsonSerializer.Serialize(record, jsonOptions);
        await channel.Writer.WriteAsync(json);

        var count = Interlocked.Increment(ref processed);
        var flag  = isSkipped ? " [SIN TEXTO]" : string.Empty;
        Console.Error.WriteLine($"  [{count,4}/{pdfFiles.Length}] {relativePath}{flag}");
    }
    catch (Exception ex)
    {
        Interlocked.Increment(ref failed);
        Console.Error.WriteLine($"  ERROR: {pdfPath}");
        Console.Error.WriteLine($"         {ex.Message}");
    }
    finally
    {
        semaphore.Release();
    }
});

await Task.WhenAll(tasks);
channel.Writer.Complete();
await writerTask;

Console.Error.WriteLine(new string('-', 72));
Console.Error.WriteLine($"Completado: {processed} procesados  |  {skipped} sin texto  |  {failed} errores");
Console.Error.WriteLine($"Fichero JSONL: {Path.GetFullPath(outputFile)}");

return failed > 0 ? 2 : 0;

// -----------------------------------------------------------------------
// Modelo de registro para serialización
// -----------------------------------------------------------------------
record DocumentRecord(
    [property: JsonPropertyName("tipologia")] string Tipologia,
    [property: JsonPropertyName("file")]      string File,
    [property: JsonPropertyName("path")]      string Path,
    [property: JsonPropertyName("pages")]     int    Pages,
    [property: JsonPropertyName("chars")]     int    Chars,
    [property: JsonPropertyName("skipped")]   bool   Skipped,
    [property: JsonPropertyName("markdown")]  string Markdown
);
