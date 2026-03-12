import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly warnings = signal<string[]>([]);
  protected readonly rows = signal<ScheduleRow[]>([]);
  protected readonly optimization = signal<number>(0);
  protected readonly fileName = signal<string | null>(null);
  protected readonly dragActive = signal(false);
  protected readonly dayFilter = signal<number | 0>(0);

  protected pdvName = '';
  protected pdvCode = '';
  protected trxAverage = '28';
  protected promoterCount = 10;
  protected minDelta = '-3';
  protected maxDelta = '30';
  protected note = '';
  protected selectedDays = new Set<number>();
  protected file: File | null = null;
  protected theme = 'dark';

  protected readonly dayOptions: DayOption[] = [
    { id: 1, label: 'Lun' },
    { id: 2, label: 'Mar' },
    { id: 3, label: 'Mié' },
    { id: 4, label: 'Jue' },
    { id: 5, label: 'Vie' },
    { id: 6, label: 'Sáb' },
    { id: 7, label: 'Dom' },
  ];

  protected async submitPreview(): Promise<void> {
    this.error.set(null);
    this.warnings.set([]);
    this.rows.set([]);

    if (!this.file) {
      this.error.set('Selecciona un CSV para continuar.');
      return;
    }

    const formData = this.buildFormData();

    try {
      this.loading.set(true);
      const response = await fetch(`${this.apiBaseUrl()}/api/csv/parse`, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        const text = await response.text();
        this.error.set(text || 'Error al procesar.');
        return;
      }

      const payload = (await response.json()) as SchedulePreviewResponse;
      if (!payload.success) {
        this.error.set('No fue posible generar la previsualización.');
        return;
      }

      this.warnings.set(payload.warnings ?? []);
      this.rows.set(payload.rows ?? []);
      this.optimization.set(payload.optimizationPercent ?? 0);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected async downloadCsv(): Promise<void> {
    this.error.set(null);

    if (!this.file) {
      this.error.set('Selecciona un CSV para descargar la malla.');
      return;
    }

    const formData = this.buildFormData();

    try {
      this.loading.set(true);
      const response = await fetch(`${this.apiBaseUrl()}/api/csv/parse?format=csv`, {
        method: 'POST',
        headers: { Accept: 'text/csv' },
        body: formData,
      });

      if (!response.ok) {
        const text = await response.text();
        this.error.set(text || 'Error al descargar CSV.');
        return;
      }

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = this.fileName() ?? 'schedule.csv';
      anchor.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.loading.set(false);
    }
  }

  protected onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) {
      this.file = null;
      return;
    }

    this.setFile(input.files[0]);
  }

  protected toggleDay(day: number): void {
    if (this.selectedDays.has(day)) {
      this.selectedDays.delete(day);
    } else {
      this.selectedDays.add(day);
    }
  }

  protected isDaySelected(day: number): boolean {
    return this.selectedDays.has(day);
  }

  protected displayedRows(): ScheduleRow[] {
    const filter = this.dayFilter();
    const data = this.rows();
    if (!filter) {
      return data;
    }

    return data.filter((row) => row.dayNumber === filter);
  }

  protected uniqueDays(): number[] {
    const days = new Set(this.rows().map((row) => row.dayNumber));
    return Array.from(days).sort((a, b) => a - b);
  }

  protected maxRequired(): number {
    return this.rows().reduce((max, row) => Math.max(max, row.requiredStaff), 0);
  }

  protected loadClass(row: ScheduleRow): string {
    if (this.promoterCount <= 0 || row.requiredStaff <= 0) {
      return 'bg-base-200/60 text-base-content';
    }

    const ratio = row.requiredStaff / this.promoterCount;
    if (ratio <= 0.6) {
      return 'bg-success/15 text-base-content';
    }

    if (ratio <= 0.85) {
      return 'bg-warning/15 text-base-content';
    }

    return 'bg-error/15 text-base-content';
  }

  protected loadLabel(row: ScheduleRow): string {
    if (this.promoterCount <= 0 || row.requiredStaff <= 0) {
      return 'Sin carga';
    }

    const ratio = row.requiredStaff / this.promoterCount;
    if (ratio <= 0.6) {
      return 'Baja';
    }

    if (ratio <= 0.85) {
      return 'Media';
    }

    return 'Alta';
  }

  protected warningCount(): number {
    return this.warnings().length;
  }

  protected warningSummary(): WarningBucket[] {
    const summary: WarningBucket[] = [
      { label: 'Insuficiente', key: 'insuficiente', count: 0, colorClass: 'bg-error' },
      { label: 'Ocio alto', key: 'ocio alto', count: 0, colorClass: 'bg-warning' },
      { label: 'Sobrecupo', key: 'sobrecupo', count: 0, colorClass: 'bg-info' },
      { label: 'Turno corto', key: 'turno corto', count: 0, colorClass: 'bg-accent' },
      { label: 'Otros', key: 'otros', count: 0, colorClass: 'bg-base-content/40' },
    ];

    const warnings = this.warnings();
    for (const warning of warnings) {
      const text = warning.toLowerCase();
      if (text.includes('insuficiente')) {
        summary[0].count += 1;
      } else if (text.includes('ocio alto')) {
        summary[1].count += 1;
      } else if (text.includes('sobrecupo')) {
        summary[2].count += 1;
      } else if (text.includes('turno corto')) {
        summary[3].count += 1;
      } else {
        summary[4].count += 1;
      }
    }

    return summary.filter((bucket) => bucket.count > 0);
  }

  protected maxWarningCount(): number {
    return Math.max(1, ...this.warningSummary().map((bucket) => bucket.count));
  }

  protected promoterColumns(): number[] {
    return Array.from({ length: this.promoterCount }, (_, index) => index + 1);
  }

  protected isAssigned(row: ScheduleRow, promoterId: number): boolean {
    return row.assignedPromoters.includes(`Promotor ${promoterId}`);
  }

  protected rowTooltip(row: ScheduleRow): string {
    return `TRX ${row.trxValue} · Delta ${row.computedDelta} · ${row.comment} · Req ${row.requiredStaff} · ${row.assignedPromoters.length} asignados`;
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
  }

  protected onFileDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    if (!event.dataTransfer?.files?.length) {
      return;
    }

    this.setFile(event.dataTransfer.files[0]);
  }

  protected setFile(file: File): void {
    this.file = file;
    this.fileName.set(this.file.name.replace('.csv', '_schedule.csv'));
  }

  protected setTheme(theme: string): void {
    this.theme = theme;
    document.body.setAttribute('data-theme', theme);
  }

  protected toggleTheme(): void {
    const next = this.theme === 'dark' ? 'light' : 'dark';
    this.setTheme(next);
  }

  private buildFormData(): FormData {
    const formData = new FormData();
    formData.append('file', this.file!);
    formData.append('pdvName', this.pdvName);
    formData.append('pdvCode', this.pdvCode);
    formData.append('trxAverage', this.trxAverage);
    formData.append('promoterCount', String(this.promoterCount));
    formData.append('selectedDays', JSON.stringify(Array.from(this.selectedDays)));
    formData.append('note', this.note);
    if (this.minDelta.trim().length > 0) {
      formData.append('minDelta', this.minDelta);
    }
    if (this.maxDelta.trim().length > 0) {
      formData.append('maxDelta', this.maxDelta);
    }
    return formData;
  }

  private apiBaseUrl(): string {
    const raw = window.__BACKEND_URL__ || window.schedulerApi?.backendUrl || 'http://localhost:5031';
    return raw.endsWith('/') ? raw.slice(0, -1) : raw;
  }
}

interface DayOption {
  id: number;
  label: string;
}

interface ScheduleRow {
  dayNumber: number;
  hour: number;
  trxValue: number;
  trxDelta: number | null;
  computedDelta: number;
  comment: string;
  requiredStaff: number;
  assignedPromoters: string[];
}

interface SchedulePreviewResponse {
  success: boolean;
  rows: ScheduleRow[];
  warnings: string[];
  optimizationPercent: number;
}

interface ThemeOption {
  id: string;
  label: string;
}

interface WarningBucket {
  label: string;
  key: string;
  count: number;
  colorClass: string;
}

declare global {
  interface Window {
    schedulerApi?: {
      backendUrl?: string;
    };
    __BACKEND_URL__?: string;
  }
}
