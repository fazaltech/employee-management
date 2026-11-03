import { Component, input } from '@angular/core';

@Component({
  selector: 'app-icon',
  template: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="paths[name()] || paths['grid']" /></svg>`,
  styles: [':host{display:inline-flex;width:20px;height:20px;flex-shrink:0;vertical-align:middle}svg{width:100%;height:100%}'],
})
export class Icon {
  readonly name = input('grid');
  protected readonly paths: Record<string, string> = {
    grid: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
    people: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M17 4a4 4 0 0 1 0 8 M22 21v-2a4 4 0 0 0-3-3.9',
    building: 'M4 21V3h12v18 M16 9h4v12 M2 21h20 M8 7h4 M8 11h4 M8 15h4 M8 21v-3h4v3',
    hierarchy: 'M9 3h6v5H9z M3 16h6v5H3z M15 16h6v5h-6z M12 8v4 M6 16v-4h12v4',
    clock: 'M12 8v5l3 2 M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
    chart: 'M4 3v18h17 M9 17v-5 M14 17V7 M19 17v-8',
    settings: 'M4 7h16 M4 17h16 M8 4v6 M16 14v6',
    menu: 'M4 6h16 M4 12h16 M4 18h16',
    search: 'M21 21l-5-5 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',
    bell: 'M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9 M10 21h4',
    down: 'M6 9l6 6 6-6', plus: 'M12 5v14 M5 12h14',
    arrow: 'M5 12h14 M14 7l5 5-5 5', check: 'M5 12l4 4L19 6',
    logout: 'M9 3H4v18h5 M10 12h11 M17 8l4 4-4 4',
    close: 'M6 6l12 12 M18 6L6 18', refresh: 'M20 7v5h-5 M4 17v-5h5 M6 6a8 8 0 0 1 13 2 M18 18A8 8 0 0 1 5 16',
    calendar: 'M3 5h18v16H3z M3 10h18 M7 3v4 M17 3v4',
  };
}
