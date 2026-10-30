# Plan de implementación · IvaCedente, menores diferidos (AB#101215)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Saldar los cuatro menores reales de la consola IvaCedente (Object ID repetido, cuotas a céntimo, tests de `Sustento` desde `Consolidar`, helper numérico único) y dejar los tres descartes anotados.

**Architecture:** Arreglos puntuales en `Engine/Consolidador.cs` y un helper `CsvUtil.Numero(double?)` que usan los dos escritores de CSV. Sin cambios en modelos, lectores ni `FraseSustento`.

**Tech Stack:** .NET 8, xUnit. Un solo `dotnet` a la vez.

**Spec:** `docs/superpowers/specs/2026-10-30-iva-cedente-menores-design.md`

## Global Constraints

- Ediciones en `.cs` sin BOM y con CRLF: reemplazo por script (python) que conserve los finales de línea; comprobar con `file`.
- Commits con `AB#101215`; sin coautores ni menciones a modelos.
- Build sin advertencias; `dotnet format DocumentIA.Batch.sln --verify-no-changes --include src/DocumentIA.Batch.IvaCedente/ tests/DocumentIA.Batch.IvaCedente.Tests/` limpio; tests 152 + nuevos en verde.
- Punto decimal en los CSV, `CultureInfo.InvariantCulture`; los lectores no cambian.
- Sin llamadas a PRO.

## Review Focus

- Object ID repetido con la primera fila sin discrepancia y la segunda con ella: la primera aparición gana solo si ya está en el diccionario; si la primera no discrepa, la segunda debe entrar (test en Task 1).
- Cuotas `null` frente a cuota informada: el redondeo no debe tocar los nulos ni convertirlos en 0 (test en Task 2).
- Cuota del título elegido `null` con otros títulos con cuota: `cuotasDistintas` solo cuenta títulos con cuota, como hoy (cubierto por `Consolidar_CuotasDistintas_RecogeLosTitulosConOtraCuota`).
- Valores con muchos decimales en el CSV por activo (por ejemplo 0.1+0.2): el helper emite la cadena más corta que hace ida y vuelta, igual en los dos escritores (test en Task 4).
- Consolidado del lote regenerado: solo puede cambiar en activos cuyo motivo `cuotas de IVA distintas` venía de ruido por debajo del céntimo (verificación manual en Task 5).

---

### Task 1: Object ID repetido no pisa la discrepancia anterior

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs:124-146` (`RecogerDiscrepancias`)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Consumes: helpers del test `Titulo(activo, objectId, hubo, deduccion, tipo, cuota, confianza)` y `Uno(params FilaDocumento[])`.
- Produces: nada nuevo; misma firma de `Consolidar`.

- [ ] **Step 1: Escribir los dos tests que fallan**

```csharp
[Fact]
public void Consolidar_ObjectIdRepetido_ConservaLaPrimeraDiscrepancia()
{
    var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 200), Titulo("1", "B", "sí", cuota: 300));
    Assert.Equal(new[] { new Discrepancia("B", "sí", null, 200, null) }, a.Discrepancias);
}

[Fact]
public void Consolidar_ObjectIdRepetido_LaSegundaEntraSiLaPrimeraNoDiscrepa()
{
    var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 100), Titulo("1", "B", "sí", cuota: 300));
    Assert.Equal(new[] { new Discrepancia("B", "sí", null, 300, null) }, a.Discrepancias);
}
```

- [ ] **Step 2: Ejecutar y ver fallar el primero**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~ObjectIdRepetido"`
Expected: 1 FAIL (`B` con cuota 300), 1 PASS.

- [ ] **Step 3: En `RecogerDiscrepancias`, sustituir la asignación `porDoc[f.ObjectId] = ...` por `porDoc.TryAdd(f.ObjectId, new Discrepancia(...))`**

- [ ] **Step 4: Ejecutar y ver pasar**

