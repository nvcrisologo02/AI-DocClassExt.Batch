# Plan: discrepancias en `Sustento` y `Revisar` con filas sin resolver (IvaCedente)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que el consolidado de IvaCedente muestre en `Sustento` los títulos que discrepan del elegido y no mande a revisión los activos con IVA resuelto por tener ficheros sin resolver.

**Architecture:** `Consolidador` recoge, por activo, una lista de `Discrepancia` (un registro por título que discrepa, con sus valores) y la deja en `FilaActivo.Discrepancias`, que no se escribe en el CSV; `FraseSustento` la convierte en el tramo `; discrepan: …`. La regla de `Revisar` se ajusta en el mismo `Consolidador`.

**Tech Stack:** .NET 8, xUnit. Proyecto `src/DocumentIA.Batch.IvaCedente`, tests `tests/DocumentIA.Batch.IvaCedente.Tests`.

**Spec:** `docs/superpowers/specs/2026-10-30-iva-cedente-sustento-discrepancias-design.md`

**Work item:** AB#101211 (Task bajo AB#101191). Todos los commits llevan `(AB#101211)`.

## Global Constraints

- Español en código visible al usuario, comentarios, tests y commits; sin autoría ni menciones de modelos.
- Ediciones sin BOM (el commit 124f974 ya tuvo que retirarlo). Un solo `dotnet` a la vez.
- Formato es-ES de tipo y cuota igual que en la frase principal: tipo `0.##` + `%`, cuota `N2` + ` €`.
- Separadores exactos: tramo `; discrepan: `, entradas separadas por `; `, partes de una entrada por `, `.
- Decisión del plan (no está en la spec): en `IVA contradictorio` y `deducción contradictoria` solo discrepan los títulos con valor `sí` o `no`; los `no consta` / `no aplica` no se listan.
- Entradas ordenadas por `ObjectId` con `StringComparer.Ordinal`.
- Antes de cada commit: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests` en verde y `dotnet format DocumentIA.Batch.sln --verify-no-changes` limpio.

## Review Focus

1. Título que discrepa en IVA y en deducción a la vez: una sola entrada `doc X IVA no, deducción no` (test en Tarea 3).
2. Activo con `cuotas de IVA distintas` donde un título con IVA sí no tiene cuota: no se lista por cuota (test en Tarea 2).
3. Activo `no consta` con filas sin resolver: sigue `Revisar = sí` (test en Tarea 1).
4. Activo con filas sin resolver y además `IVA contradictorio`: `Revisar = sí` y motivos en orden (test existente `Consolidar_MotivoRevisar_AcumulaEnOrden`, se mantiene).
5. Activo sin discrepancias: `Sustento` idéntico al actual, sin tramo vacío (tests existentes de `FraseSustentoTests`, se mantienen).

---

### Tarea 1: `Revisar = no` cuando el único motivo son filas sin resolver y el IVA está resuelto

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs` (cálculo de `Revisar`)
- Modify: `src/DocumentIA.Batch.IvaCedente/Commands/ConsolidarCommand.cs` (texto del aviso)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Produces: `FilaActivo.Revisar` = `"no"` cuando `MotivoRevisar == "filas sin resolver (n)"` y `HuboIVA` es `sí` o `no`.

- [ ] **Paso 1: cambiar la expectativa del test existente y añadir los casos nuevos**

Sustituir `Consolidar_ActivoConFilaIncompleta_MarcaRevisarSinCambiarLosValores` por:

```csharp
[Theory]
[InlineData(EstadosProcesado.Error)]
[InlineData(EstadosProcesado.Timeout)]
[InlineData(EstadosProcesado.Pendiente)]
public void Consolidar_FilaIncompletaConIvaResuelto_DejaElMotivoPeroNoRevisa(string estado)
{
    var a = Uno(Titulo("1", "A", "sí", "sí"), new FilaDocumento { IdSareb = "1", ObjectId = "B", Procesado = estado });
    Assert.Equal("no", a.Revisar);
    Assert.Equal("filas sin resolver (1)", a.MotivoRevisar);
    Assert.Equal("sí", a.HuboIVA);

    var conNo = Uno(Titulo("2", "C", "no"), new FilaDocumento { IdSareb = "2", ObjectId = "D", Procesado = estado });
    Assert.Equal("no", conNo.Revisar);
}

[Theory]
[InlineData(EstadosProcesado.Error)]
[InlineData(EstadosProcesado.Pendiente)]
public void Consolidar_FilaIncompletaSinIvaResuelto_SigueRevisando(string estado)
{
    var sinTitulo = Uno(new FilaDocumento { IdSareb = "1", ObjectId = "A", Procesado = estado });
    Assert.Equal(Consolidador.SinTitulo, sinTitulo.HuboIVA);
    Assert.Equal("sí", sinTitulo.Revisar);

    var noConsta = Uno(Titulo("2", "B", "no consta"), new FilaDocumento { IdSareb = "2", ObjectId = "C", Procesado = estado });
    Assert.Equal("sí", noConsta.Revisar);
    Assert.Equal("filas sin resolver (1)", noConsta.MotivoRevisar);
}
```

