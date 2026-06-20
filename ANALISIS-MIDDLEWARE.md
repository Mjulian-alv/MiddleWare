# Middleware de Transformación de Datos — Análisis, Diseño y Roadmap

> **Origen de la idea:** el proyecto `ConsultasGenericas` (BI/dashboards) resolvió sin proponérselo la mitad difícil de un middleware de integración: la **lectura desde orígenes heterogéneos** (SQL Server, MySQL, MariaDB, Oracle, Excel, REST) expuesta de forma uniforme. Este documento define cómo convertir esa base en un **middleware de transformación de datos para otros sistemas**, en una **solución separada** que **no toque** ni desestabilice el sistema actual.
>
> **Estado:** diseño aprobado. Listo para escribir el plan de implementación.
> **Fecha de análisis original:** 2026-06-07.
> **Fecha de esta revisión:** 2026-06-19 — incorpora los avances del proyecto base y el diseño definitivo acordado.
> **Premisa rectora:** *separar para no romper.* El sistema de dashboards sigue intacto; a lo sumo es **un origen más**.

---

## Tabla de contenidos
1. [Visión y objetivo](#1-visión-y-objetivo)
2. [Avances del proyecto base desde el análisis original](#2-avances-del-proyecto-base-desde-el-análisis-original)
3. [Inventario de activos reutilizables](#3-inventario-de-activos-reutilizables-del-proyecto-actual)
4. [Análisis de brechas (qué falta hoy)](#4-análisis-de-brechas--qué-falta-hoy)
5. [Estrategia adoptada](#5-estrategia-adoptada-opción-1)
6. [Arquitectura objetivo](#6-arquitectura-objetivo)
7. [Flujo de datos y los dos almacenes](#7-flujo-de-datos-y-los-dos-almacenes)
8. [Modelo de dominio](#8-modelo-de-dominio)
9. [El modelo de streaming (la brecha dura)](#9-el-modelo-de-streaming--la-brecha-dura)
10. [Conectores: sources y data stores](#10-conectores-sources-y-data-stores)
11. [Transformaciones](#11-transformaciones)
12. [Motor / orquestador](#12-motor--orquestador)
13. [Scheduling y triggers](#13-scheduling-y-triggers)
14. [Encadenamiento de pipelines](#14-encadenamiento-de-pipelines)
15. [Entrega de datos (PULL ahora, PUSH después)](#15-entrega-de-datos-pull-ahora-push-después)
16. [Interfaz de usuario (DataPipeline.Admin)](#16-interfaz-de-usuario-datapipelineadmin)
17. [Confiabilidad](#17-confiabilidad)
18. [Observabilidad](#18-observabilidad)
19. [Aislamiento y estabilidad](#19-aislamiento-y-estabilidad-la-preocupación-central)
20. [Hoja de ruta por fases](#20-hoja-de-ruta-por-fases)
21. [Decisiones tomadas](#21-decisiones-tomadas-decision-log)
22. [Riesgos y mitigaciones](#22-riesgos-y-mitigaciones)
23. [Apéndices](#23-apéndices)

---

## 1) Visión y objetivo

**Hoy:** `ConsultasGenericas` lee datos de muchos orígenes y los **expone/visualiza** (endpoints HTTP autenticados + dashboards). Es, en esencia, la etapa **Extract** de un ETL más una capa de presentación.

**Visión:** un **middleware** que, además de leer, **transforme** y **entregue** datos a **otros sistemas**, mediante **pipelines** (`origen → transformación → tabla propia → entrega`) **encadenables**, programables y monitoreables. Esto es territorio **ETL / integración de datos / iPaaS liviano**.

**Casos de uso típicos:**
- Replicar/sincronizar datos de una BD origen a otra (con mapeo de esquema).
- Exponer datos transformados a otros sistemas (pull por GET ahora; push por POST después).
- Consolidar varios orígenes en un destino unificado (staging/datawarehouse).
- Generar tablas listas para consumo de sistemas batch.
- Disparar entregas por programación (cron) y, más adelante, por evento (webhook) o encadenamiento.

**No-objetivos (al menos al inicio):** reemplazar un datawarehouse, hacer CDC en tiempo real de baja latencia, ni competir con un iPaaS empresarial. Empezar **acotado y estable**.

---

## 2) Avances del proyecto base desde el análisis original

Entre el análisis original (2026-06-07) y esta revisión, `ConsultasGenericas` evolucionó y **ya resolvió en la práctica varias de las brechas que el MD marcaba como faltantes**. Esto cambia la estrategia: hay código probado del que partir, no solo teoría.

| Avance detectado | Ubicación | Qué desbloquea para el middleware |
|---|---|---|
| **Scheduler de larga vida** (`BackgroundService` real, en uso) | `Api/Services/EmailSchedulerHostedService.cs` | El patrón "Worker programado" deja de ser teoría: loop cada 60s, busca tareas vencidas (`NextRunAt <= now`), ejecuta, **recalcula `NextRunAt` en `finally`** (anti-bucle), `scope` de `DbContext` por ciclo, delay inicial de arranque. Lecciones ya pagadas. |
| **Cálculo de próxima corrida** | `Api/Services/EmailScheduleCalculator.cs` | Frecuencias Daily/Weekly/Monthly + hora del día. Molde directo para el scheduling de pipelines (sin necesidad de Quartz/Hangfire al inicio). |
| **Pipeline de reporte/entrega funcionando** | `Api/Services/DashboardReportService.cs` | Es, en esencia, un pipeline real: **Source** (corre endpoints vía `QueryDispatcherService`) → **Transform** (pivot/agregaciones, formato) → **Sink** (Excel con ClosedXML, gráficos a imagen/PDF con SkiaSharp) → **Delivery** (`EmailService`). Demuestra que la mecánica de sinks ya existe. |
| **Parámetros de corrida / presets** | `Core/Enums/ReportParameterPreset.cs`, `EmailSchedule.ParameterValuesJson` | Resuelve rangos de fecha relativos (ej. "Ayer") a `desde/hasta` en tiempo de ejecución + diccionario JSON de parámetros fijos. Es el concepto de `RunContext`/parámetros de la corrida. |
| **Transformaciones de agregación** | `Core/DTOs/PivotConfigDto.cs`, `Core/Enums/PivotAggregate.cs` | Sum/Count/Average/Max/Min ya implementadas. La **lógica matemática** de agregación es reutilizable como transformación (la UI del cubo no). |
| **Gestión de SMTP / secretos** | `Core/Entities/SmtpConfig.cs` | Config persistida de credenciales (combinable con AES-256). |
| **Patrón Admin completo y repetible** | `ConsultasGenericas.Admin/*` | Blazor Server + **Syncfusion** (no MudBlazor). Molde calcable para el diseñador de pipelines (ver sección 16). |

**Lo que sigue intacto como brecha dura:** el **streaming**. Los ejecutores actuales siguen haciendo `QueryAsync` → `List` completa (`QueryResultDto.FromDapper`). El punto técnico central del MD (sección 9) hay que escribirlo de cero. Ver sección 4.

---

## 3) Inventario de activos reutilizables (del proyecto actual)

Ruta base del proyecto actual: `E:\Sistemas\ConsultasGenericas` (el análisis original decía `D:`; el disco cambió a `E:`).

| Activo | Ubicación actual | Reutilización |
|---|---|---|
| `DataSource` (entidad: tipo, connection string, user/pass, BaseUrl, FilePath) | `ConsultasGenericas.Core/Entities/DataSource.cs` | ✅ **directo** |
| `DataSourceType` (MySQL, MariaDB, SqlServer, Oracle, Excel, RestApi) | `ConsultasGenericas.Core/Enums/DataSourceType.cs` | ✅ directo |
| `IQueryExecutor` (contrato de lectura) | `ConsultasGenericas.Core/Interfaces/IQueryExecutor.cs` | ✅ punto de partida; se reemplaza por `ISource` en streaming |
| `IEncryptionService` + `EncryptionService` (AES-256) | `Core/Interfaces/IEncryptionService.cs` + `Api/Services/EncryptionService.cs` | ✅ **extraer a librería** |
| Ejecutores concretos (6) | `Api/Services/QueryExecutors/*.cs` | ✅ **extraer + adaptar a streaming** |
| `QueryDispatcherService` (elige ejecutor + desencripta) | `Api/Services/QueryDispatcherService.cs` | ✅ patrón de fábrica reutilizable |
| `EmailSchedulerHostedService` (scheduler probado) | `Api/Services/EmailSchedulerHostedService.cs` | ✅ **molde del scheduler de pipelines** |
| `EmailScheduleCalculator` (próxima corrida) | `Api/Services/EmailScheduleCalculator.cs` | ✅ molde de cálculo de cron/frecuencia |
| `DashboardReportService` (pipeline reporte→entrega) | `Api/Services/DashboardReportService.cs` | ✅ **referencia de cómo se ve un pipeline real con sinks** |
| Agregaciones de pivot | `Core/DTOs/PivotConfigDto.cs` + `Enums/PivotAggregate.cs` | 🤔 lógica de agregación reutilizable como transform (opcional) |
| `ResultColumnDto` (nombre + tipo inferido) | `Core/DTOs/ResultColumnDto.cs` | ✅ describe esquema de un dataset |
| Parámetros `@param` (Dapper) y `{param}` (ruta) | `Api` (QueryController) | ✅ patrón reutilizable |
| `ConnectionTestService` (probar conexión) | `Api/Services/ConnectionTestService.cs` | ✅ útil para validar configs |
| Patrón Admin (Blazor Server + **Syncfusion**, CRUD, formularios, auth) | `ConsultasGenericas.Admin/*` | ✅ **molde del diseñador de pipelines** |

### Datos arquitectónicos clave
- **`ConsultasGenericas.Core` NO tiene dependencias externas** (ni EF, ni Dapper, ni drivers). Es modelos/interfaces puros → **referenciarlo es barato y de bajo riesgo**.
- **Los ejecutores y servicios viven en `Api`, no en `Core`.** Dependen de: `Dapper`, `Microsoft.Data.SqlClient`, `MySqlConnector`, `Oracle.ManagedDataAccess.Core`, `ClosedXML`, `HttpClient`. Para reusarlos hay que **extraer la mecánica a una librería de conectores** (no arrastrar todo el `Api`, que trae EF/controllers).
- **El Admin habla con la API por `HttpClient`**, nunca toca la BD directo. Patrón a calcar.

### Limitación a corregir al portar
El ejecutor actual (ej. `SqlServerQueryExecutor`) hace `conexion.QueryAsync(...)` de **Dapper que materializa TODAS las filas** en una `List`. En este proyecto eso ya causó cuelgues con 60.000–120.000 filas. **Para ETL hay que leer por streaming** (`DbDataReader`/`IAsyncEnumerable`), nunca cargar todo. Ver sección 9.

---

## 4) Análisis de brechas — qué falta hoy

Tras los avances de la sección 2, el cuadro de brechas se actualiza así:

| # | Capacidad | Estado | Comentario |
|---|---|---|---|
| 1 | **Streaming** | ❌ **falta (brecha dura)** | Lo más crítico. Hay que escribirlo de cero (sección 9). |
| 2 | **Sinks de escritura a BD** (insert/upsert/merge) | ❌ falta | El sink interno (tabla propia) se diseña nuevo vía `IDataStore`. |
| 3 | **Motor de pipeline genérico** (encadenable) | ❌ falta | `PipelineRunner` nuevo (sección 12). |
| 4 | **Scheduling de pipelines** | 🟡 patrón probado | Generalizar `EmailSchedulerHostedService`. |
| 5 | **Transformaciones genéricas** (rename/cast/filter/computed) | 🟡 parcial | Existe agregación (pivot); falta el resto config-driven. |
| 6 | **Confiabilidad** (reintentos, dead-letter, idempotencia, watermark) | ❌ falta | Fase posterior (Polly, etc.). |
| 7 | **Observabilidad** (historial de corridas, filas in/out) | 🟡 patrón probado | Reusar el concepto del scheduler/`LastRunAt`. |
| 8 | **Encadenamiento de pipelines** | ❌ falta | `RunAfter` + `DataStoreSource` (sección 14). |
| 9 | **UI de diseño de pipelines** | 🟡 patrón probado | Calcar el Admin actual (sección 16). |

---

## 5) Estrategia adoptada (Opción 1)

Se evaluaron tres caminos:

| Opción | Descripción | Veredicto |
|---|---|---|
| **A. Solución nueva sembrada con lo probado** ⭐ | Solución separada `DataPipeline`, sembrada copiando/generalizando los patrones que ya funcionan (scheduler, mecánica de sinks, encriptación, exposición por GET). El streaming y las abstracciones del pipeline se escriben **limpios desde cero** (no hay nada probado que copiar ahí). | ✅ **ELEGIDA** |
| B. Motor ETL genérico de cero | Diseñar todo (incluido scheduler y sinks) desde cero, reusando solo `Core` + conectores. Más prolijo, más trabajo, riesgo de reintroducir bugs ya resueltos. | Descartada |
| C. Extender `ConsultasGenericas` | Agregar middleware dentro del proyecto actual. Máximo reuso pero **rompe "separar para no romper"**. | Descartada |

**Razón de la elección:** para un proyecto cuyo principio rector es minimizar riesgo y no desestabilizar, **partir de patrones probados** (sobre todo en scheduling y manejo de errores, donde más duele equivocarse) supera a la prolijidad de empezar de cero. El streaming —única parte sin precedente— se escribe limpio igual en cualquier opción.

---

## 6) Arquitectura objetivo

Solución **NUEVA e independiente** (`.slnx` propio en `E:\Sistemas\MiddleWare`), separada de `ConsultasGenericas`:

```
DataPipeline/
├── DataPipeline.Core         ← dominio puro: PipelineDefinition, StepDefinition,
│                                RecordBatch, contratos (ISource/ITransform/IDataStore),
│                                RunContext, RetryPolicy. Sin drivers.
│                                (opcional: referenciar ConsultasGenericas.Core para DataSource/enums)
├── DataPipeline.Connectors   ← Sources en streaming (SqlServer/REST/Excel/… adaptados) +
│                                IDataStore (SqliteDataStore ahora; SqlServerDataStore después) +
│                                DataStoreSource (lee tablas de otro pipeline) +
│                                EncryptionService extraído.
├── DataPipeline.Engine       ← PipelineRunner (source→transform→store por lotes) +
│                                Scheduler genérico (patrón EmailScheduler) +
│                                historial de corridas + encolado de dependientes.
├── DataPipeline.Api          ← EL DEPLOYABLE: entrega PULL (GET autenticado) + on-demand +
│                                API de administración. El scheduler corre como HostedService
│                                dentro de este host (igual que EmailScheduler hoy).
└── DataPipeline.Admin        ← Blazor Server + Syncfusion (patrón calcado del Admin actual):
                                 diseñador de pipelines, disparo manual, historial.
                                 Habla con DataPipeline.Api por HttpClient.
```

**Decisión de simplicidad:** se replica el patrón `Api + Admin` de `ConsultasGenericas`. El **scheduler vive como `HostedService` dentro del host de `DataPipeline.Api`** (igual que `EmailSchedulerHostedService` corre dentro de la API actual) → menos piezas que desplegar. El aislamiento se cumple igual porque es **otra app, otro proceso y otra BD** que `ConsultasGenericas`. Separar un `DataPipeline.Worker` independiente para correr el scheduler en su propio proceso queda como optimización posterior, si una pipeline pesada llegara a competir con la API de entrega.

**Responsabilidades:**
- **Core:** contratos y modelos. Sin dependencias de drivers.
- **Connectors:** lectura (sources en streaming), persistencia (`IDataStore`), y el `DataStoreSource` que lee tablas producidas por otros pipelines.
- **Engine:** toma una `PipelineDefinition`, instancia los pasos, los conecta por *streaming* de lotes, persiste el resultado en la tabla destino, maneja estado/historial/encadenamiento.
- **Api:** host deployable; expone la entrega PULL, el disparo on-demand y la API de administración; hospeda el scheduler.
- **Admin:** UI para diseñar/monitorear sin tocar JSON a mano; consume `DataPipeline.Api`.

---

## 7) Flujo de datos y los dos almacenes

El diseño introduce una **tabla intermedia propia** como punto de integración (el análisis original pensaba el pipeline como streaming directo origen→destino; esta tabla desacopla extracción de entrega y es además el punto de enlace para encadenar — sección 14).

```
        EXTRACCIÓN (programada)                      ENTREGA
┌──────────────────────────────────┐      ┌─────────────────────────────┐
│  Source (SQL/REST/Excel, stream)  │      │  PULL (ahora): el sistema    │
│            │                      │      │  consumidor hace GET a        │
│            ▼                      │      │  /api/data/{pipeline} y se    │
│      Transform(s)                 │      │  lleva los datos              │
│            │                      │      │                             │
│            ▼                      │      │  PUSH (después): el middleware│
│   Sink interno → TABLA propia  ───┼──────┼──►  programa un POST al        │
│   (almacén B, vía IDataStore)     │      │  sistema destino             │
└──────────────────────────────────┘      └─────────────────────────────┘
```

**Por qué la tabla intermedia es buena:**
1. **Desacopla extracción de entrega.** La extracción pesada corre en horario valle y deja el dato "listo". El GET/POST leen de **nuestra** tabla controlada (indexada, paginada), **nunca golpean el origen en vivo** → ataca de frente el riesgo de carga sobre BDs compartidas.
2. **Simplifica el streaming.** El streaming crítico es solo `origen → tabla` (la extracción). El GET/POST ya leen de tabla propia.
3. **Reusa lo que `ConsultasGenericas` hace bien:** exponer una tabla por GET autenticado. El modo PULL es casi gratis.
4. **Es la costura del encadenamiento** (sección 14).

### Los dos almacenes (decisión clave)

No mezclar; tienen requisitos distintos:

| | **A) BD de metadatos/estado** | **B) Almacén de datos (landing/serving)** |
|---|---|---|
| Qué guarda | `PipelineDefinition`, `StepDefinition`, `PipelineRun` (historial) | Datos de negocio ya transformados (las tablas que expone el GET) |
| Esquema | Fijo y conocido → **entidades EF Core** | **Dinámico** (columnas según cada origen) |
| Volumen | Chico | Potencialmente grande |
| Acceso | EF Core normal | Crear tabla + bulk insert + leer paginado, detrás de **`IDataStore`** |
| Dev | SQLite | SQLite |
| Producción | portable casi gratis vía EF | `SqlServerDataStore` (menor fricción: ya hay SQL Server en el entorno) o `PostgresDataStore` |

**Regla de diseño que hace barato escalar:** **todo el acceso al almacén B va detrás de `IDataStore`** (`EnsureTable / WriteBatchAsync / ReadPagedAsync`), con una implementación por motor. **Nada de SQL específico de SQLite suelto en el motor.** El día que se escale, se escribe `SqlServerDataStore`/`PostgresDataStore` y se cambia el conector, sin reescribir el Engine.

**Límites de SQLite a tener presentes (no muerden en dev, sí en producción):** un solo escritor (concurrencia scheduler+GET), tipado laxo (sin `decimal`/`date` reales), y volumen acotado.

---

## 8) Modelo de dominio

Definición declarativa (config) + contratos de runtime.

> **Convención de idioma (Opción 2):** los **enums** (tipo + valores) y las **propiedades** de los tipos de dominio/config (`PipelineDefinition`, `StepDefinition`) van **en castellano**; las **interfaces, tipos técnicos de runtime** (`Record`, `RecordBatch`, `RunContext`), **servicios** y **métodos** quedan **en inglés** con los modismos .NET (`I`, `Async`, `CancellationToken`). Sin tildes ni `ñ` en identificadores. La referencia canónica de nombres está en el plan de Fase 1 (`docs/superpowers/plans/2026-06-19-datapipeline-fase1.md`, sección Global Constraints → "Mapa de nombres").

### Definición (lo que se persiste / edita — almacén A)
```csharp
public sealed class PipelineDefinition
{
    public Guid    Id { get; set; }
    public string  Nombre { get; set; } = "";
    public bool    Habilitado { get; set; } = true;

    // Programación (molde de EmailSchedule): frecuencia + hora, con ProximaCorrida persistida.
    public TipoProgramacion Programacion { get; set; } = TipoProgramacion.BajoDemanda;
    public string? HoraDelDia { get; set; }         // "HH:mm"
    public int?    DiaDeLaSemana { get; set; }      // Semanal
    public int?    DiaDelMes { get; set; }          // Mensual
    public DateTime? ProximaCorrida { get; set; }
    public DateTime? UltimaCorrida { get; set; }

    public List<StepDefinition> Pasos { get; set; } = new();    // ordenados (Origen + Transformaciones)
    public string TablaDestino { get; set; } = "";             // tabla destino en el almacén B
    public ModoEscritura ModoEscritura { get; set; } = ModoEscritura.Reemplazar;

    public List<Guid> CorrerDespuesDe { get; set; } = new();    // encadenamiento (sección 14)

    public int     FilasPorLote { get; set; } = 5_000;
    // Nota: RetryPolicy Retry se agrega en Fase 4 (confiabilidad).
}

public sealed class StepDefinition
{
    public string Nombre { get; set; } = "";
    public TipoPaso Tipo { get; set; }                 // Origen | Transformacion
    public string  Conector { get; set; } = "";        // ej: "SqlServerSource", "DataStoreSource", "ColumnMapTransform"
    public Dictionary<string, string> Configuracion { get; set; } = new();
}

public enum TipoPaso { Origen, Transformacion }   // el "Sink" interno es fijo: escribir a TablaDestino
public enum TipoProgramacion { BajoDemanda, Diaria, Semanal, Mensual }
public enum ModoEscritura { Agregar, Combinar, Reemplazar }
```

> El **sink interno** (escribir a `TablaDestino` vía `IDataStore`) es implícito y fijo en todo pipeline. Por eso los `Pasos` son **Origen + Transformaciones**. La entrega (PULL/PUSH) es una capa aparte sobre la tabla.

### Runtime: el dato en vuelo
```csharp
public sealed class Record
{
    public IReadOnlyDictionary<string, object?> Values { get; init; } = ...;
}

public sealed class RecordBatch
{
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Record> Rows { get; init; } = Array.Empty<Record>();
}
```

### Contratos de pasos (todo en streaming)
```csharp
public interface ISource
{
    IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, CancellationToken ct);
}

public interface ITransform
{
    IAsyncEnumerable<RecordBatch> TransformAsync(IAsyncEnumerable<RecordBatch> input, RunContext ctx, CancellationToken ct);
}

// El sink interno: persistencia en el almacén B (esquema dinámico).
public interface IDataStore
{
    Task EnsureTableAsync(string table, IReadOnlyList<string> columns, RunContext ctx, CancellationToken ct);
    Task WriteBatchAsync(string table, RecordBatch batch, WriteMode mode, RunContext ctx, CancellationToken ct);
    Task<RecordBatch> ReadPagedAsync(string table, IReadOnlyDictionary<string,string> filtros, int page, int pageSize, CancellationToken ct);
}
```

`RunContext` lleva: `PipelineId`, `RunId`, parámetros de la corrida (presets de fecha estilo `ReportParameterPreset`), watermark previo, logger, métricas y el `IEncryptionService` para desencriptar credenciales.

**Por qué `IAsyncEnumerable<RecordBatch>`:** encadena `Source → Transform → IDataStore` como una tubería *pull* perezosa; la memoria se mantiene acotada (un par de lotes a la vez) sin importar el tamaño del origen.

---

## 9) El modelo de streaming — la brecha dura

**Problema real ya observado:** los ejecutores actuales hacen `QueryAsync` → `List` con **todas** las filas. Con 60k–120k filas ya fue pesado. Para ETL, inviable. **Esta es la única parte sin precedente probado: se escribe de cero.**

**Solución:** leer con `DbDataReader` y emitir lotes:
```csharp
// Sketch de un SqlServerSource en streaming
public async IAsyncEnumerable<RecordBatch> ReadAsync(RunContext ctx, [EnumeratorCancellation] CancellationToken ct)
{
    await using var con = new SqlConnection(ConnStr);
    await con.OpenAsync(ct);
    await using var cmd = new SqlCommand(Sql, con) { CommandTimeout = 0 };
    // bind params (@desde, @hasta, watermark...) desde ctx
    await using var rd = await cmd.ExecuteReaderAsync(ct);

    var cols = Enumerable.Range(0, rd.FieldCount).Select(rd.GetName).ToArray();
    var buffer = new List<Record>(BatchSize);
    while (await rd.ReadAsync(ct))
    {
        var values = new Dictionary<string, object?>(cols.Length);
        for (int i = 0; i < cols.Length; i++)
            values[cols[i]] = rd.IsDBNull(i) ? null : rd.GetValue(i);
        buffer.Add(new Record { Values = values });

        if (buffer.Count >= BatchSize)
        {
            yield return new RecordBatch { Columns = cols, Rows = buffer };
            buffer = new List<Record>(BatchSize);
        }
    }
    if (buffer.Count > 0)
        yield return new RecordBatch { Columns = cols, Rows = buffer };
}
```

**Reglas de oro de streaming:**
- Nunca `ToList()` del dataset completo.
- `CommandTimeout` adecuado para extracciones largas.
- Lotes configurables (`BatchSize`, ej. 5.000).
- Backpressure natural por ser `IAsyncEnumerable` (pull).
- Cancelación cooperativa (`CancellationToken`) en todos lados.

---

## 10) Conectores: sources y data stores

### Sources (lectura) — extraer de `Api/Services/QueryExecutors/`, adaptar a streaming
| Origen | Driver | Acción |
|---|---|---|
| SqlServer | `Microsoft.Data.SqlClient` | Portar a streaming (`DbDataReader`) |
| MySQL / MariaDB | `MySqlConnector` | idem |
| Oracle | `Oracle.ManagedDataAccess.Core` | idem |
| Excel | `ClosedXML` | leer por filas/hoja (cuidar memoria con XLSX grandes) |
| REST API | `HttpClient` | paginación / cursor / streaming JSON (`Utf8JsonReader`) |
| **DataStoreSource** | (propio) | **lee una tabla del almacén B producida por otro pipeline** → encadenamiento (sección 14) |

> Mantener la firma de config compatible con `DataSource` actual (tipo, connection string, etc.) para reusar definiciones ya cargadas.

### Data stores (escritura interna + lectura para entrega) — NUEVOS, detrás de `IDataStore`
| Implementación | Cuándo |
|---|---|
| `SqliteDataStore` | Desarrollo y volúmenes chicos |
| `SqlServerDataStore` | Producción (menor fricción: SQL Server ya en el entorno) |
| `PostgresDataStore` | Alternativa de producción |

Modos de escritura: `Append` | `Upsert(porClave)` | `Replace(truncate+load)`.

### Patrón de fábrica
Un `ISourceFactory` / `IDataStoreFactory` que, dado `StepDefinition.Type` + `Config`, instancian el conector concreto (igual que `QueryDispatcherService` hoy elige ejecutor por `DataSourceType`).

---

## 11) Transformaciones

Tres enfoques (combinables):

1. **Config-driven (mapeo declarativo)** — el de arranque.
   - JSON: `{ "rename": {"total":"monto"}, "cast": {"monto":"decimal"}, "filter": "monto > 0", "computed": {"neto":"monto - bonif"} }`.
   - Cubre el 80% (renombrar, castear, filtrar, columnas calculadas).
   - Mini-evaluador de expresiones (`NCalc`, `System.Linq.Dynamic`, o propio).
   - **Reusable:** la lógica de agregación del pivot (Sum/Count/Avg/Max/Min) ya existe en `ConsultasGenericas`.

2. **SQL sobre staging** — potente para joins/agregaciones.
   - El source aterriza a una tabla *staging* (almacén B); un paso SQL ejecuta `SELECT ... JOIN ... GROUP BY`; el resultado va a la tabla final. Encaja naturalmente con la tabla intermedia.

3. **Scripting (C# / plugin)** — máxima flexibilidad, para casos puntuales (queda para el final).

**Arranque:** (1) config-driven para lo común + (2) SQL-staging para joins/agregaciones.

---

## 12) Motor / orquestador

`PipelineRunner` toma una `PipelineDefinition` y arma la tubería:
```csharp
IAsyncEnumerable<RecordBatch> stream = source.ReadAsync(ctx, ct);
foreach (var t in transforms)        // en orden
    stream = t.TransformAsync(stream, ctx, ct);

await dataStore.EnsureTableAsync(def.TargetTable, /* columnas */, ctx, ct);
await foreach (var batch in stream.WithCancellation(ct))
    await dataStore.WriteBatchAsync(def.TargetTable, batch, def.WriteMode, ctx, ct);
```
Responsabilidades del runner:
- Resolver pasos (factories) a partir de `Steps`.
- Inyectar `RunContext` (params, watermark, secretos, logger, métricas).
- Persistir en `TargetTable` por lotes vía `IDataStore`.
- Política de **reintentos** (a nivel corrida y/o lote) con backoff (fase posterior).
- **Watermark**: leer/guardar el corte incremental (fase posterior).
- Cierre: marcar la corrida (OK/Failed), filas in/out, duración, error → `PipelineRun`.

---

## 13) Scheduling y triggers

**Molde:** generalizar `EmailSchedulerHostedService` (no hace falta Quartz/Hangfire al inicio).

- **`PipelineSchedulerHostedService`** (BackgroundService): cada ~60s busca pipelines vencidos (`Enabled && NextRunAt <= now`), lanza la corrida en el Engine, y **recalcula `NextRunAt` en `finally`** (anti-bucle, lección probada).
- **`ScheduleCalculator`** (molde de `EmailScheduleCalculator`): Daily/Weekly/Monthly + hora del día.
- **On-demand:** endpoint "correr ahora" + botón en el Admin.
- **Encadenado:** al cerrar una corrida OK, encolar dependientes (sección 14).
- **Webhook / file watcher:** fase posterior.

---

## 14) Encadenamiento de pipelines

Era una preocupación central del análisis original (estabilidad al encadenar procesos). Hay **dos niveles**:

**1. Intra-pipeline:** `source → transform → transform → store`. Ya resuelto por la composición `IAsyncEnumerable` del runner (sección 12).

**2. Inter-pipeline:** A y después B. Aquí la **tabla de landing es el punto de enlace**. Dos formas, ortogonales y combinables:

```
   ENLACE DE DATOS                          ENLACE DE CONTROL
   (B consume lo de A)                       (B corre después de A)

   Pipeline A                                Pipeline A  ──corre OK──┐
     └─► tabla_A  ◄──┐                                              │ RunAfter
                     │ DataStoreSource                              ▼
   Pipeline B ───────┘                       el scheduler encola Pipeline B
     (su source lee tabla_A)
```

- **A) Enlace de datos — `DataStoreSource`:** conector cuyo origen es **otra tabla del propio middleware**. El source de B es "leé `tabla_A`". A produce, B consume. Sin acople en vivo entre procesos.
- **B) Enlace de control — `RunAfter`:** campo `RunAfter` en `PipelineDefinition`. El scheduler, **cuando un `PipelineRun` de A termina OK**, busca los pipelines que lo declaran como predecesor y los encola. Se construye encima del scheduler + historial existentes.

Combinaciones: solo orden (`RunAfter` sin dependencia de datos), solo datos (B lee `tabla_A` en su propio horario), o ambos (lo normal).

**Por qué NO desestabiliza (la preocupación del MD):** los pipelines están **desacoplados por la tabla, no por streaming en vivo**:
- Cada corrida es su propio `PipelineRun` con `try/catch` (lección del `EmailScheduler`): **uno que falla no frena a los demás**.
- **Gate por dependencia:** si A falla, B **no se dispara**. No quedan procesos colgados.
- **Timeout por corrida** + circuit breaker (posterior). Como A ya terminó y persistió antes de que arranque B, **un B lento o caído no puede bloquear a A**.

---

## 15) Entrega de datos (PULL ahora, PUSH después)

**PULL (Fase 2 — arranque):**
- Endpoint `GET /api/data/{pipeline}` autenticado (Basic Auth, patrón reusado de `ConsultasGenericas`).
- Lee de la `TargetTable` vía `IDataStore.ReadPagedAsync` (paginado, filtros).
- Devuelve un `QueryResultDto`-like (Columns + Rows). El consumidor jala.

**PUSH (Fase 5 — después):**
- Sink REST que hace `POST/PUT` por lote al sistema destino, con auth configurable, rate-limit y reintentos.
- Programado por el mismo scheduler (lee de la tabla y empuja).

Arrancamos **solo con PULL** porque es lo más barato y reusa lo que el proyecto base ya hace bien.

---

## 16) Interfaz de usuario (DataPipeline.Admin)

**Molde:** calcar el patrón del `ConsultasGenericas.Admin` — **Blazor Server + Syncfusion** (.NET 10). La mecánica es idéntica; cambia el dominio.

**Patrón reusado:**
- **Por entidad:** `Components/Pages/{Entidad}/Index.razor` (grilla Syncfusion) + `Form.razor` (alta/edición).
- **Servicios cliente:** un `{Entidad}AdminService.cs` por entidad que habla con la API por `HttpClient` (el Admin no toca la BD directo).
- **Auth:** `AdminAuthService` + `AdminSessionState` (login, sesión, gating por rol).
- **Layout:** `MainLayout` + `NavMenu` + `Login`.
- **Componentes Syncfusion:** Grid, Inputs, DropDowns, Calendars, Notifications, Popups, Spinner.

**Páginas del `DataPipeline.Admin`:**

| Ruta | Qué hace | Molde de origen |
|---|---|---|
| `/login` | Auth (rol admin) | `Login.razor` + `AdminAuthService` |
| `/datasources` | CRUD de orígenes + probar conexión | `DataSources/Index+Form` |
| `/pipelines` | Lista de pipelines (estado, enabled, próxima corrida) | `Dashboards/Index` |
| `/pipelines/editar/{id}` | Form del pipeline: source + transforms + tabla destino + schedule + `RunAfter`, gestión inline de steps | `Dashboards/Form` (gestión inline de widgets) |
| `/pipelines/{id}/correr` | Botón "correr ahora" (on-demand) | patrón de prueba de query |
| `/runs` | Historial de corridas: filas in/out, duración, estado, error | `Envios/Index` |

El JSON sigue siendo el formato persistido; el Admin lo genera/edita visualmente (no hace falta editar JSON a mano).

---

## 17) Confiabilidad

- **Anti-bucle:** recalcular `NextRunAt` en `finally` (lección ya probada del `EmailScheduler`).
- **Historial** `PipelineRun` por corrida (inicio/fin, estado, filas in/out, error).
- **Reintentos** con backoff exponencial (`Polly`) — fase posterior. Distinguir transitorios (red, deadlock) de permanentes (datos inválidos).
- **Idempotencia:** upsert por clave o registro de filas entregadas → reintentar no duplica.
- **Carga incremental (watermark):** guardar el último valor procesado y filtrar `WHERE col > @watermark`. Diseñado en `RunContext` desde el inicio aunque se implemente en Fase 4.
- **Dead-letter:** filas/lotes que fallan van a un almacén aparte, sin frenar el resto — fase posterior.
- **Timeouts y cancelación** en cada paso.
- **Circuit breaker** para destinos caídos — fase posterior.

---

## 18) Observabilidad

Persistir en la **BD de metadatos del middleware (almacén A)**:
- `PipelineRun` (Id, PipelineId, inicio/fin, estado, filasLeídas, filasEscritas, filasError, mensaje).
- Log estructurado (Serilog) a archivo/seq/otel.
- **Alertas:** email/Slack/Teams ante fallo o cero filas (reusar `EmailService`).
- Métricas (Prometheus/OpenTelemetry) opcional.
- El Admin muestra el historial en `/runs`.

---

## 19) Aislamiento y estabilidad (la preocupación central)

> Objetivo explícito del usuario: **no desestabilizar el proceso actual** al encadenar procesos.

**Reglas de aislamiento:**
1. **Solución separada** (otro `.slnx`/repo). Nada de tocar `ConsultasGenericas`.
2. **Proceso separado** (la app `DataPipeline` corre como su propio host). Una pipeline pesada **no puede** tirar abajo dashboards/API del sistema actual.
3. **Bases de datos separadas** (almacén A + B propios). **Nunca** la BD del sistema actual.
4. **Deploy separado** (otro servicio Windows / contenedor / app).
5. Al sistema actual se lo trata, **a lo sumo, como ORIGEN** (vía HTTP) o se comparte **solo la librería `Core` de solo-lectura**. **Jamás** se escribe su base.
6. **Versionado/pin:** el middleware fija una versión de `Core` (o copia los modelos mínimos) para que cambios futuros en `ConsultasGenericas.Core` no lo rompan sin aviso.

**Riesgo no obvio — orígenes compartidos:** si el middleware lee de las **mismas BDs de origen** que ya golpean los dashboards (ej. `POSSERVER`), comparten carga. Mitigaciones: **réplicas de lectura**, **horarios valle**, incremental, snapshot donde aplique, throttling. La **tabla intermedia** ayuda: la extracción es lo único que toca el origen; la entrega lee de tabla propia.

---

## 20) Hoja de ruta por fases

### Fase 1 — Cimientos — *1–2 semanas*
- [ ] Crear solución `DataPipeline` (`.slnx` propio en `E:\Sistemas\MiddleWare`).
- [ ] `DataPipeline.Core`: `PipelineDefinition`, `StepDefinition`, `Record`, `RecordBatch`, `ISource/ITransform/IDataStore`, `RunContext`, `RetryPolicy`.
- [ ] `DataPipeline.Connectors`: **1 source SqlServer en streaming** + **`SqliteDataStore`**. Extraer `EncryptionService`.
- [ ] `DataPipeline.Engine`: `PipelineRunner` mínimo (source→store, sin transform) por lotes.
- **Criterio:** correr un pipeline definido en **JSON a mano**, de SqlServer a una tabla SQLite, por streaming.

### Fase 2 — Motor usable + entrega PULL — *1–2 semanas*
- [ ] `ITransform` config-driven (rename/cast/filter/computed).
- [ ] `PipelineSchedulerHostedService` + `ScheduleCalculator` (molde del EmailScheduler).
- [ ] API de entrega **PULL**: `GET /api/data/{pipeline}` autenticado (lee de `IDataStore`).
- [ ] Persistencia de definiciones + historial de corridas (`PipelineRun`, almacén A).
- **Criterio:** pipeline programado que aterriza y se expone por GET, con historial. (Todavía JSON a mano.)

### Fase 3 — UI Admin — *2–3 semanas*
- [ ] `DataPipeline.Admin` (Blazor + Syncfusion, calcando el Admin actual): CRUD de pipelines y datasources, diseñador de steps, disparo manual, historial `/runs`.
- **Criterio:** crear/editar/correr pipelines y ver corridas sin tocar JSON.

### Fase 4 — Confiabilidad y encadenamiento — *2–3 semanas*
- [ ] Carga incremental (**watermark**).
- [ ] Reintentos (**Polly**), dead-letter, idempotencia (upsert).
- [ ] 2º source (el que el negocio necesite) + **`SqlServerDataStore`**.
- [ ] **Encadenamiento:** `RunAfter` + `DataStoreSource` + timeout/gate por dependencia + (opcional) circuit breaker.
- **Criterio:** pipelines encadenados, reanudables, monitoreados y aislados.

### Fase 5 — Entrega PUSH — *1–2 semanas*
- [ ] Sink REST `POST/PUT` por lote, programado, con auth/rate-limit/reintentos.
- **Criterio:** el middleware empuja datos a un sistema destino por su cuenta.

### Fase 6 — Convergencia opcional — *cuando esté estable*
- [ ] *Si se quiere DRY:* refactorizar la API actual para usar también `DataPipeline.Connectors`. **No es necesario** y **nunca** antes de que el middleware esté sólido.

---

## 21) Decisiones tomadas (decision log)

| # | Decisión | Resolución |
|---|---|---|
| 1 | Estrategia de construcción | **Opción 1**: solución nueva sembrada con patrones probados; streaming/abstracciones de cero. |
| 2 | Acoplamiento con `Core` | Referenciar `ConsultasGenericas.Core` al inicio; **pinear versión**. |
| 3 | Flujo de datos | `source → transform → tabla intermedia propia → entrega`. |
| 4 | Almacenes | **Dos**: A) metadatos/estado (EF Core) y B) datos (dinámico, detrás de `IDataStore`). |
| 5 | BD de dev / producción | **SQLite** en dev (A y B); producción: `SqlServerDataStore` (o Postgres) para B. |
| 6 | Entrega | **PULL primero** (GET autenticado); **PUSH después** (POST programado). |
| 7 | Scheduler | **Generalizar el `BackgroundService` probado** (no Quartz/Hangfire al inicio). |
| 8 | Worker | Scheduler como **HostedService dentro del host de la API** al inicio; `Worker` separado, posterior. |
| 9 | Transformación | Config-driven + SQL-staging; scripting al final. |
| 10 | UI | `DataPipeline.Admin` **Blazor + Syncfusion**, calcando el Admin actual. |
| 11 | Encadenamiento | `RunAfter` (control) + `DataStoreSource` (datos), desacoplados por la tabla. |
| 12 | Formato in-flight | `Dictionary<string,object?>` al inicio; revisar si hay cuello. |

---

## 22) Riesgos y mitigaciones

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Carga sobre BDs de origen compartidas | Lentitud en dashboards y/o POS | Réplicas, horarios valle, incremental, throttling; **tabla intermedia** (solo la extracción toca el origen) |
| Materializar grandes volúmenes (heredar el bug actual) | OOM / cuelgues | **Streaming obligatorio** (sección 9), lotes |
| Límites de SQLite en producción | Bloqueos por concurrencia, tipos laxos | `IDataStore` aísla el motor → migrar a SQL Server/Postgres sin reescribir |
| Encadenado frágil (un eslabón cae y bloquea todo) | Procesos colgados | Desacople por tabla, gate por dependencia, timeouts, circuit breaker |
| Duplicación de datos en reintentos | Datos corruptos en destino | Idempotencia/upsert, control de filas enviadas |
| Acoplamiento accidental al sistema actual | Cambios rompen ambos | Solución/proceso/BD/deploy separados; pin de versión |
| Secretos en claro | Seguridad | Reusar AES-256; keystore/secret manager si crece |
| Cambios de esquema en origen/destino | Pipelines rotas | Validación de esquema previa; versionado de definiciones |

---

## 23) Apéndices

### A) Ejemplo de definición de pipeline (JSON)
```json
{
  "nombre": "ventas_a_serving",
  "habilitado": true,
  "programacion": "Diaria",
  "horaDelDia": "02:30",
  "filasPorLote": 5000,
  "tablaDestino": "serving_ventas",
  "modoEscritura": "Combinar",
  "correrDespuesDe": [],
  "pasos": [
    {
      "nombre": "leer_ventas",
      "tipo": "Origen",
      "conector": "SqlServerSource",
      "configuracion": {
        "dataSourceId": "POSSERVER",
        "sql": "SELECT sucursal, fecha, monto, cantidad FROM ventas WHERE fecha > @watermark",
        "watermarkColumn": "fecha"
      }
    },
    {
      "nombre": "normalizar",
      "tipo": "Transformacion",
      "conector": "ColumnMapTransform",
      "configuracion": {
        "rename": { "monto": "importe" },
        "cast":   { "importe": "decimal", "fecha": "date" },
        "filter": "importe > 0"
      }
    }
  ]
}
```
> El sink interno (escribir a `serving_ventas` vía `IDataStore`, `Combinar`/upsert por clave) es implícito. La entrega PULL se hace luego con `GET /api/data/ventas_a_serving`.

### B) Ejemplo de encadenamiento (JSON)
```json
{
  "nombre": "consolidado_dw",
  "habilitado": true,
  "programacion": "BajoDemanda",
  "tablaDestino": "dw_consolidado",
  "modoEscritura": "Reemplazar",
  "correrDespuesDe": ["<id de ventas_a_serving>"],
  "pasos": [
    {
      "nombre": "leer_serving",
      "tipo": "Origen",
      "conector": "DataStoreSource",
      "configuracion": { "sourceTable": "serving_ventas" }
    }
  ]
}
```
> `consolidado_dw` corre **después** de `ventas_a_serving` (`correrDespuesDe`) y **lee su tabla** (`DataStoreSource`): encadenamiento de control + datos.

### C) Glosario
- **Source:** paso que lee de un origen.
- **Transform:** paso que mapea/filtra/agrega datos en vuelo.
- **Sink interno:** persistencia del resultado en la tabla propia (almacén B) vía `IDataStore`.
- **Landing/serving:** la tabla intermedia propia donde aterriza el dato transformado y desde donde se entrega.
- **Batch/lote:** conjunto de filas procesado de a poco (streaming).
- **Watermark:** marca del último dato procesado para cargas incrementales.
- **DataStoreSource:** source que lee una tabla producida por otro pipeline (enlace de datos).
- **RunAfter:** dependencia de control: un pipeline corre después de que otro termina OK.
- **Dead-letter:** almacén de registros que fallaron, para revisión.
- **Idempotencia:** reejecutar produce el mismo resultado (no duplica).
- **Backpressure:** el consumidor regula el ritmo del productor (lo da `IAsyncEnumerable`).
- **PULL / PUSH:** entrega por GET que jala el consumidor / por POST que empuja el middleware.

### D) Referencias a archivos del proyecto actual (para extraer/inspirarse)
- Ejecutores: `E:\Sistemas\ConsultasGenericas\ConsultasGenericas.Api\Services\QueryExecutors\*.cs`
- Dispatcher: `...\ConsultasGenericas.Api\Services\QueryDispatcherService.cs`
- Scheduler probado: `...\ConsultasGenericas.Api\Services\EmailSchedulerHostedService.cs` (+ `EmailScheduleCalculator.cs`)
- Pipeline reporte→entrega: `...\ConsultasGenericas.Api\Services\DashboardReportService.cs` (+ `EmailService.cs`, `ChartImageRenderer.cs`)
- Encriptación: `...\ConsultasGenericas.Api\Services\EncryptionService.cs` (+ `Core/Interfaces/IEncryptionService.cs`)
- Modelos: `...\ConsultasGenericas.Core\Entities\DataSource.cs`, `Enums\DataSourceType.cs`, `Interfaces\IQueryExecutor.cs`, `DTOs\QueryResultDto.cs`, `DTOs\ResultColumnDto.cs`
- Patrón Admin: `...\ConsultasGenericas.Admin\Components\Pages\*` + `Services\*AdminService.cs`

---

*Documento de diseño aprobado — incorpora los avances del proyecto base (scheduler, pipeline reporte/entrega, pivot, presets) y el diseño definitivo. Próximo paso: escribir el plan de implementación de la Fase 1.*
