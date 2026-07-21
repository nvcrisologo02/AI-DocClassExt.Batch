using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using DocumentIA.Batch.ClassificationLite.Services;
using DocumentIA.Batch.Evaluation.Engine;
using DocumentIA.Batch.Evaluation.Io;
using DocumentIA.Batch.Evaluation.Models;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch.Evaluation.Commands;

public static class RunCommand
{
    private const string DefaultCorpusRoot = @"H:\Documentia\ParaNacho\Class";
    private const string DefaultEnv = "DEV";
    private const int DefaultParallel = 2;

    public static async Task<int> ExecuteAsync(string[] args)
    {
        var parsed = CliArgs.Parse(args);
        var set = DatasetSelector.ParseSet(parsed.Require("set"));
        var corpusRoot = parsed.GetOrDefault("corpus-root", DefaultCorpusRoot);
        var envName = parsed.GetOrDefault("env", DefaultEnv);
        var label = parsed.GetOrDefault("label", string.Empty);
        var parallel = parsed.GetIntOrDefault("parallel", DefaultParallel);
        var configPath = parsed.GetOrDefault("config", Path.Combine(AppContext.BaseDirectory, "config.json"));

        if (parallel < 1)
        {
            throw new EvaluationUsageException("--parallel debe ser >= 1.");
        }

        var evalDir = EvalPaths.FindEvalDirectory();
        var documents = LoadDocuments(set, evalDir);
        if (documents.Count == 0)
        {
            throw new EvaluationUsageException($"El set '{set}' no tiene documentos que evaluar.");
        }

        var config = LoadConfig(configPath);
        var environment = config.Environments.FirstOrDefault(e => string.Equals(e.Name, envName, StringComparison.OrdinalIgnoreCase))
            ?? throw new EvaluationUsageException(
                $"El entorno '{envName}' no existe en '{configPath}'. Entornos disponibles: "
                + string.Join(", ", config.Environments.Select(e => e.Name)));

        if (string.IsNullOrWhiteSpace(environment.FunctionKey))
        {
            throw new EvaluationUsageException(
                $"El entorno '{envName}' no tiene Function Key configurada en '{configPath}'. "
                + "Copia config.sample.json a config.json junto al ejecutable y rellena la key "
                + "(nunca la pases por consola).");
        }

        // Clasificacion-only a nivel TDN1_TDN2, sin resumen: es lo que pide el harness de
        // evaluacion, independientemente de lo que tenga guardado config.json para la app Lite.
        var evalConfig = new LiteConfig
        {
            SelectedEnvironment = environment.Name,
            Environments = config.Environments,
            ParallelQueries = parallel,
            InternalBatchSize = config.InternalBatchSize,
            PollingIntervalSeconds = config.PollingIntervalSeconds,
            ClassificationLevel = "TDN1_TDN2",
            Provider = config.Provider,
            Model = config.Model,
            OnlyClassification = true,
            ForceReprocess = config.ForceReprocess,
            MaxRetries = config.MaxRetries,
            SkipAlreadyProcessed = config.SkipAlreadyProcessed,
            GenerateSummary = false
        };

        var backend = new IngestBackendAdapter(new DocumentIaBackendClient(), environment);
        var classifier = new EvaluationClassifier(backend, evalConfig);

        var startedAt = DateTime.UtcNow;
        var runDirName = $"{startedAt:yyyyMMdd-HHmmss}" + (string.IsNullOrWhiteSpace(label) ? string.Empty : $"-{label}");
        var runDir = Path.Combine(evalDir, "runs", runDirName);
        Directory.CreateDirectory(runDir);

        Console.WriteLine($"Set: {set} ({documents.Count} documentos) | Entorno: {environment.Name} ({environment.BackendUrl})");
        Console.WriteLine($"Corpus: {corpusRoot} | Paralelismo: {parallel} | Salida: {runDir}");

        var results = new EvaluationResultRow[documents.Count];
        var processed = 0;
        var gate = new object();

        using var semaphore = new SemaphoreSlim(parallel);
        var tasks = documents.Select(async (doc, i) =>
        {
            await semaphore.WaitAsync();
            try
            {
                results[i] = await classifier.ClassifyAsync(corpusRoot, doc, CancellationToken.None);
            }
            finally
            {
                semaphore.Release();
                lock (gate)
                {
                    processed++;
                    if (processed % 10 == 0 || processed == documents.Count)
                    {
                        Console.WriteLine($"  [{processed}/{documents.Count}] procesados");
                    }
                }
            }
        });

        await Task.WhenAll(tasks);

        var completedAt = DateTime.UtcNow;
        ResultsCsv.Write(Path.Combine(runDir, "results.csv"), results);

        var runInfo = new RunInfo
        {
            Env = environment.Name,
            Set = set.ToString(),
            CorpusRoot = corpusRoot,
            Label = string.IsNullOrWhiteSpace(label) ? null : label,
            AppVersion = typeof(RunCommand).Assembly.GetName().Version?.ToString() ?? "unknown",
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            Total = results.Length,
            Ok = results.Count(r => r.Estado == EvaluationEstado.Ok),
            Error = results.Count(r => r.Estado == EvaluationEstado.Error),
            Timeout = results.Count(r => r.Estado == EvaluationEstado.Timeout),
            Parallel = parallel
        };

        var runInfoJson = System.Text.Json.JsonSerializer.Serialize(runInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(runDir, "run-info.json"), runInfoJson);

        Console.WriteLine($"Completado: {runInfo.Ok} OK, {runInfo.Error} error, {runInfo.Timeout} timeout de {runInfo.Total} en {(completedAt - startedAt).TotalMinutes:F1} min.");
        Console.WriteLine($"Resultados: {Path.Combine(runDir, "results.csv")}");

        return 0;
    }

    private static List<ManifestRow> LoadDocuments(EvaluationSet set, string evalDir)
    {
        switch (set)
        {
            case EvaluationSet.Golden:
                return ManifestCsvReader.Read(Path.Combine(evalDir, "golden.csv"));
            case EvaluationSet.Full:
                return DatasetSelector.Full(ManifestCsvReader.Read(Path.Combine(evalDir, "manifest.csv")));
            case EvaluationSet.HalfA:
                return DatasetSelector.HalfA(DatasetSelector.Full(ManifestCsvReader.Read(Path.Combine(evalDir, "manifest.csv"))));
            case EvaluationSet.HalfB:
                return DatasetSelector.HalfB(DatasetSelector.Full(ManifestCsvReader.Read(Path.Combine(evalDir, "manifest.csv"))));
            case EvaluationSet.Cata100:
                return DatasetSelector.Cata100(ManifestCsvReader.Read(Path.Combine(evalDir, "manifest.csv")));
            default:
                throw new EvaluationUsageException($"--set desconocido: '{set}'.");
        }
    }

    private static LiteConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new EvaluationUsageException(
                $"No se encontro config.json en '{configPath}'. Copia config.sample.json junto al "
                + "ejecutable, renombralo a config.json y rellena la Function Key del entorno.");
        }

        return new LiteConfigService(configPath).Load();
    }
}
