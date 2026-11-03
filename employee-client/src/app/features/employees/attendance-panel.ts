import { Component, inject, input, signal, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { finalize, forkJoin, Observable } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { Employee } from '../../core/models/employee.model';
import { Attendance, HrmService } from '../../core/services/hrm.service';
import { readApiError } from '../../core/api-error';

@Component({
  selector: 'app-attendance-panel', imports: [FormsModule, DatePipe],
  template: `<section class="panel">
    <h2>Attendance</h2><p>One work period per UTC day. Managers can access their direct reports.</p>
    <div class="controls">
      <label>Employee <select [(ngModel)]="employeeId" (ngModelChange)="selectEmployee()" [disabled]="busy()"><option value="">Select employee</option>@for (e of employees(); track e.id) {<option [value]="e.id">{{e.firstName}} {{e.lastName}}</option>}</select></label>
      <label>From <input type="date" [(ngModel)]="from"></label><label>To <input type="date" [(ngModel)]="to"></label>
      <button (click)="load()" [disabled]="!employeeId || busy()">Load attendance</button>
      <button (click)="act(hrm.checkIn(employeeId))" [disabled]="!employeeId || busy() || !stateKnown() || !!openPeriod() || !!todayRecord()">Check in now</button>
    </div>
    <p role="status">{{message()}}</p>
    @if (openPeriod(); as open) {
      <section class="open-period" aria-label="Open work period">
        <strong>Currently checked in</strong>
        <p>Work date: {{open.workDate}}. Checked in {{open.checkInUtc | date:'yyyy-MM-dd HH:mm':'UTC'}} UTC.</p>
        <p>This period remains open even if it falls outside the selected history dates.</p>
        <button (click)="act(hrm.checkOut(employeeId, open.workDate))" [disabled]="busy()">Check out of this period</button>
        @if (canCorrect()) {<button (click)="edit(open)">Correct this period</button>}
      </section>
    } @else if (todayRecord()) {
      <p>Attendance is already recorded for today (UTC). One work period per day is supported.</p>
    }
    @for (row of rows(); track row.id) {
      <div class="record"><strong>{{row.workDate}} — {{row.status}}</strong>
        <span>In: {{row.checkInUtc ? (row.checkInUtc | date:'HH:mm':'UTC') : '—'}} UTC</span>
        <span>Out: {{row.checkOutUtc ? (row.checkOutUtc | date:'yyyy-MM-dd HH:mm':'UTC') : '—'}} UTC</span>
        <span>{{row.notes}}</span>
        @if (row.checkInUtc && !row.checkOutUtc) { <button (click)="act(hrm.checkOut(employeeId,row.workDate))" [disabled]="busy()">Check out now</button> }
        @if (canCorrect()) { <button (click)="edit(row)">Correct</button> }
      </div>
    }
    @if (canCorrect()) {
      <details [open]="correctionOpen()"><summary>Add or correct attendance</summary>
        <form (ngSubmit)="correct()" class="controls">
          <label>Work date (UTC)<input name="date" type="date" [(ngModel)]="workDate" required></label>
          <label>Status<select name="status" [(ngModel)]="status"><option>Present</option><option>Absent</option><option>Leave</option></select></label>
          <label>Check-in (UTC)<input name="in" type="datetime-local" [(ngModel)]="checkIn"></label>
          <label>Check-out (UTC)<input name="out" type="datetime-local" [(ngModel)]="checkOut"></label>
          <label>Correction reason<input name="notes" [(ngModel)]="notes" required maxlength="2000"></label>
          <button [disabled]="!employeeId || busy()">Save correction</button>
        </form>
      </details>
    }
  </section>`,
  styles: [`.panel{background:white;border:1px solid #e4e7ef;padding:24px;border-radius:16px;margin:24px 0}.controls,.record{display:flex;gap:12px;align-items:end;flex-wrap:wrap;margin:16px 0}label{display:grid;gap:6px}input,select,button{padding:8px;border:1px solid #cbd1df;border-radius:6px}button{cursor:pointer}summary{cursor:pointer}p{color:#596174}.open-period{padding:18px;background:#edf8f5;border:1px solid #a5d8ce;border-radius:10px;margin:16px 0}.open-period button{margin-right:10px}`],
})
export class AttendancePanel {
  readonly employees = input.required<Employee[]>();
  protected readonly hrm = inject(HrmService);
  protected readonly rows = signal<Attendance[]>([]);
  protected readonly openPeriod = signal<Attendance | null>(null);
  protected readonly todayRecord = signal<Attendance | null>(null);
  protected readonly stateKnown = signal(false);
  protected readonly correctionOpen = signal(false);
  private generation = 0;
  protected readonly message = signal(''); protected readonly busy = signal(false);
  protected employeeId = ''; protected from = new Date().toISOString().slice(0, 8) + '01';
  protected to = new Date().toISOString().slice(0, 10); protected workDate = this.to;
  protected status = 'Present'; protected checkIn = ''; protected checkOut = ''; protected notes = '';
  constructor() { effect(() => { this.hrm.organization(); this.generation++; this.employeeId = ''; this.rows.set([]); this.openPeriod.set(null); this.todayRecord.set(null); this.stateKnown.set(false); this.busy.set(false); this.message.set(''); this.correctionOpen.set(false); }); }
  protected canCorrect() { return ['OrganizationOwner', 'HRAdmin', 'Manager'].includes(this.hrm.organization()?.role ?? ''); }
  protected selectEmployee() {
    this.rows.set([]); this.openPeriod.set(null); this.todayRecord.set(null); this.stateKnown.set(false); this.message.set(''); this.correctionOpen.set(false);
    this.generation++;
    if (this.employeeId) this.load();
  }
  protected load(preserveMessage = '') {
    if (!this.employeeId) return;
    const version = ++this.generation;
    const org = this.hrm.organization()?.id; const employee = this.employeeId;
    this.busy.set(true); this.message.set(preserveMessage); this.stateKnown.set(false);
    forkJoin({rows: this.hrm.attendance(employee, this.from, this.to), state: this.hrm.attendanceState(employee)})
      .pipe(finalize(() => {if (version === this.generation) this.busy.set(false);})).subscribe({
      next: result => {
        if (version !== this.generation || org !== this.hrm.organization()?.id || employee !== this.employeeId) return;
        this.rows.set(result.rows); this.openPeriod.set(result.state.openPeriod); this.todayRecord.set(result.state.todayRecord); this.stateKnown.set(true);
        this.message.set(preserveMessage || (result.rows.length ? '' : 'No attendance in this date range.'));
      },
      error: e => {if (version === this.generation) this.message.set(readApiError(e, 'Unable to load attendance.'));},
    });
  }
  protected act(request: Observable<Attendance>) {
    const version = ++this.generation;
    const org = this.hrm.organization()?.id; const employee = this.employeeId;
    this.busy.set(true); this.message.set('');
    request.pipe(finalize(() => {if (version === this.generation) this.busy.set(false);})).subscribe({
      next: () => {if (version === this.generation && org === this.hrm.organization()?.id && employee === this.employeeId) this.load();},
      error: e => {
        if (version !== this.generation) return;
        const message = readApiError(e, 'Unable to save attendance.');
        if (e instanceof HttpErrorResponse && e.status === 409) this.load(message);
        else this.message.set(message);
      },
    });
  }
  protected edit(row: Attendance) {
    this.correctionOpen.set(true);
    this.workDate = row.workDate; this.status = row.status;
    this.checkIn = row.checkInUtc?.slice(0, 16) ?? ''; this.checkOut = row.checkOutUtc?.slice(0, 16) ?? ''; this.notes = '';
  }
  protected correct() { this.act(this.hrm.correct(this.employeeId, this.workDate, {
    status: this.status, checkInUtc: this.status === 'Present' && this.checkIn ? this.checkIn + ':00Z' : null,
    checkOutUtc: this.status === 'Present' && this.checkOut ? this.checkOut + ':00Z' : null, notes: this.notes,
  })); }
}
