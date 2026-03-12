# SchedulerPDV

SchedulerPDV es una app de escritorio para generar mallas de turnos de promotores en PDV usando histórico de transacciones por hora (TRX).

## Qué hace
- Carga un CSV de TRX (formato simple o initial extendido).
- Genera la malla con reglas de negocio.
- Permite previsualizar y descargar el resultado en CSV con columnas `Promotor 1..N`.

## Requisitos
- .NET SDK 8+
- Node.js 18+ (recomendado 20+)

## Backend
Ruta: `E:\downloads\Vertere\Back\src\Api`

```bash
dotnet run
```

El backend expone `http://localhost:5031`.

## Frontend (Electron + Angular)
Ruta: `E:\downloads\Vertere\Front\schedulerpdv-front`

```bash
npm install
npm run dev
```

Esto levanta Angular en `http://localhost:4200` y Electron abre la ventana.

## Endpoint principal
`POST /api/csv/parse` (multipart/form-data)

Campos:
- `file` (CSV)
- `pdvName`, `pdvCode`
- `trxAverage`
- `promoterCount`
- `selectedDays` (JSON array string, ej: `[1,2,3,4]`)
- `note` (opcional)

Para descargar CSV: `?format=csv` o `Accept: text/csv`

## Licencia
Uso freeware/no comercial bajo CC BY-NC 4.0. Ver `LICENSE`.
