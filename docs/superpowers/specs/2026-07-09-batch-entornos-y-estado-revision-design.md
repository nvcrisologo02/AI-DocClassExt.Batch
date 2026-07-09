# DocumentIA.Batch — Selector de entornos y estado EXTRACCION_INCOMPLETA

**Fecha:** 2026-07-09
**Work items:** AB#99871 (estado EXTRACCION_INCOMPLETA en revisión). El selector de entornos es alcance nuevo acordado en esta sesión; se creará su propio WI antes de implementar.
**Repo:** `DocumentIA.Batch` (app WPF de procesamiento por lotes).

## Objetivo

1. Que la app Batch reconozca el nuevo estado `EXTRACCION_INCOMPLETA` (introducido en el backend por AB#99861) como caso de revisión, para que esos documentos entren en el bucket de "Revisión" y sean reprocesables en vez de contarse como completados.
2. Sustituir la configuración de un único backend (URL + Function Key sueltos) por un **catálogo de entornos** seleccionable con un desplegable, gestionable desde la propia app, con la configuración de cada entorno (endpoint + apikey) persistida en el fichero de config.

## Contexto actual (código existente)

- `BatchConfig` (`Models/BatchConfig.cs`): tiene `string BackendUrl` y `string FunctionKey` únicos (por defecto apuntan a prod, `https://srbappprodocai.azurewebsites.net`), más el resto de preferencias (tipología, umbrales, colas, prompts…).
- `SettingsService` (`Services/SettingsService.cs`): carga/guarda un único `config.json` en `AppContext.BaseDirectory` (directorio de salida, junto al `.exe`; NO está en control de versiones).
- `MainViewModel`: expone `BackendUrl`/`FunctionKey` (TwoWay a dos cajas de texto en `MainWindow.xaml`), `EffectiveBackendUrl` (cae a la URL de prod por defecto), `LoadConfig()`/`SaveConfig()`, `RefreshTipologiasAsync()` (usa `BackendUrl`) y el health check (usa `EffectiveBackendUrl` + `FunctionKey`). Todas las llamadas al backend van vía `DocumentIaBackendClient` con URL + key.
- `IsRevisionQuality(string)` (`MainViewModel.cs:1517-1522`): clasifica si un estado es "de revisión". Hoy matchea `"REVISION"`, `"VALIDACION_CON_ERRORES"` y `"BAJA_CONFIANZA"`. Se alimenta de `TryGetEstadoCalidad()`, que lee `Resultado.EstadoCalidad` (OK/REVISION/ERROR) y, si viene vacío, cae a `Resultado.Estado` (OK/EXTRACCION_INCOMPLETA/VALIDACION_CON_ERRORES/BAJA_CONFIANZA_CLASIFICACION/…).

## Decisiones de diseño (acordadas)

- **Un solo fichero:** todo va en `config.json`; no se crea fichero de entornos aparte.
- **Entornos editables desde la app** mediante un **diálogo de gestión** dedicado (patrón del `PromptEditorDialog` existente), no cajas libres en el panel principal.
- **Apikeys en texto plano** dentro de `config.json` (que no se versiona; mismo tratamiento que hoy tiene `FunctionKey`).
- Se incluye el fix del bug latente `BAJA_CONFIANZA` → `BAJA_CONFIANZA_CLASIFICACION` en `IsRevisionQuality`.

## Arquitectura

### Modelo de configuración

`BatchConfig` gana dos campos y deja los antiguos solo para migración:

```csharp
public class BatchConfig
{
    // ...campos actuales sin cambios (SelectedTipologia, umbrales, NumeroColas, prompts, etc.)...

    // Catálogo de entornos y entorno activo (NUEVO)
    public List<EnvironmentConfig> Environments { get; set; } = new();
    public string SelectedEnvironment { get; set; } = string.Empty;

    // Legacy: se conservan SOLO para migración desde config.json anteriores.
    // Tras migrar, la fuente de verdad son Environments + SelectedEnvironment.
    public string BackendUrl { get; set; } = "https://srbappprodocai.azurewebsites.net";
    public string FunctionKey { get; set; } = string.Empty;
}

public class EnvironmentConfig
{
    public string Name { get; set; } = string.Empty;
    public string BackendUrl { get; set; } = string.Empty;
    public string FunctionKey { get; set; } = string.Empty;
}
```

### Migración (en `SettingsService.Load()` o en un paso de normalización invocado tras cargar)

Regla determinista, idempotente:

1. Si `Environments` NO está vacío → no se toca (config ya migrado).
2. Si `Environments` está vacío y `BackendUrl` heredado tiene valor → crear un entorno `{ Name = "Producción", BackendUrl = <heredado>, FunctionKey = <heredado> }`, y `SelectedEnvironment = "Producción"`.
3. Si `Environments` está vacío y no hay heredado útil → sembrar `{ Name = "Producción", BackendUrl = "https://srbappprodocai.azurewebsites.net", FunctionKey = "" }` y `SelectedEnvironment = "Producción"`, para que el desplegable nunca esté vacío.

En todos los casos, tras migrar, si `SelectedEnvironment` no corresponde a ningún entorno existente, se selecciona el primero del catálogo.

### Resolución del entorno activo

`MainViewModel` deja de exponer `BackendUrl`/`FunctionKey` como cajas editables y pasa a:

- `ObservableCollection<EnvironmentConfig> Environments`
- `EnvironmentConfig? SelectedEnvironment` (o el nombre + lookup)
- `EffectiveBackendUrl` → `SelectedEnvironment?.BackendUrl` (o cadena vacía si no hay)
- `EffectiveFunctionKey` → `SelectedEnvironment?.FunctionKey ?? ""`

Todas las llamadas al backend (`DocumentIaBackendClient`, tipologías, health, procesamiento) usan `EffectiveBackendUrl`/`EffectiveFunctionKey`.

## Interfaz

### Panel principal (`MainWindow.xaml`)

- Se **eliminan** las dos cajas de texto "Backend URL" y "Function Key".
- Se añaden:
  - Un **ComboBox de entornos** (`ItemsSource = Environments`, `DisplayMemberPath = Name`, `SelectedItem = SelectedEnvironment` TwoWay).
  - Un botón **"Gestionar entornos"**.
- Se **mantiene** el texto de solo lectura existente "Endpoint: {EffectiveBackendUrl}" para ver a qué apunta el entorno activo.

### Diálogo "Gestionar entornos" (ventana nueva, patrón `PromptEditorDialog`)

- Lista de entornos (por nombre) con botones **Añadir / Editar / Borrar**.
- Formulario por entorno: **Nombre**, **Backend URL**, **Function Key** (esta última en `PasswordBox` o campo enmascarado).
- **Aceptar** valida y devuelve el catálogo actualizado; **Cancelar** descarta.
- Validación: nombre no vacío y único (case-insensitive); Backend URL no vacía. Si se borra el entorno activo, tras aceptar se selecciona el primero restante (o ninguno si queda vacío).
- Al aceptar, el ViewModel actualiza `Environments`, ajusta `SelectedEnvironment` si procede, y guarda `config.json` vía `SettingsService.Save`.

## Flujo de datos y comportamiento

- **Arranque:** `SettingsService.Load()` → migración → el ViewModel puebla `Environments` y fija `SelectedEnvironment`. Se dispara refresco de tipologías + health del entorno activo.
- **Cambio de entorno (ComboBox):** persistir `SelectedEnvironment` en `config.json`; recalcular `EffectiveBackendUrl`/`EffectiveFunctionKey`; re-lanzar `RefreshTipologiasAsync()` y health contra el nuevo backend; refrescar el `CanExecute` de los comandos.
- **Guard:** si el catálogo está vacío o no hay entorno seleccionado (o su URL está vacía), se deshabilita el procesado y el refresco (equivalente al guard actual sobre `BackendUrl` vacío).

## Fix de estado de revisión (AB#99871)

En `IsRevisionQuality` (`MainViewModel.cs:1517-1522`), el conjunto de estados de revisión pasa a incluir `EXTRACCION_INCOMPLETA` y a corregir `BAJA_CONFIANZA` → `BAJA_CONFIANZA_CLASIFICACION`:

```csharp
private static bool IsRevisionQuality(string value)
{
    return string.Equals(value, "REVISION", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "VALIDACION_CON_ERRORES", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "EXTRACCION_INCOMPLETA", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "BAJA_CONFIANZA_CLASIFICACION", StringComparison.OrdinalIgnoreCase);
}
```

Para poder testearlo sin instanciar el ViewModel WPF, la lógica se extrae a un helper puro:

```csharp
// Services/BatchStatusClassifier.cs
public static class BatchStatusClassifier
{
    private static readonly HashSet<string> RevisionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "REVISION", "VALIDACION_CON_ERRORES", "EXTRACCION_INCOMPLETA", "BAJA_CONFIANZA_CLASIFICACION"
    };

    public static bool IsRevisionQuality(string? value) =>
        !string.IsNullOrWhiteSpace(value) && RevisionStates.Contains(value.Trim());
}
```

`MainViewModel.IsRevisionQuality` pasa a delegar en `BatchStatusClassifier.IsRevisionQuality`. `IsRevisionFile` sigue igual (combina `Estado == "Revision"` con `IsRevisionQuality(EstadoCalidad)`).

## Testing

Se crea un proyecto nuevo `tests/DocumentIA.Batch.Tests` (xUnit, mismo estilo que `tests/DocumentIA.Batch.Classification.Tests`), referenciando `DocumentIA.Batch`. Cobertura de lógica pura (la UI WPF se verifica manualmente):

1. **Clasificación de estado** (`BatchStatusClassifier`):
   - `EXTRACCION_INCOMPLETA` → revisión (true).
   - `BAJA_CONFIANZA_CLASIFICACION` → revisión (true).
   - `REVISION`, `VALIDACION_CON_ERRORES` → true (no regresión).
   - `OK`, `""`, `null`, `ERROR` → false.
   - Case-insensitive y con espacios (`" extraccion_incompleta "`).
2. **Migración de config** (`SettingsService` o helper de normalización):
   - Config heredado con `BackendUrl`/`FunctionKey` y `Environments` vacío → tras cargar, un entorno `"Producción"` con esos valores y `SelectedEnvironment = "Producción"`.
   - Config ya migrado (con `Environments`) → intacto, no se duplica ni reordena.
   - Config sin nada útil → semilla `"Producción"` con URL de prod y key vacía.
3. **Round-trip de serialización** de `BatchConfig` con `Environments` (varios entornos) → serializar y deserializar preserva nombres, URLs y keys.

**Verificación manual (no automatizada):** desplegable puebla entornos; cambiar de entorno refresca tipologías/health y persiste; diálogo de gestión añade/edita/borra y valida nombre único/URL; borrar el entorno activo reselecciona correctamente.

## Fuera de alcance (YAGNI)

- Cifrado de apikeys (DPAPI u otro): se mantiene texto plano.
- Fichero de entornos separado o gestión por ops fuera de la app.
- Import/export de entornos, perfiles compartidos, o sincronización remota.
- Cambios en el backend o en el contrato de estados (ya cubiertos por AB#99861).
