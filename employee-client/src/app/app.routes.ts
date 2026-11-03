import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  ...['leaves', 'shifts', 'payroll'].map(path => ({path, canActivate: [authGuard], loadComponent: () => import('./features/hr/hr-page').then(c => c.HrPage), data: {view: path}, title: 'HR workspace | PeopleOps'})),
  {path: 'change-password', canActivate: [authGuard], loadComponent: () => import('./features/auth/change-password').then(c => c.ChangePassword), title: 'Change password | PeopleOps'},
  { path: 'dashboard', canActivate: [authGuard], loadComponent: () => import('./features/dashboard/dashboard').then(c => c.Dashboard), title: 'Dashboard | PeopleOps' },
  { path: 'departments', canActivate: [authGuard], loadComponent: () => import('./features/workspace/workspace-page').then(c => c.WorkspacePage), data: {view: 'departments'}, title: 'Departments | PeopleOps' },
  { path: 'hierarchy', canActivate: [authGuard], loadComponent: () => import('./features/workspace/workspace-page').then(c => c.WorkspacePage), data: {view: 'hierarchy'}, title: 'Organization hierarchy | PeopleOps' },
  { path: 'attendance', canActivate: [authGuard], loadComponent: () => import('./features/workspace/workspace-page').then(c => c.WorkspacePage), data: {view: 'attendance'}, title: 'Attendance | PeopleOps' },
  { path: 'reports', canActivate: [authGuard], loadComponent: () => import('./features/dashboard/dashboard').then(c => c.Dashboard), data: {view: 'reports'}, title: 'Reports | PeopleOps' },
  { path: 'settings', canActivate: [authGuard], loadComponent: () => import('./features/workspace/workspace-page').then(c => c.WorkspacePage), data: {view: 'settings'}, title: 'Settings | PeopleOps' },
  { path: 'password-setup', loadComponent: () => import('./features/auth/password-setup').then(c => c.PasswordSetup), title: 'Set password | PeopleOps' },
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login').then((component) => component.Login),
    title: 'Sign in | PeopleOps',
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./features/auth/register/register').then((component) => component.Register),
    title: 'Create account | PeopleOps',
  },
  {
    path: 'employees',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/employees/employees').then((component) => component.Employees),
    title: 'Employees | PeopleOps',
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