- [ ] **Paso 2: ejecutar y ver fallar**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~Consolidar_FilaIncompleta"`
Expected: FAIL en `Consolidar_FilaIncompletaConIvaResuelto_DejaElMotivoPeroNoRevisa` (Revisar es "sí").

- [ ] **Paso 3: ajustar `Revisar` en `Consolidador.Consolidar`**

Sustituir `Revisar = motivos.Count > 0 ? ValoresIva.Si : ValoresIva.No` por una variable calculada antes de construir la fila: `revisar = motivos.Count > 0 && !(motivos.Count == 1 && sinResolver > 0 && hubo is ValoresIva.Si or ValoresIva.No)`. Añadir un comentario de una línea con la regla.

- [ ] **Paso 4: cambiar el aviso de `ConsolidarCommand`**

Texto exacto: `Aviso: {incompletas} filas siguen en pendiente, error o timeout; el motivo queda en MotivoRevisar y solo los activos sin IVA resuelto llevan Revisar = sí. Relanza procesar (con --reintentar-errores si procede) antes de dar el resultado por bueno.`

- [ ] **Paso 5: tests completos y formato**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests` → PASS (anotar n/n; 142 actuales, cambia el número de casos de Theory).
Run: `dotnet format DocumentIA.Batch.sln --verify-no-changes` → sin salida.

- [ ] **Paso 6: commit**

```bash
git add src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs src/DocumentIA.Batch.IvaCedente/Commands/ConsolidarCommand.cs tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs
git commit -m "feat(iva-cedente): filas sin resolver no mandan a revisión un activo con IVA resuelto (AB#101211)"
```

---

### Tarea 2: modelo `Discrepancia` y su recogida en `Consolidador`

**Files:**
- Create: `src/DocumentIA.Batch.IvaCedente/Models/Discrepancia.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Models/FilaActivo.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs`
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Produces: `public sealed record Discrepancia(string ObjectId, string? HuboIVA, double? TipoIVA, double? CuotaIVA, string? DeduccionIVA);` en `DocumentIA.Batch.IvaCedente.Models`. `HuboIVA` nulo = no discrepa en IVA; `DeduccionIVA` nulo = no discrepa en deducción. `TipoIVA`/`CuotaIVA` solo informados cuando `HuboIVA == "sí"`.
- Produces: `public List<Discrepancia> Discrepancias { get; set; } = new();` en `FilaActivo` (no va al CSV).

- [ ] **Paso 1: tests**

```csharp
[Fact]
public void Consolidar_IvaContradictorio_RecogeLosTitulosQueDiscrepan()
{
    var a = Uno(Titulo("1", "B", "no"), Titulo("1", "A", "sí", tipo: 21, cuota: 1200), Titulo("1", "C", "no consta"));
    var d = Assert.Single(a.Discrepancias);
    Assert.Equal(new Discrepancia("B", "no", null, null, null), d);
}

[Fact]
public void Consolidar_CuotasDistintas_RecogeLosTitulosConOtraCuota()
{
    var a = Uno(Titulo("1", "A", "sí", tipo: 21, cuota: 100, confianza: 0.9), Titulo("1", "B", "sí", tipo: 21, cuota: 200), Titulo("1", "C", "sí"));
    Assert.Equal("A", a.DocIVA);
    var d = Assert.Single(a.Discrepancias);
    Assert.Equal(new Discrepancia("B", "sí", 21, 200, null), d);
}

[Fact]
public void Consolidar_DeduccionContradictoria_RecogeLosTitulosQueDiscrepan()
{
    var a = Uno(Titulo("1", "A", "sí", "sí"), Titulo("1", "B", "sí", "no"), Titulo("1", "C", "sí", "no aplica"));
    var d = Assert.Single(a.Discrepancias);
    Assert.Equal(new Discrepancia("B", null, null, null, "no"), d);
}

