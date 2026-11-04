import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { AuthService } from '../../core/services/auth.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { SupportPage } from './support-page';

const hub = vi.hoisted(() => ({ start: vi.fn(), stop: vi.fn(), invoke: vi.fn(), on: vi.fn(), onclose: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn() }));
vi.mock('@microsoft/signalr', () => ({ HubConnectionBuilder: class {
  withUrl() { return this; } withAutomaticReconnect() { return this; } build() { return hub; }
} }));

describe('Support chat', () => {
  const organization = signal({ id: 'org', role: 'Manager' });
  const platform = signal(false);
  beforeEach(async () => {
    vi.resetAllMocks(); organization.set({ id: 'org', role: 'Manager' }); platform.set(false);
    hub.start.mockResolvedValue(undefined); hub.stop.mockResolvedValue(undefined);
    hub.invoke.mockImplementation(async (method: string) => method === 'ListConversations'
      ? [{ id: 'a', userName: 'Owner', organizationName: 'Org', updatedAtUtc: '2026-11-04T10:00:00Z' }, { id: 'b', userName: 'Manager', organizationName: 'Org', updatedAtUtc: '2026-11-04T10:00:00Z' }] : []);
    Element.prototype.scrollIntoView = vi.fn();
    await TestBed.configureTestingModule({ imports: [SupportPage], providers: [
      { provide: AuthService, useValue: { user: signal({ id: 'admin' }), token: 'token' } },
      { provide: WorkspaceService, useValue: { platform, loading: signal(false), hrm: { organization } } },
    ] }).compileComponents();
  });
  it('does not open a connection for employees', async () => {
    organization.set({ id: 'org', role: 'Employee' });
    const fixture = TestBed.createComponent(SupportPage); fixture.detectChanges(); await fixture.whenStable();
    expect(hub.start).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('owners and managers only');
  });
  it('keeps separate drafts and sends to the selected conversation', async () => {
    platform.set(true);
    const fixture = TestBed.createComponent(SupportPage); fixture.detectChanges(); await fixture.whenStable();
    const page = fixture.componentInstance;
    await page.select('a'); page.draft = 'Owner response';
    await page.select('b'); page.draft = 'Manager response';
    await page.send();
    expect(hub.invoke).toHaveBeenCalledWith('SendMessage', 'b', 'Manager response');
    await page.select('a'); expect(page.draft).toBe('Owner response');
    fixture.destroy(); expect(hub.stop).toHaveBeenCalled();
  });
});
