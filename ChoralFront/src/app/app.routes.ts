import { Routes } from '@angular/router';
import { authGuard } from '@core/guards/auth.guard';
import { guestGuard } from '@core/guards/guest.guard';
import { adminGuard } from '@core/guards/admin.guard';
import { clientRoleGuard } from '@core/guards/client-role.guard';
import { spaceRoleGuard } from '@core/guards/space-role.guard';
import { zoneRedirectGuard } from '@core/guards/zone-redirect.guard';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { RoutePaths } from '@core/route-paths';
import { MANAGEMENT_ROLES } from '@core/zone-resolver';

const CHOIR_ONLY = [SpaceTypeEnum.Choir];

export const routes: Routes = [
  // Racine de l'application : jamais de contenu propre. Redirige vers /dashboard, qui porte
  // déjà la règle d'aiguillage (authGuard + zoneRedirectGuard) — non connecté -> /login,
  // Admin -> /admin, chef de chœur -> /management/:spaceId, choriste -> /me. Sans cette
  // entrée, `/` tombait sur le wildcard `**` et affichait la page 404.
  { path: '', pathMatch: 'full', redirectTo: RoutePaths.Dashboard },
  {
    path: RoutePaths.Login,
    canActivate: [guestGuard],
    loadComponent: () => import('@app/components/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: RoutePaths.ForgotPassword,
    loadComponent: () => import('@app/components/auth/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent)
  },
  {
    path: RoutePaths.ResetPassword,
    loadComponent: () => import('@app/components/auth/reset-password/reset-password.component').then(m => m.ResetPasswordComponent)
  },

  // Activation du compte d'un membre invité — page transitoire atteinte depuis le lien reçu
  // par mail, donc SANS garde (même convention que reset-password et verify-email) : le
  // destinataire n'a pas encore de mot de passe, il ne peut pas être connecté.
  {
    path: RoutePaths.ActivateAccount,
    loadComponent: () =>
      import('@app/components/auth/activate-account/activate-account.component').then(m => m.ActivateAccountComponent)
  },

  // Registration auto-service (lot 6). guestGuard uniquement sur /registration elle-même (même
  // convention que /login) — /registration/confirm et /verify-email restent accessibles
  // sans garde, comme forgot-password/reset-password (pages transitoires).
  {
    path: RoutePaths.Registration,
    canActivate: [guestGuard],
    loadComponent: () => import('@app/components/auth/registration/registration.component').then(m => m.RegistrationComponent)
  },
  {
    path: `${RoutePaths.Registration}/${RoutePaths.RegistrationConfirm}`,
    loadComponent: () =>
      import('@app/components/auth/registration-confirm/registration-confirm.component').then(m => m.RegistrationConfirmComponent)
  },
  {
    path: RoutePaths.VerifyEmail,
    loadComponent: () => import('@app/components/auth/verify-email/verify-email.component').then(m => m.VerifyEmailComponent)
  },

  // Accessible connecté ET non connecté — aucun guard (voir join.component pour le mode
  // de garde retenu).
  {
    path: RoutePaths.Join,
    loadComponent: () => import('@app/components/auth/join/join.component').then(m => m.JoinComponent)
  },

  // Hub post-connexion pour un utilisateur sans aucun rattachement (target de zone-resolver.ts),
  // aussi accessible directement pour rejoindre/créer un espace supplémentaire. Chemins enfants
  // à plat (même convention que les sous-routes /admin), pas de Shell (pages autonomes).
  {
    path: RoutePaths.Start,
    canActivate: [authGuard],
    loadComponent: () => import('@app/components/onboarding/start/start.component').then(m => m.StartComponent)
  },
  {
    path: `${RoutePaths.Start}/${RoutePaths.StartCreateChoir}`,
    canActivate: [authGuard],
    loadComponent: () => import('@app/components/onboarding/create-choir/create-choir.component').then(m => m.CreateChoirComponent)
  },
  {
    path: `${RoutePaths.Start}/${RoutePaths.StartCreateEvent}`,
    canActivate: [authGuard],
    loadComponent: () =>
      import('@app/components/onboarding/create-event/create-event.component').then(m => m.CreateEventComponent)
  },

  // Route « intelligente », jamais rendue en pratique (le guard redirige systématiquement) :
  // target neutre pour guestGuard, returnUrl, et NotFoundComponent (figé, non modifié).
  {
    path: RoutePaths.Dashboard,
    canActivate: [authGuard, zoneRedirectGuard],
    loadComponent: () => import('@app/components/shared/placeholder/placeholder.component').then(m => m.PlaceholderComponent),
    data: { title: 'Redirection...' }
  },

  {
    path: RoutePaths.Admin,
    canActivate: [adminGuard],
    loadComponent: () => import('@app/components/layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: RoutePaths.AdminDashboard },
      {
        path: RoutePaths.AdminDashboard,
        loadComponent: () => import('@app/components/admin/dashboard/dashboard.component').then(m => m.AdminDashboardComponent),
        data: { title: 'Administration — Tableau de bord' }
      },
      {
        path: RoutePaths.AdminClients,
        loadComponent: () => import('@app/components/admin/clients/client-list/client-list.component').then(m => m.ClientListComponent),
        data: { title: 'Clients' }
      },
      {
        path: `${RoutePaths.AdminClients}/:id`,
        loadComponent: () =>
          import('@app/components/admin/clients/client-detail/client-detail.component').then(m => m.ClientDetailComponent),
        data: { title: 'Détail client' }
      },
      {
        path: RoutePaths.AdminChoirs,
        loadComponent: () =>
          import('@app/components/admin/choirs/choir-list/choir-list.component').then(m => m.ChoirListComponent),
        data: { title: 'Chorales' }
      },
      {
        path: `${RoutePaths.AdminChoirs}/:id`,
        loadComponent: () =>
          import('@app/components/admin/choirs/choir-detail/choir-detail.component').then(m => m.ChoirDetailComponent),
        data: { title: 'Détail chorale' }
      },
      {
        path: RoutePaths.AdminEvents,
        loadComponent: () =>
          import('@app/components/admin/events/event-list/event-list.component').then(m => m.EventListComponent),
        data: { title: 'Événements' }
      },
      {
        path: `${RoutePaths.AdminEvents}/:id`,
        loadComponent: () =>
          import('@app/components/admin/events/event-detail/event-detail.component').then(
            m => m.EventDetailComponent
          ),
        data: { title: 'Détail événement' }
      },
      {
        path: RoutePaths.AdminUsers,
        loadComponent: () =>
          import('@app/components/admin/users/user-list/user-list.component').then(
            m => m.UserListComponent
          ),
        data: { title: 'Utilisateurs' }
      },
      {
        path: `${RoutePaths.AdminUsers}/:id`,
        loadComponent: () =>
          import('@app/components/admin/users/user-detail/user-detail.component').then(
            m => m.UserDetailComponent
          ),
        data: { title: 'Détail utilisateur' }
      },
      {
        path: RoutePaths.AdminSongs,
        loadComponent: () => import('@app/components/admin/songs/song-catalogue.component').then(m => m.SongCatalogueComponent),
        data: { title: 'Chants' }
      },
      {
        path: RoutePaths.AdminAudit,
        loadComponent: () => import('@app/components/admin/audit/audit.component').then(m => m.AdminAuditComponent),
        data: { title: 'Audit' }
      }
    ]
  },

  {
    path: `${RoutePaths.Client}/:clientId`,
    canActivate: [clientRoleGuard],
    loadComponent: () => import('@app/components/layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('@app/components/client/my-structure/my-structure.component').then(m => m.MyStructureComponent),
        data: { title: 'Ma structure' }
      },
      {
        path: RoutePaths.ClientChoirDetail,
        loadComponent: () =>
          import('@app/components/client/choir-detail/choir-detail.component').then(m => m.ChoirDetailComponent),
        data: { title: 'Fiche chorale' }
      }
    ]
  },

  {
    path: `${RoutePaths.Management}/:spaceId`,
    canActivate: [spaceRoleGuard(MANAGEMENT_ROLES)],
    loadComponent: () => import('@app/components/layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: RoutePaths.Dashboard },
      {
        path: RoutePaths.Dashboard,
        loadComponent: () => import('@app/components/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: RoutePaths.Members,
        canActivate: [spaceRoleGuard([UserRoleEnum.Manager], CHOIR_ONLY)],
        loadComponent: () => import('@app/components/members/member-list/member-list.component').then(m => m.MemberListComponent),
        data: { title: 'Membres' }
      },
      {
        path: RoutePaths.MemberDetail,
        canActivate: [spaceRoleGuard([UserRoleEnum.Manager], CHOIR_ONLY)],
        loadComponent: () => import('@app/components/shared/placeholder/placeholder.component').then(m => m.PlaceholderComponent),
        data: { title: 'Détail membre' }
      },
      {
        path: RoutePaths.Songs,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () => import('@app/components/songs/song-list/song-list.component').then(m => m.SongListComponent),
        data: { title: 'Chants' }
      },
      {
        path: RoutePaths.SongDetail,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () => import('@app/components/songs/song-detail/song-detail.component').then(m => m.SongDetailComponent),
        data: { title: 'Détail chant' }
      },
      {
        path: RoutePaths.Scores,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () => import('@app/components/scores/score-list/score-list.component').then(m => m.ScoreListComponent),
        data: { title: 'Partitions' }
      },
      {
        path: RoutePaths.Recordings,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () =>
          import('@app/components/recordings/recording-list/recording-list.component').then(
            m => m.RecordingListComponent
          ),
        data: { title: 'Enregistrements' }
      },
      {
        path: RoutePaths.SongLists,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () =>
          import('@app/components/song-lists/song-list-list/song-list-list.component').then(
            m => m.SongListListComponent
          ),
        data: { title: 'Listes de chants' }
      },
      {
        path: RoutePaths.SongListDetail,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () =>
          import('@app/components/song-lists/song-list-detail/song-list-detail.component').then(
            m => m.SongListDetailComponent
          ),
        data: { title: 'Détail de la liste de chants' }
      },
      {
        path: RoutePaths.Events,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () =>
          import('@app/components/events/event-list/event-list.component').then(m => m.EventListComponent),
        data: { title: 'Événements' }
      },
      {
        path: RoutePaths.EventDetail,
        canActivate: [spaceRoleGuard(MANAGEMENT_ROLES, CHOIR_ONLY)],
        loadComponent: () =>
          import('@app/components/events/event-detail/event-detail.component').then(m => m.EventDetailComponent),
        data: { title: 'Détail événement' }
      },
      // Aucune route « Consignes » : une consigne n'existe que rattachée à son chant, et se
      // gère donc depuis SongDetailComponent (décision produit, Spec/chorale/10-decisions.md).
      {
        path: RoutePaths.Activity,
        canActivate: [spaceRoleGuard([UserRoleEnum.Manager])],
        loadComponent: () => import('@app/components/shared/placeholder/placeholder.component').then(m => m.PlaceholderComponent),
        data: { title: 'Activité' }
      }
    ]
  },

  {
    path: RoutePaths.Me,
    canActivate: [authGuard],
    loadComponent: () => import('@app/components/layout/shell/shell.component').then(m => m.ShellComponent),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('@app/components/me/member-home/member-home.component').then(m => m.MemberHomeComponent),
        data: { title: 'Espace membre' }
      },
      {
        path: RoutePaths.Songs,
        loadComponent: () =>
          import('@app/components/me/member-songs/member-song-list/member-song-list.component').then(
            m => m.MemberSongListComponent
          ),
        data: { title: 'Mes chants' }
      },
      {
        path: RoutePaths.SongDetail,
        loadComponent: () =>
          import('@app/components/me/member-songs/member-song-detail/member-song-detail.component').then(
            m => m.MemberSongDetailComponent
          ),
        data: { title: 'Détail du chant' }
      }
    ]
  },

  {
    path: RoutePaths.NoSpace,
    canActivate: [authGuard],
    loadComponent: () => import('@app/components/no-space/no-space.component').then(m => m.NoSpaceComponent)
  },

  {
    path: '**',
    loadComponent: () => import('@app/components/shared/not-found/not-found.component').then(m => m.NotFoundComponent)
  }
];
