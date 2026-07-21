import { Routes } from '@angular/router';
import { ArtifactsPageComponent } from './features/artifacts/artifacts-page.component';
import { ContainerDetailComponent } from './features/container-detail/container-detail.component';
import { ContainerDiagnosticsComponent } from './features/diagnostics/container-diagnostics.component';
import { ContainerLogsComponent } from './features/logs/container-logs.component';
import { LoginComponent } from './features/login/login.component';
import { ContainerMetricsComponent } from './features/metrics/container-metrics.component';
import { ContainerOverviewComponent } from './features/overview/container-overview.component';
import { ContainersComponent } from './features/containers/containers.component';
import { RecordingDetailComponent } from './features/recordings/recording-detail.component';
import { RecordingsPageComponent } from './features/recordings/recordings-page.component';
import { SystemStatusComponent } from './features/system/system-status.component';
import { IncidentsPageComponent } from './features/incidents/incidents-page.component';
import { IncidentDetailComponent } from './features/incidents/incident-detail.component';

export const appRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'containers' },
  { path: 'login', component: LoginComponent },
  { path: 'containers', component: ContainersComponent },
  {
    path: 'containers/:id',
    component: ContainerDetailComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      { path: 'overview', component: ContainerOverviewComponent },
      { path: 'logs', component: ContainerLogsComponent },
      { path: 'metrics', component: ContainerMetricsComponent },
      { path: 'diagnostics', component: ContainerDiagnosticsComponent },
      { path: 'artifacts', component: ArtifactsPageComponent }
    ]
  },
  { path: 'recordings', component: RecordingsPageComponent },
  { path: 'recordings/:id', component: RecordingDetailComponent },
  { path: 'incidents', component: IncidentsPageComponent },
  { path: 'incidents/:id', component: IncidentDetailComponent },
  { path: 'artifacts', component: ArtifactsPageComponent },
  { path: 'system', component: SystemStatusComponent },
  { path: '**', redirectTo: 'containers' }
];