[Fact]
public void Consolidar_TituloQueDiscrepaEnAmbas_UnaSolaEntradaOrdenadaPorObjectId()
{
    var a = Uno(Titulo("1", "Z", "no", "no"), Titulo("1", "A", "sí", "sí", cuota: 100), Titulo("1", "M", "sí", "no", cuota: 300));
    Assert.Equal(new[] { new Discrepancia("M", "sí", null, 300, "no"), new Discrepancia("Z", "no", null, null, "no") }, a.Discrepancias);
}

[Fact]
public void Consolidar_SinMotivosDeDiscrepancia_ListaVacia()
{
    Assert.Empty(Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 100)).Discrepancias);
    Assert.Empty(Uno(Titulo("1", "A", "sí"), new FilaDocumento { IdSareb = "1", ObjectId = "B", Procesado = EstadosProcesado.Error }).Discrepancias);
}
```

- [ ] **Paso 2: ejecutar y ver fallar**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~Discrepa"`
Expected: error de compilación (`Discrepancia` y `Discrepancias` no existen).

- [ ] **Paso 3: crear el record y la propiedad**

`Models/Discrepancia.cs` con el record de Interfaces y un `<summary>` en español. `FilaActivo.Discrepancias` con `<summary>` que diga que no se escribe en el CSV.

- [ ] **Paso 4: recoger las discrepancias en `Consolidador.Consolidar`**

Método privado `static List<Discrepancia> RecogerDiscrepancias(List<FilaDocumento> conRespuesta, string hubo, string deduccion, FilaDocumento? tituloIva, bool ivaContradictorio, bool deduccionContradictoria, bool cuotasDistintas)`:
- Si `ivaContradictorio`: títulos con `HuboIVA` en {sí, no} y distinto de `hubo` → `HuboIVA` informado, tipo y cuota solo si vale sí.
- Si `cuotasDistintas`: títulos con `HuboIVA == sí`, `CuotaIVA` no nula y distinta de `tituloIva.CuotaIVA` → `HuboIVA = "sí"`, tipo y cuota.
- Si `deduccionContradictoria`: títulos con `DeduccionIVA` en {sí, no} y distinta de `deduccion` → `DeduccionIVA` informada.
- Fusionar por `ObjectId` (un título puede aparecer en IVA y en deducción). Ordenar por `ObjectId` ordinal.

Llamarlo tras calcular los motivos y asignar a `Discrepancias` en el inicializador de `FilaActivo`.

- [ ] **Paso 5: tests completos y formato** (mismos comandos que Tarea 1, anotar n/n).

- [ ] **Paso 6: commit**

```bash
git add src/DocumentIA.Batch.IvaCedente/Models/Discrepancia.cs src/DocumentIA.Batch.IvaCedente/Models/FilaActivo.cs src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs
git commit -m "feat(iva-cedente): el consolidador recoge los títulos que discrepan del elegido (AB#101211)"
```

---

### Tarea 3: tramo `; discrepan: …` en `FraseSustento` y manual

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/FraseSustento.cs`
- Modify: `src/DocumentIA.Batch.IvaCedente/README.md` (filas `Sustento`, `MotivoRevisar`, `Revisar` de la tabla del consolidado)
- Modify: `docs/superpowers/specs/2026-10-30-iva-cedente-sustento-discrepancias-design.md` (nota de implementación: solo discrepan valores sí/no)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/FraseSustentoTests.cs`

**Interfaces:**
- Consumes: `FilaActivo.Discrepancias` (Tarea 2).

- [ ] **Paso 1: tests**

```csharp
[Fact]
public void Componer_ConDiscrepanciaDeIva_AnadeElTramo()
{
    var a = new FilaActivo { HuboIVA = "sí", TipoIVA = 21, CuotaIVA = 1200, DocIVA = "A", DeduccionIVA = "no consta" };
    a.Discrepancias.Add(new Discrepancia("B", "no", null, null, null));
    Assert.Equal("IVA sí 21% 1.200,00 € (doc A); deducción no consta; discrepan: doc B IVA no", FraseSustento.Componer(a));
}

[Fact]
public void Componer_VariasDiscrepancias_UnaEntradaPorTituloConSusPartes()
{
    var a = new FilaActivo { HuboIVA = "sí", CuotaIVA = 100, DocIVA = "A", DeduccionIVA = "sí", DocDeduccion = "A" };
    a.Discrepancias.Add(new Discrepancia("M", "sí", 21, 300, "no"));
    a.Discrepancias.Add(new Discrepancia("Z", null, null, null, "no"));
    Assert.EndsWith("; discrepan: doc M IVA sí 21% 300,00 €, deducción no; doc Z deducción no", FraseSustento.Componer(a));
}

[Fact]
public void Componer_SinDiscrepancias_NoAnadeNada()
{
    var a = new FilaActivo { HuboIVA = "no", DocIVA = "9", DeduccionIVA = "no aplica" };
    Assert.Equal("IVA no (doc 9); deducción no aplica", FraseSustento.Componer(a));
}
```