Run: el mismo filtro. Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs
git commit -m "fix(iva-cedente): un Object ID repetido en el activo conserva su primera discrepancia (AB#101215)"
```

### Task 2: Cuotas comparadas a céntimo

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs:74` (motivo) y `:128` (discrepancia por cuota)
- Modify: `src/DocumentIA.Batch.IvaCedente/README.md` (fila `Revisar` de la tabla del CSV por activo)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Produces: `private static double? ACentimo(double? cuota)` en `Consolidador`: `Math.Round(c, 2, MidpointRounding.AwayFromZero)` si hay valor, `null` si no.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
[Fact]
public void Consolidar_CuotasQueDifierenPorDebajoDelCentimo_NoMarcaRevisar()
{
    var a = Uno(Titulo("1", "A", "sí", cuota: 1200.001), Titulo("1", "B", "sí", cuota: 1200.004));
    Assert.Equal("no", a.Revisar);
    Assert.Empty(a.Discrepancias);
}

[Fact]
public void Consolidar_CuotasQueDifierenEnUnCentimo_MarcaRevisarYRecogeLaDiscrepancia()
{
    var a = Uno(Titulo("1", "A", "sí", cuota: 1200.00), Titulo("1", "B", "sí", cuota: 1200.01));
    Assert.Equal("sí", a.Revisar);
    Assert.Equal(Consolidador.MotivoCuotasDistintas, a.MotivoRevisar);
    Assert.Equal(new[] { new Discrepancia("B", "sí", null, 1200.01, null) }, a.Discrepancias);
}

[Fact]
public void Consolidar_CuotaNulaJuntoACuotaInformada_NoCuentaComoDistinta()
{
    var a = Uno(Titulo("1", "A", "sí", cuota: 100), Titulo("1", "B", "sí"));
    Assert.Equal("no", a.Revisar);
    Assert.Empty(a.Discrepancias);
}
```

- [ ] **Step 2: Ejecutar y ver fallar el primero**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~Cuota"`
Expected: `PorDebajoDelCentimo` FAIL; los demás PASS.

- [ ] **Step 3: Añadir `ACentimo` y usarlo en el `Distinct()` del motivo (línea 74) y en la comparación de la discrepancia (línea 128): `ACentimo(f.CuotaIVA) != ACentimo(tituloIva?.CuotaIVA)`. La `Discrepancia` sigue guardando la cuota original.**

- [ ] **Step 4: Ejecutar y ver pasar**

Run: el mismo filtro. Expected: todos PASS.

- [ ] **Step 5: README: en la fila `Revisar`, "cuotas de IVA distintas entre los títulos con IVA" pasa a "cuotas de IVA distintas (comparadas a céntimo) entre los títulos con IVA".**

- [ ] **Step 6: Commit**

```bash
git add src/DocumentIA.Batch.IvaCedente/Engine/Consolidador.cs src/DocumentIA.Batch.IvaCedente/README.md tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs
git commit -m "fix(iva-cedente): las cuotas de IVA se comparan a céntimo (AB#101215)"
```

### Task 3: Tests de `Sustento` desde `Consolidar`

**Files:**
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs`

**Interfaces:**
- Consumes: parámetros `cedente` y `fecha` del helper `Titulo(...)`.

- [ ] **Step 1: Escribir los dos tests (deben pasar ya; si alguno falla, el fallo es un defecto real y se para a investigar)**

```csharp
[Fact]
public void Consolidar_Sustento_FraseCompletaSinDiscrepancias()
{
    var a = Uno(Titulo("1", "12345", "sí", "sí", tipo: 21, cuota: 64050, cedente: "BANCO X", fecha: "2008-03-12"), Titulo("1", "99", "no consta"));
    Assert.Equal("IVA sí 21% 64.050,00 € (doc 12345, cedente BANCO X, 2008-03-12); deducción sí (doc 12345)", a.Sustento);
}

