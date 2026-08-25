using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;

namespace ChoraleBackEnd.Api.Data;

public sealed class ProgramServiceDeclarator
{
    public static void ServicesDeclarator(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ILogService, LogService>();
        builder.Services.AddScoped<IPathService, PathService>();
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<IJwtGeneratorService, JwtGeneratorService>();
        builder.Services.AddScoped<IUserRoleDataService, UserRoleDataService>();
        builder.Services.AddScoped<ISpaceRoleResolverService, SpaceRoleResolverService>();
        builder.Services.AddScoped<ISpaceAccessAuditService, SpaceAccessAuditService>();
        builder.Services.AddScoped<IMembershipService, MembershipService>();
        builder.Services.AddScoped<IAccountService, AccountService>();
        builder.Services.AddScoped<ISingerService, SingerService>();
        builder.Services.AddScoped<IAdminUserQueryService, AdminUserQueryService>();
        builder.Services.AddScoped<IAdminUserService, AdminUserService>();
        builder.Services.AddScoped<IClientRoleResolverService, ClientRoleResolverService>();
        builder.Services.AddScoped<IServiceLimitService, ServiceLimitService>();
        builder.Services.AddScoped<IClientService, ClientService>();
        builder.Services.AddScoped<IChoirService, ChoirService>();
        builder.Services.AddScoped<IAdminChoirService, AdminChoirService>();
        builder.Services.AddScoped<ISectionService, SectionService>();
        builder.Services.AddScoped<ISectionVoicePartLookupService, SectionVoicePartLookupService>();
        builder.Services.AddScoped<IMemberEnrollmentService, MemberEnrollmentService>();
        builder.Services.AddScoped<IEventParticipationSeedingService, EventParticipationSeedingService>();
        builder.Services.AddScoped<IChoirMembersService, ChoirMembersService>();
        builder.Services.AddScoped<IChoirMasterService, ChoirMasterService>();
        builder.Services.AddScoped<ISongService, SongService>();
        builder.Services.AddScoped<IAdminSongService, AdminSongService>();
        builder.Services.AddScoped<IScoreFileService, ScoreFileService>();
        builder.Services.AddScoped<IChoirAuthorizationService, ChoirAuthorizationService>();
        builder.Services.AddScoped<IScoreAuthorizationService, ScoreAuthorizationService>();
        builder.Services.AddScoped<IScoreService, ScoreService>();
        builder.Services.AddScoped<IRecordingFileService, RecordingFileService>();
        builder.Services.AddScoped<IRecordingAuthorizationService, RecordingAuthorizationService>();
        builder.Services.AddScoped<IRecordingService, RecordingService>();
        builder.Services.AddScoped<ISongListService, SongListService>();
        builder.Services.AddScoped<IInstructionService, InstructionService>();
        builder.Services.AddScoped<IDashboardService, DashboardService>();
        builder.Services.AddScoped<IEventAuthorizationService, EventAuthorizationService>();
        builder.Services.AddScoped<IUserInvitationService, UserInvitationService>();
        builder.Services.AddScoped<IGuestAccountLifecycleService, GuestAccountLifecycleService>();
        builder.Services.AddScoped<IEventService, EventService>();
        builder.Services.AddScoped<IEventParticipantService, EventParticipantService>();
        builder.Services.AddScoped<IAdminEventService, AdminEventService>();
        builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        builder.Services.AddScoped<IAdminAuditListService, AdminAuditListService>();
        builder.Services.AddScoped<IRegistrationService, RegistrationService>();
        builder.Services.AddScoped<IJoinCodeService, JoinCodeService>();
        builder.Services.AddScoped<IMembershipRequestService, MembershipRequestService>();
        builder.Services.AddScoped<IOnboardingCreationService, OnboardingCreationService>();
    }
}