- [ ] **Paso 2: ejecutar y ver fallar**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~FraseSustentoTests"`
Expected: FAIL en los dos primeros (falta el tramo).

- [ ] **Paso 3: implementar**

En `FraseSustento.Componer`, tras componer `frase` y `deduccion`, si `a.Discrepancias.Count > 0` añadir `"; discrepan: " + string.Join("; ", a.Discrepancias.Select(Entrada))`. Método privado `static string Entrada(Discrepancia d)`: `doc {ObjectId}` + partes separadas por `, `: `IVA {HuboIVA}` (con tipo y cuota si HuboIVA es sí, reutilizando el formato existente extraído a un helper `static IEnumerable<string> TipoYCuota(double? tipo, double? cuota)`) y `deducción {DeduccionIVA}`. La rama `sin título previo` no cambia.

- [ ] **Paso 4: README y nota en la spec**

- Fila `Sustento`: añadir `Con discrepancias (IVA contradictorio, deducción contradictoria o cuotas distintas) termina en `; discrepan: doc 456 IVA sí 21% 1.200,00 €, deducción no; doc 789 IVA no`, una entrada por título cuyo valor sí/no o cuya cuota difiere del título elegido.`
- Fila `MotivoRevisar`: sustituir `Vacío cuando Revisar = no.` por `Puede llevar solo `filas sin resolver (n)` con `Revisar = no` cuando el IVA ya está resuelto.`
- Fila `Revisar`: `sí` si hay contradicción (IVA o deducción) o cuotas distintas, o si hay filas sin resolver y `HuboIVA` es `no consta` o `sin título`; `no` en otro caso, también con filas sin resolver cuando `HuboIVA` es `sí` o `no`.
- Spec: bajo "Comportamiento acordado", línea `Nota de implementación (2026-10-30): en IVA contradictorio y deducción contradictoria solo se listan los títulos con valor sí o no; no consta y no aplica no cuentan como discrepancia.`

- [ ] **Paso 5: tests completos, formato y build sin warnings**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests` → PASS n/n.
Run: `dotnet build DocumentIA.Batch.sln` → 0 advertencias.
Run: `dotnet format DocumentIA.Batch.sln --verify-no-changes` → sin salida.

- [ ] **Paso 6: commit** (incluye la spec y el plan, que están sin seguimiento)

```bash
git add src/DocumentIA.Batch.IvaCedente/Engine/FraseSustento.cs src/DocumentIA.Batch.IvaCedente/README.md tests/DocumentIA.Batch.IvaCedente.Tests/FraseSustentoTests.cs docs/superpowers/specs/2026-10-30-iva-cedente-sustento-discrepancias-design.md docs/superpowers/plans/2026-10-30-iva-cedente-sustento-discrepancias.md
git commit -m "feat(iva-cedente): Sustento lista los títulos que discrepan; manual y diseño (AB#101211)"
```

---

### Tarea 4: regenerar el consolidado del lote de PRO (sin llamadas)

**Files:**
- Create (no se commitea, `docs/auxiliares/` no está en git): `docs/auxiliares/temps/2026-10-29/lote-consolidado-v4.csv`

- [ ] **Paso 1: ejecutar `consolidar`** con la sintaxis del README sobre `docs/auxiliares/temps/2026-10-29/lote-salida.csv` y salida `lote-consolidado-v4.csv`.

- [ ] **Paso 2: comparar con v3**: recuento de `Revisar = sí` (v3: 62), cuántos activos pasan a `no` por filas sin resolver, cuántas filas llevan `; discrepan:`; comprobar a ojo dos activos con `IVA contradictorio` y uno con `cuotas de IVA distintas`. Dejar los recuentos en chat.
