using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Api.Data;
using ChoraleBackEnd.Api.Identity;
using ChoraleBackEnd.Common.Constants;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ChoraleBackEnd.Api.Extensions;

public static class SecurityBuilderExtensions
{
    private const int PasswordResetTokenLifespanHours = 1;

    public static void ConfigureIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddLocalization();

        builder.Services
            .AddIdentity<User, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;


                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 3;
                options.Lockout.AllowedForNewUsers = true;

                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+ ";

                options.User.RequireUniqueEmail = true;
            })
            // message erreur mdp personnalisé
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InvitationTokenProvider>(AccountTokenConstants.InvitationTokenProvider);

        // Durée de vie du jeton de réinitialisation de mot de passe. Elle ne concerne QUE le
        // « mot de passe oublié » : l'invitation a son propre fournisseur ci-dessous, sinon un
        // invité ouvrant son mail le lendemain ne pouvait plus rejoindre la chorale.
        builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(PasswordResetTokenLifespanHours);
        });

        // Une valeur nulle ou négative rendrait tout lien d'invitation mort à l'émission : on
        // retombe alors sur le défaut plutôt que de casser silencieusement le parcours.
        var configuredInvitationHours = builder.Configuration.GetValue<int?>("Invitation:TokenLifespanHours");
        var invitationLifespanHours = configuredInvitationHours is > 0
            ? configuredInvitationHours.Value
            : InvitationTokenProviderOptions.DefaultLifespanHours;

        builder.Services.Configure<InvitationTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(invitationLifespanHours);
        });
    }

    public static void ConfigureJwt(this WebApplicationBuilder builder)
    {
        var jwtSecret = builder.Configuration["JWTToken:Secret"]
            ?? throw new InvalidOperationException("JWT Secret manquant.");
        var jwtIssuer = builder.Configuration["JWTToken:Issuer"] ?? "";
        var jwtAudience = builder.Configuration["JWTToken:Audience"] ?? "";

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = true;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(20)
            };
        });
    }

    public static void ConfigureAuthorization(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuthorizationHandler, SpaceRoleAuthorizationHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, ClientRoleAuthorizationHandler>();
        builder.Services.AddScoped<ISpaceContextAccessor, SpaceContextAccessor>();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Bearer, policy =>
            {
                policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
            options.AddPolicy(AuthorizationPolicies.ChoirManagerOrSectionLeader, policy =>
                policy.Requirements.Add(new SpaceRoleRequirement(
                    UserRoleEnum.Manager, UserRoleEnum.SectionLeader)));
            options.AddPolicy(AuthorizationPolicies.ChoirManager, policy =>
                policy.Requirements.Add(new SpaceRoleRequirement(UserRoleEnum.Manager)));
            options.AddPolicy(AuthorizationPolicies.SpaceManager, policy =>
                policy.Requirements.Add(new SpaceRoleRequirement(
                    UserRoleEnum.Manager, UserRoleEnum.Organizer)));
            options.AddPolicy(AuthorizationPolicies.ClientManager, policy =>
                policy.Requirements.Add(new ClientRoleRequirement(UserRoleEnum.ClientManager)));
            options.AddPolicy(AuthorizationPolicies.AdminOrClientManager, policy =>
                policy.Requirements.Add(new ClientRoleRequirement(UserRoleEnum.ClientManager)));
        });
    }

    public static void ConfigureCors(this WebApplicationBuilder builder)
    {
        var frontendOrigin = builder.Configuration["Frontend:BaseUrl"];
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                if (!string.IsNullOrWhiteSpace(frontendOrigin))
                    policy.WithOrigins(frontendOrigin)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
            });
        });
    }
}