[Fact]
public void Consolidar_Sustento_FraseCompletaConTramoDeDiscrepancias()
{
    var a = Uno(Titulo("1", "A", "sí", "no", tipo: 21, cuota: 1200), Titulo("1", "B", "sí", "no", tipo: 21, cuota: 1300), Titulo("1", "C", "no", "no"));
    Assert.Equal("IVA sí 21% 1.200,00 € (doc A); deducción no (doc A); discrepan: doc B IVA sí 21% 1.300,00 €; doc C IVA no", a.Sustento);
}
```

- [ ] **Step 2: Ejecutar**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~Consolidar_Sustento"`
Expected: 2 PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/DocumentIA.Batch.IvaCedente.Tests/ConsolidadorTests.cs
git commit -m "test(iva-cedente): la frase Sustento se comprueba completa desde Consolidar (AB#101215)"
```

### Task 4: Helper numérico único para los dos CSV

**Files:**
- Modify: `src/DocumentIA.Batch.IvaCedente/Io/CsvUtil.cs` (añadir), `Io/SalidaDocumentosCsv.cs:85-86` (quitar `Numero(double?)`, usar el de CsvUtil), `Io/SalidaActivosCsv.cs:37` (igual)
- Test: `tests/DocumentIA.Batch.IvaCedente.Tests/CsvUtilTests.cs`

**Interfaces:**
- Produces: `public static string Numero(double? valor)` en `CsvUtil`: `valor?.ToString(CultureInfo.InvariantCulture) ?? ""`.

- [ ] **Step 1: Escribir el test que falla**

```csharp
[Theory]
[InlineData(null, "")]
[InlineData(21, "21")]
[InlineData(1200.5, "1200.5")]
[InlineData(0.95, "0.95")]
[InlineData(0.30000000000000004, "0.30000000000000004")]
public void Numero_EmiteConPuntoYLaCadenaMasCorta(double? valor, string esperado)
    => Assert.Equal(esperado, CsvUtil.Numero(valor));
```

- [ ] **Step 2: Ejecutar y ver fallar (no compila: `Numero` no existe)**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests --filter "FullyQualifiedName~Numero_Emite"`

- [ ] **Step 3: Añadir `CsvUtil.Numero` (con `using System.Globalization;`), borrar los `Numero(double?)` privados de los dos escritores y llamar a `CsvUtil.Numero` en su lugar. El `Numero(string)` lector de `SalidaDocumentosCsv` se queda.**

- [ ] **Step 4: Ejecutar todos los tests del proyecto**

Run: `dotnet test tests/DocumentIA.Batch.IvaCedente.Tests`
Expected: 152 + 12 = 164 PASS (los tests existentes de los escritores siguen en verde).

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIA.Batch.IvaCedente/Io/CsvUtil.cs src/DocumentIA.Batch.IvaCedente/Io/SalidaDocumentosCsv.cs src/DocumentIA.Batch.IvaCedente/Io/SalidaActivosCsv.cs tests/DocumentIA.Batch.IvaCedente.Tests/CsvUtilTests.cs
git commit -m "refactor(iva-cedente): un solo formato numérico para los dos CSV (AB#101215)"
```

### Task 5: Verificación de cierre y descartes

**Files:** ninguno nuevo. Salida temporal en `docs/auxiliares/temps/2026-10-30/lote-consolidado-v5.csv`.

- [ ] **Step 1: Build y formato**

Run: `dotnet build DocumentIA.Batch.sln` → 0 advertencias. Después: `dotnet format DocumentIA.Batch.sln --verify-no-changes --include src/DocumentIA.Batch.IvaCedente/ tests/DocumentIA.Batch.IvaCedente.Tests/` → sin salida, exit 0.

- [ ] **Step 2: Regenerar el consolidado del lote sin PRO**

Run: `dotnet run --project src/DocumentIA.Batch.IvaCedente -- consolidar --entrada docs/auxiliares/temps/2026-10-29/lote-salida.csv --salida docs/auxiliares/temps/2026-10-30/lote-consolidado-v5.csv`
Comparar con `lote-consolidado-v4.csv`: `diff` vacío o solo activos cuyo `MotivoRevisar` pierde `cuotas de IVA distintas` por diferencias inferiores al céntimo. Anotar el resultado en el traspaso.

- [ ] **Step 3: Comentario de cierre en AB#101215 (escritura en ADO, con sí explícito del usuario)**: commits, tests n/n, resultado del diff y los tres descartes con motivo (orden de motivos determinista y testado; `Tipologia` escribe lo que devuelve el core, decisión 2026-10-29; confianza fija aceptada como limitación, decisión 2026-10-29).

- [ ] **Step 4: Revisión final de rama (subagente, Opus) sobre `main..feature/101215-iva-cedente-menores`; integrar en `main` por fast-forward y push con sí explícito.**
