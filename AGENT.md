# AGENT.md — SchedulerPDV
> Control de Horas Hombre por Promotor · PDV Scheduler
> Stack: .NET 8 (backend) + Electron + Angular (frontend)

---

## 📌 Índice
1. [Visión del Producto](#1-visión-del-producto)
2. [Arquitectura del Sistema](#2-arquitectura-del-sistema)
3. [Estructura de Directorios](#3-estructura-de-directorios)
4. [Modelos de Datos](#4-modelos-de-datos)
5. [Reglas de Negocio](#5-reglas-de-negocio)
6. [Algoritmo de Malla](#6-algoritmo-de-malla)
7. [API Endpoints (.NET)](#7-api-endpoints-net)
8. [Módulos Frontend (Electron + Angular)](#8-módulos-frontend-electron--angular)
9. [Formato CSV — Entrada y Salida](#9-formato-csv--entrada-y-salida)
10. [Plan de Sprints](#10-plan-de-sprints)
11. [Skills y Buenas Prácticas](#11-skills-y-buenas-prácticas)
12. [Guías de Calidad por Capa](#12-guías-de-calidad-por-capa)
13. [Criterios de Aceptación](#13-criterios-de-aceptación)

---

## 1. Visión del Producto

**SchedulerPDV** es una aplicación de escritorio que automatiza la generación de mallas de turnos para promotores en Puntos de Venta (PDV). 

### Problema que resuelve
- Los PDV tienen tráfico de transacciones variable por hora. En horas pico se necesitan más promotores que en horas valle. Hoy las mallas se hacen manualmente, lo que genera:
  - Favoritismos en la asignación de horas
  - Sobre o sub-dotación en horas específicas
  - Incumplimientos laborales (exceso de horas, turnos sin descanso)
  - Pagos incorrectos por hora trabajada

### Solución
Dado el histórico de TRX (transacciones) por hora del día y un número de promotores disponibles, el sistema:
1. Calcula cuántos promotores se necesitan en cada franja horaria
2. Asigna promotores **aleatoriamente** respetando todas las restricciones laborales
3. Genera una malla exportable en CSV lista para pagos y control

---

## 2. Arquitectura del Sistema

```
┌─────────────────────────────────────────────────────┐
│                   ELECTRON (Shell)                   │
│  Lanza el proceso .NET al iniciar la app             │
│  Mata el proceso .NET al cerrar la app               │
│                                                     │
│  ┌──────────────────────────────────────────────┐   │
│  │           ANGULAR (Renderer Process)          │   │
│  │  Setup → Schedule View → Export              │   │
│  │  Comunica con .NET vía fetch(localhost:5050)  │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
              │  HTTP (localhost:5050)
              ▼
┌─────────────────────────────────────────────────────┐
│             .NET 8 — ASP.NET Core Minimal API        │
│                                                     │
│   CsvParserService   →  parsea CSV de entrada       │
│   SchedulerService   →  motor de malla              │
│   BusinessRules      →  valida restricciones        │
│   ExportService      →  genera CSV de salida        │
│   SqliteStore        →  histórico local (SQLite)    │
└─────────────────────────────────────────────────────┘
```

### Comunicación Electron ↔ .NET
- El proceso principal de Electron (`main.js`) lanza el ejecutable .NET como **child process** al iniciar **solo en empaquetado**; en desarrollo debes ejecutar `dotnet run` por separado.
- Angular hace llamadas REST a `http://localhost:5031/api/...` (coincide con `launchSettings.json`).
- Al cerrar la ventana Electron, `main.js` mata el proceso .NET con `dotnetProcess.kill()`.
- El puerto 5031 es configurable vía variable de entorno `SCHEDULER_PORT` (backend y Electron deben apuntar al mismo).

### Persistencia local (histórico)
- Se usa SQLite embebido en la capa `Infrastructure`.
- La base local se crea automáticamente al arrancar si no existe.
- El archivo se guarda en una ruta local de la app (ej: `%APPDATA%/SchedulerPDV/schedulerpdv.db`).

---

## 3. Estructura de Directorios

```
schedulerpdv/
│
├── Back/                               ← Backend .NET 8
│   ├── SchedulerPDV.slnx               ← Solución .NET (formato slnx)
│   └── src/
│       ├── Domain/                     ← Entidades + reglas de dominio
│       ├── Application/                ← Casos de uso + servicios
│       └── Infrastructure/             ← Persistencia (SQLite) + IO
│
└── Front/                              ← Electron + Angular
    ├── main.js                         ← Proceso principal Electron
    ├── preload.js                      ← Bridge seguro (contextBridge)
    ├── package.json
    ├── electron-builder.yml            ← Config empaquetado
    │
    └── src/
        ├── main.ts                     ← Entry point Angular
        ├── app/
        │   ├── app.component.ts        ← Shell principal + router outlet
        │   ├── app.routes.ts           ← Rutas principales
        │   │
        │   ├── pages/
        │   │   ├── setup/              ← Parámetros del PDV + carga CSV
        │   │   ├── schedule-view/      ← Grilla visual de malla
        │   │   └── export/             ← Descarga CSV de salida
        │   │
        │   ├── components/
        │   │   ├── hour-grid/          ← Tabla 24h × 7días con color por carga
        │   │   ├── promoter-badge/     ← Chip de promotor con estado
        │   │   ├── load-indicator/     ← Barra TRX real vs capacidad
        │   │   └── day-selector/       ← Toggle día específico / semana
        │   │
        │   └── services/
        │       ├── schedule.service.ts ← Estado global de malla + llamadas API
        │       └── csv-import.service.ts ← Lógica de carga de archivo
        │
        └── api/
            └── scheduler-api.ts        ← Wrapper fetch → localhost:5050
```

---

## 4. Modelos de Datos

### `PdvConfig` — Parámetros de entrada del usuario
```csharp
public class PdvConfig
{
    public string PdvName        { get; set; }  // "Amarillo y Crema"
    public string PdvCode        { get; set; }  // "1825PP050"
    public int    PromoterCount  { get; set; }  // Número total de promotores disponibles
    public double TrxAverage     { get; set; }  // TRX promedio por promotor/hora (ej: 28)
    public string Mode           { get; set; }  // "day" | "week"
    public List<int> SelectedDays { get; set; } // 1..7 (1=lunes, 7=domingo)
    public int    WeekSeed       { get; set; }  // Semilla aleatoria (PdvCode + nroSemana)
}
```

### `TrxHourData` — Dato de TRX por hora leído del CSV
```csharp
public class TrxHourData
{
    public int    DayNumber  { get; set; }  // 1..7 (1=lunes, 7=domingo)
    public int    Hour       { get; set; }  // 0..23
    public double TrxValue   { get; set; }  // 124.6
    public int RequiredStaff { get; set; }  // Calculado: CEIL(TrxValue / TrxAverage)
}
```

### `Promoter` — Promotor individual
```csharp
public class Promoter
{
    public int    Id              { get; set; }   // 1..N
    public string Name            { get; set; }   // "Promotor 1" o nombre real
    public int    WeeklyHours     { get; set; }   // Horas acumuladas en la semana
    public string? RestDay        { get; set; }   // Día de descanso asignado
    public List<ShiftBlock> Shifts { get; set; } = new();
}
```

### `ShiftBlock` — Bloque de turno continuo
```csharp
public class ShiftBlock
{
    public int DayNumber  { get; set; }  // 1..7 (1=lunes, 7=domingo)
    public int StartHour  { get; set; }  // 0..23
    public int EndHour    { get; set; }  // 0..24
    public int Hours      => EndHour - StartHour;
}
```

### `WeekSchedule` — Resultado completo
```csharp
public class WeekSchedule
{
    public PdvConfig Config         { get; set; }
    public List<Promoter> Promoters { get; set; }
    public List<TrxHourData> HourlyData { get; set; }
    public List<ValidationWarning> Warnings { get; set; }  // Alertas si no se pueden cumplir reglas
    public DateTime GeneratedAt     { get; set; }
}
```

---

## 5. Reglas de Negocio

Implementadas en `BusinessRules.cs`. **Todas son validadas antes de confirmar la asignación de cada hora.**

### R1 — Duración de turno (por bloque continuo)
```
MIN_SHIFT_HOURS = 4
MAX_SHIFT_HOURS = 9

Un bloque de horas consecutivas para un promotor debe cumplir:
  4 ≤ ShiftBlock.Hours ≤ 9
```
- Si al construir el bloque quedaría en < 4h, se extiende hacia adelante si es posible.
- Si supera 9h, se parte en dos bloques con descanso entre ellos.

### R2 — Horas semanales máximas
```
MAX_WEEKLY_HOURS = 44

Promoter.WeeklyHours ≤ 44

Si un promotor llega a 44h acumuladas, se excluye del pool para el resto de la semana.
```

### R3 — Día de descanso semanal
```
Cada promotor debe tener exactamente 1 día completo sin turnos por semana.

Asignación: al promotor con más horas acumuladas al final de cada día se le asigna 
el próximo día disponible como descanso.
Restricción: el día de descanso no puede ser el mismo para todos los promotores 
(se distribuyen los descansos para garantizar cobertura mínima).
```

### R4 — Descanso entre turnos (mínimo interjornada)
```
MIN_REST_BETWEEN_SHIFTS_HOURS = 8   (mínimo absoluto)
RECOMMENDED_REST_HOURS = 10         (óptimo)

Si un promotor termina un turno a las 10:00 AM, no puede iniciar otro
turno antes de las 18:00 (con mínimo 8h) o 20:00 (con 10h recomendadas).

Aplica entre días también: un turno que termina a las 23:00 del lunes implica
que el martes el promotor no puede entrar antes de las 07:00 (8h) u 09:00 (10h).
```

### R5 — Cobertura mínima garantizada
```
En ninguna hora del día puede haber 0 promotores si TrxValue > 0.
Siempre debe haber al menos 1 promotor activo mientras el PDV opere.
Si TrxValue = 0, la cobertura mínima permitida ahora es 0 (sin asignación).
```

### R6 — Balance de carga (TRX delta)
```
TrxDelta = TRX esperado por persona - TRX real (columna 24 del CSV initial)

Si TrxDelta <= -3  → promotores insuficientes (pendiente alto)
Si TrxDelta > 5    → horas de ocio altas (sobrecapacidad)
Objetivo: TrxDelta lo más cercano posible a 0.
```

### Tabla resumen de constantes
| Constante | Valor | Descripción |
|---|---|---|
| `MIN_SHIFT_HOURS` | 4 | Horas mínimas por turno |
| `MAX_SHIFT_HOURS` | 9 | Horas máximas por turno |
| `MAX_WEEKLY_HOURS` | 44 | Horas semanales máximas |
| `REST_DAYS_PER_WEEK` | 1 | Días de descanso por promotor/semana |
| `MIN_REST_HOURS` | 8 | Descanso mínimo entre turnos |
| `RECOMMENDED_REST_HOURS` | 10 | Descanso recomendado entre turnos |

---

## 6. Algoritmo de Malla

Ubicado en `SchedulerService.cs`. Pseudocódigo completo:

```
FUNCIÓN GenerateSchedule(config, trxData):

  // FASE 1: Preparar mapa de requerimientos
  PARA CADA hora en trxData:
    hora.RequiredStaff = CEIL(hora.TrxValue / config.TrxAverage)

  // FASE 2: Inicializar promotores
  promotores = [Promoter(1..config.PromoterCount)]
  semilla = config.WeekSeed  // Reproducible por semana (ej: 202605)

  // FASE 3: Asignar descansos rotativos
  diasDescanso = DistribuirDescansos(promotores, dias)
  // Algoritmo round-robin: Promotor 1 → lunes, Promotor 2 → martes, etc.
  // Con módulo: Promotor N → dias[N % 7]

  // FASE 4: Construir malla hora por hora
  PARA CADA dia EN dias_ordenados:
    PARA CADA hora EN 00:00..23:00:
      requeridos = hora.RequiredStaff
      
      disponibles = promotores
        DONDE promotor.RestDay != dia     // siempre se respeta día de descanso asignado
          Y   promotor.WeeklyHours < MAX_WEEKLY_HOURS
          Y   NO tiene turno activo que violaría R4 (descanso entre turnos)
          Y   NO alcanza MAX_SHIFT_HOURS en el bloque actual
      
      // Orden determinista con semilla (no aleatorio entre ejecuciones)
      disponibles = OrdenarPorHorasTrabajadasLuegoHash(detalle = semilla + promotor.Id + dia)
      
      asignados = disponibles[0..requeridos]
      
      // Registrar asignación
      PARA CADA promotor EN asignados:
        AgregarOExtenderBloque(promotor, dia, hora)
      
      // Validar bloques que terminaron en esta hora
      PARA CADA promotor NO EN asignados CON bloqueActivo:
        ValidarYCerrarBloque(promotor, dia, hora)

  // FASE 5: Post-validación
  advertencias = ValidarTodasLasReglas(promotores)
  
  RETORNAR WeekSchedule(promotores, trxData, advertencias)


FUNCIÓN AgregarOExtenderBloque(promotor, dia, hora):
  SI promotor NO tiene bloque abierto en este dia:
    Crear ShiftBlock(dia, inicio=hora)
  SINO:
    Extender ShiftBlock actual (fin = hora + 1h)
  SI ShiftBlock.Hours == MAX_SHIFT_HOURS:
    CerrarBloque(promotor)


FUNCIÓN ValidarYCerrarBloque(promotor, dia, hora):
  bloque = promotor.BloqueActual
  SI bloque.Hours < MIN_SHIFT_HOURS:
    REGISTRAR advertencia "Turno muy corto: {promotor.Name} {dia} {bloque.Hours}h"
  CerrarBloque(promotor)
  promotor.WeeklyHours += bloque.Hours
```

### Cálculo de promotores por hora (núcleo del negocio)
```
Ejemplo con TRX promedio = 28:

  TRX hora  →  Promotores necesarios
  ────────────────────────────────────
   23.4      →  CEIL(23.4 / 28)  = 1
   58.1      →  CEIL(58.1 / 28)  = 3
   95.1      →  CEIL(95.1 / 28)  = 4
  110.3      →  CEIL(110.3 / 28) = 4
  124.8      →  CEIL(124.8 / 28) = 5
  135.2      →  CEIL(135.2 / 28) = 5
```

---

## 7. API Endpoints (.NET)

Base URL: `http://localhost:5050/api`

### `POST /schedule/generate`
Genera la malla completa.

**Request body:**
```json
{
  "pdvName": "Amarillo y Crema",
  "pdvCode": "1825PP050",
  "promoterCount": 14,
  "trxAverage": 28.0,
  "mode": "week",
  "selectedDays": [1,2,3,4,5,6,7],
  "weekSeed": 202605,
  "trxData": [
    { "dayNumber": 7, "hour": 0, "trxValue": 23.4 },
    { "dayNumber": 7, "hour": 1, "trxValue": 22.7 }
  ]
}
```

Si `weekSeed` no se envía, el backend usa la semana ISO de la fecha actual para mantener reproducibilidad dentro de la misma semana.

**Response:**
```json
{
  "success": true,
  "schedule": {
    "generatedAt": "2025-06-10T14:30:00",
    "promoters": [
      {
        "id": 1,
        "name": "Promotor 1",
        "weeklyHours": 40,
        "restDay": "lunes",
        "shifts": [
          { "day": "domingo", "startTime": "00:00", "endTime": "05:00", "hours": 5 },
          { "day": "domingo", "startTime": "16:00", "endTime": "20:00", "hours": 4 }
        ]
      }
    ],
    "hourlyData": [
      {
        "dayNumber": 7,
        "hour": 0,
        "trxValue": 23.4,
        "requiredStaff": 1,
        "assignedStaff": ["Promotor 1"],
        "capacity": 28,
        "difference": 4.6
      }
    ],
    "warnings": []
  }
}
```

### `POST /csv/parse`
Parsea el CSV de entrada y devuelve los datos TRX estructurados.

**Request:** `multipart/form-data` con campo `file` (el CSV).
**Opcionales:** `selectedDays` como string `1,2,3,4`.

**Response:**
```json
{
  "success": true,
  "rows": [
    { "dayNumber": 1, "hour": 0, "trxValue": 23.4 }
  ],
  "warnings": []
}
```

### `POST /export/csv`
Genera y devuelve el CSV de malla para descarga.

**Request body:** Objeto `WeekSchedule` completo (el que devolvió `/schedule/generate`).
**Response:** `application/octet-stream` — archivo CSV descargable.

### `GET /health`
Ping de salud para que Electron sepa que el backend está listo.
```json
{ "status": "ok", "version": "1.0.0" }
```

---

## 8. Módulos Frontend (Electron + Angular)

### `main.js` — Proceso principal Electron
```javascript
// Responsabilidades:
// 1. Lanzar proceso .NET hijo
// 2. Esperar a que /health responda OK antes de mostrar la ventana
// 3. Matar el proceso .NET al cerrar la app
// 4. Configurar Content Security Policy

const { app, BrowserWindow } = require('electron');
const { spawn } = require('child_process');
const path = require('path');

let dotnetProcess;

app.whenReady().then(async () => {
  // Lanzar backend
  dotnetProcess = spawn(path.join(__dirname, 'backend', 'SchedulerPDV.Api'));
  
  // Esperar a que el backend esté listo (polling /health cada 200ms, máx 10s)
  await waitForBackend('http://localhost:5050/api/health');
  
  // Crear ventana
  const win = new BrowserWindow({
    width: 1280, height: 800,
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true }
  });
  win.loadFile('src/index.html');
});

app.on('before-quit', () => dotnetProcess?.kill());
```

### `preload.js` — Bridge seguro
```javascript
// Expone SOLO lo necesario al renderer. NUNCA exponer todo node o electron.
const { contextBridge } = require('electron');

contextBridge.exposeInMainWorld('schedulerApi', {
  generateSchedule: (config) => fetch('http://localhost:5050/api/schedule/generate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(config)
  }).then(r => r.json()),
  
  parseCsv: (formData) => fetch('http://localhost:5050/api/csv/parse', {
    method: 'POST', body: formData
  }).then(r => r.json()),
  
  exportCsv: (schedule) => fetch('http://localhost:5050/api/export/csv', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(schedule)
  }).then(r => r.blob())
});
```

### `setup.component.ts/html` — Pantalla de configuración
**Campos del formulario:**
- Nombre PDV (texto)
- Código PDV (texto)
- Número de promotores (número, validado 1–50)
- TRX promedio (número, default 28)
- Modo: radio "Día específico" / "Semana completa"
- Si modo = día: selector de día (lunes...domingo)
- Upload de CSV (drag & drop o selector de archivo)
- Botón "Generar Malla"

**Flujo:**
1. Usuario sube CSV → llamar `/csv/parse` → pre-llenar campos desde el CSV
2. Usuario ajusta parámetros
3. Click "Generar" → llamar `/schedule/generate` → navegar a `ScheduleView`

### `schedule-view.component.ts/html` — Vista de malla
**Layout:** Grilla `24 filas (horas) × N columnas (días o 1 día)`

**Cada celda contiene:**
- Lista de `PromoterBadge` (chips de colores con nombre del promotor)
- `LoadIndicator`: barra pequeña mostrando `TRX real vs Capacidad asignada`

**Código de colores por carga:**
```
Verde   → capacidad ≥ TRX + 15%   (sobrecapacidad holgada)
Azul    → capacidad entre TRX y TRX+15%  (óptimo)
Amarillo → capacidad entre TRX-10% y TRX  (ajustado)
Rojo    → capacidad < TRX - 10%   (sub-dotado, requiere atención)
```

**Interacciones:**
- Click en celda → modal con detalle de la hora (TRX, promotores, diferencia)
- Click en promotor → resaltar todos sus turnos en la semana
- Botón "Exportar CSV" → llamar `/export/csv` → disparar descarga

### `hour-grid.component.ts/html` — Componente de grilla
```ts
// @Input:
// schedule: WeekSchedule
// mode: "day" | "week"
// targetDay?: string
// @Output:
// cellClick: (day, hour) => void
// promoterClick: (promoterId) => void

// Renderiza una tabla con:
// - Columna fija de horas (00:00 - 23:00)
// - Una columna por día (o una columna si mode = "day")
// - Celdas coloreadas según carga
```

---

## 9. Formato CSV — Entrada y Salida

### CSV de Entrada (3 columnas)
El CSV usa `;` como separador. Las columnas son:

| Col # | Nombre | Ejemplo |
|---|---|---|
| 0 | Día (número) | `1` (lunes) .. `7` (domingo) |
| 1 | Hora | `0` .. `23` |
| 2 | TRX | `77,7` |

**El parser debe:**
- Aceptar únicamente 3 columnas (`dayNumber;hour;trx`)
- Aceptar horas `0..23` (sin ceros a la izquierda)
- Convertir comas decimales a punto (`77,7` → `77.7`)
- Omitir líneas vacías y reportar advertencias por líneas inválidas
- Permitir un header opcional en la primera línea
- Si `selectedDays` se envía, ignorar filas fuera de esos días

### CSV de Entrada (formato initial extendido)
Además del formato simple, el sistema acepta el CSV extendido como `EXAMPLE_CSV_INITIAL.csv`.
Se extraen:
- Día: columna 4 (Nombre del día)
- Hora: columna 5
- TRX: columna 6
- TrxDelta: columna 24

**Interpretación TrxDelta:**
- Negativo: transacciones pendientes (promotores insuficientes).
- Positivo: tiempo ocioso (sobrecapacidad).
- Se generan warnings si TrxDelta <= -3 o TrxDelta > 5.

### CSV de Salida — Malla por promotor

**Archivo 1: `malla_promotores_{pdvCode}_{fecha}.csv`**
```csv
Promotor;Domingo;Lunes;Martes;Miércoles;Jueves;Viernes;Sábado;Horas Semana
Promotor 1;00:00-05:00 / 16:00-20:00;DESCANSO;07:00-16:00;07:00-16:00;07:00-16:00;08:00-17:00;08:00-17:00;40
Promotor 2;DESCANSO;06:00-14:00;06:00-15:00;...;...;...;...;42
```

**Archivo 2: `detalle_horas_{pdvCode}_{fecha}.csv`**
```csv
Día;Hora;TRX;Promotores Requeridos;Promotores Asignados;Capacidad Total;Diferencia
domingo;00:00;23.4;1;Promotor 1;28;4.6
domingo;01:00;22.7;1;Promotor 1;28;5.3
domingo;05:00;38.5;2;Promotor 1;Promotor 2;56;17.5
```

---

## 10. Plan de Sprints

### Sprint 1 — Infraestructura base (5 días)
**Backend:**
- [ ] Crear solución `.slnx` en `Back/` y proyectos en `Back/src` (Domain, Application, Infrastructure, Api)
- [ ] Configurar CORS para `localhost` (Electron)
- [ ] Implementar `GET /health`
 - [ ] Implementar `CsvParserService`
- [ ] Modelos de datos completos con validación via FluentValidation
 - [ ] Configurar SQLite local en Infrastructure y crear DB al arrancar la app

**Frontend:**
- [ ] Setup proyecto Electron + Angular (Angular CLI)
- [ ] `main.js`: lanzar proceso .NET hijo + polling health
- [ ] `preload.js`: contextBridge con los 3 métodos de API
- [ ] Estructura de rutas Angular (Setup → ScheduleView → Export)

**Criterio de aceptación Sprint 1:**
- Electron inicia, lanza .NET, y muestra una pantalla Angular básica.
- El CSV de muestra (`Libro1.csv`) se parsea correctamente y devuelve los 168 registros (7 días × 24 horas).

---

### Sprint 2 — Lógica de negocio (5 días)
**Backend:**
- [ ] Implementar `BusinessRules.cs` con las 5 reglas (con tests para cada una)
- [ ] Implementar `SchedulerService.cs` — algoritmo de malla completo
- [ ] Implementar `POST /schedule/generate` end-to-end
- [ ] Tests de integración con datos del CSV real

**Criterio de aceptación Sprint 2:**
- Para el PDV `1825PP050` con 14 promotores y TRX avg 28, el sistema genera una malla semanal sin violar ninguna regla de negocio.
- Se cubren todos los horarios con TRX > 0.

---

### Sprint 3 — UI completa (5 días)
**Frontend:**
- [ ] `setup.component`: formulario completo con upload de CSV y pre-llenado
- [ ] `hour-grid.component`: grilla 24h × 7días con código de colores
- [ ] `promoter-badge.component` + `load-indicator.component`
- [ ] Modal de detalle al click en celda
- [ ] Resaltado de turnos al click en promotor
- [ ] `export.component`: descarga de los 2 CSV de salida

**Criterio de aceptación Sprint 3:**
- La malla visual es clara y sin ambigüedades.
- Los CSV exportados pueden abrirse en Excel correctamente.

---

### Sprint 4 — Pulido y empaquetado (3 días)
- [ ] Manejo de errores y estados de carga en UI
- [ ] Advertencias visuales cuando no se pueden cumplir reglas (ej: pocos promotores)
- [ ] Configurar `electron-builder` para generar instalador `.exe` (Windows)
- [ ] Documentar README.md con instrucciones de instalación y uso
- [ ] Prueba con datos reales end-to-end

---

## 11. Skills y Buenas Prácticas

Los siguientes recursos de **skills.sh** contienen las guías de referencia que este agente **debe leer antes de implementar** cada capa del sistema:

### 🔵 Para el Frontend (Electron + Angular)

**`/mnt/skills/public/frontend-design/SKILL.md`**
> Guía de diseño de interfaces de alta calidad para Angular.
> **Cuándo usarla:** Antes de implementar cualquier componente visual (`hour-grid`, `setup`, `promoter-badge`).
> **Puntos clave para este proyecto:**
> - Usar Tailwind utility classes solamente (sin CSS custom salvo variables)
> - La grilla `HourGrid` debe ser visualmente clara con colores semánticos (verde/amarillo/rojo)
> - Evitar estéticas genéricas — la UI de scheduling debe verse como herramienta profesional de operaciones
> - Micro-interacciones en hover sobre celdas de la grilla
> - Tipografía monoespaciada para las horas (mejor legibilidad)

**`/mnt/skills/examples/mcp-builder/reference/node_mcp_server.md`**
> Patrones Node.js/TypeScript de alta calidad.
> **Cuándo usarla:** Al implementar `preload.js`, `main.js` y `schedulerApi.js`.
> **Puntos clave para este proyecto:**
> - Usar Zod para validar todas las respuestas de la API antes de pasarlas al estado Angular
> - Manejo de errores: nunca dejar `fetch` sin `try/catch`; mostrar errores en UI, no solo en consola
> - El `contextBridge` en preload debe exponer solo funciones nombradas, nunca objetos `ipcRenderer` completos
> - Logging en main process: usar `stderr`, no `stdout`

**`/mnt/skills/examples/mcp-builder/reference/mcp_best_practices.md`**
> Buenas prácticas de comunicación cliente-servidor local.
> **Cuándo usarla:** Al diseñar la comunicación Electron ↔ .NET.
> **Puntos clave para este proyecto:**
> - Usar transporte `stdio` (proceso hijo) por ser single-user, single-session — exactamente el modelo de este proyecto
> - Bind en `127.0.0.1`, no en `0.0.0.0` (seguridad DNS rebinding)
> - Validar el `Origin` header en el servidor .NET para solo aceptar peticiones de `localhost`
> - Implementar rate limit o timeout en llamadas: si el .NET no responde en 5s, mostrar error al usuario

### 🟢 Para Documentos de Salida

**`/mnt/skills/public/xlsx/SKILL.md`**
> **Cuándo usarla:** Si en el futuro se quiere exportar la malla en formato Excel además de CSV.
> Por ahora la exportación es solo CSV, pero esta skill define el formato correcto de tablas.

**`/mnt/skills/public/docx/SKILL.md`**
> **Cuándo usarla:** Si se decide generar un reporte en Word con el resumen de horas por promotor.

---

## 12. Guías de Calidad por Capa

### .NET (Backend)

```
✅ HACER                                    ❌ NO HACER
─────────────────────────────────────────────────────────
Usar Minimal API (Program.cs limpio)        No usar controllers MVC
Inyectar dependencias por interfaz          No instanciar servicios con new
Usar FluentValidation para inputs           No validar con if/else en endpoints
Usar records para DTOs (inmutables)         No mezclar modelos de BD con DTOs
Manejar errores con Results.Problem()       No lanzar excepciones sin capturar
Usar CancellationToken en async             No ignorar cancellation
Escribir test por cada regla de negocio     No testear solo el happy path
Usar TimeOnly/DateOnly para horas/fechas    No usar string para horas
Loguear con ILogger, no Console.WriteLine   No usar Console en producción
```

### TypeScript/Angular (Frontend)

```
✅ HACER                                    ❌ NO HACER
─────────────────────────────────────────────────────────
Tipar todos los objetos (interfaces TS)     No usar `any`
Usar servicios para lógica compleja         No poner lógica en componentes de UI
Usar OnPush en grillas pesadas              No re-renderizar la grilla completa
Usar estados de carga (loading/error/data)  No mostrar datos sin estado de carga
Validar CSV antes de enviar al backend      No confiar en que el CSV siempre es válido
Mostrar advertencias de reglas de negocio   No silenciar warnings del scheduler
Deshabilitar botón "Generar" si hay errores No permitir submit con form inválido
```

### Electron (Main Process)

```
✅ HACER                                    ❌ NO HACER
─────────────────────────────────────────────────────────
Usar contextIsolation: true                 No usar nodeIntegration: true
Exponer API mínima por contextBridge        No exponer require() o ipcRenderer directo
Matar proceso .NET en before-quit          No dejar procesos huérfanos
Hacer polling de /health antes de mostrar   No asumir que .NET inicia instantáneo
Firmar el instalador (.exe)                 No distribuir sin firma (Windows Defender)
```

---

## 13. Criterios de Aceptación

### Funcionales (todos deben cumplirse)
- [ ] **FA-01:** El sistema parsea correctamente un CSV de 3 columnas (`dayNumber;hour;trx`) y produce N registros según los días enviados.
- [ ] **FA-02:** Dado TRX avg=28, una hora con TRX=110.3 asigna exactamente 4 promotores.
- [ ] **FA-03:** Ningún promotor supera 44 horas en la malla semanal.
- [ ] **FA-04:** Ningún promotor trabaja más de 9 horas continuas en un turno.
- [ ] **FA-05:** Ningún promotor trabaja menos de 4 horas continuas en un turno.
- [ ] **FA-06:** Cada promotor tiene exactamente 1 día de descanso en la semana.
- [ ] **FA-07:** Entre el fin de un turno y el inicio del siguiente hay al menos 8 horas.
- [ ] **FA-08:** La asignación de promotores es aleatoria (no siempre los mismos en horas pico).
- [ ] **FA-09:** Con la misma semilla, la malla generada es idéntica (reproducibilidad).
- [ ] **FA-10:** El CSV exportado puede abrirse en Excel sin errores de formato.
- [ ] **FA-11:** El modo "día específico" genera malla solo para ese día.
- [ ] **FA-12:** Si hay pocos promotores para cubrir la demanda, se muestra advertencia visible.

### No Funcionales
- [ ] **NFN-01:** El backend responde `/schedule/generate` en < 2 segundos para 7 días × 24 horas × 14 promotores.
- [ ] **NFN-02:** El instalador `.exe` funciona en Windows 10/11 sin instalar dependencias adicionales.
- [ ] **NFN-03:** La aplicación no deja procesos .NET huérfanos al cerrar inesperadamente.
- [ ] **NFN-04:** La grilla de 7 días es legible sin scroll horizontal en 1280px de ancho.

---

*Generado por SchedulerPDV AGENT.md — Versión 1.0*
*Última actualización: 2025 · Stack: .NET 8 + Electron + Angular*
