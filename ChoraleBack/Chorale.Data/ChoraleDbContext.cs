using ChoraleBackEnd.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Data;

public sealed class ChoraleDbContext : IdentityDbContext<User>
{
    public ChoraleDbContext(DbContextOptions<ChoraleDbContext> options) : base(options) { }

    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AnalyticLog> AnalyticLogs => Set<AnalyticLog>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientMember> ClientMembers => Set<ClientMember>();
    public DbSet<Choir> Choirs => Set<Choir>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<SpaceMember> SpaceMembers => Set<SpaceMember>();
    public DbSet<SpaceMemberRole> SpaceMemberRoles => Set<SpaceMemberRole>();
    public DbSet<SectionMember> SectionMembers => Set<SectionMember>();
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<SongVoicePart> SongVoiceParts => Set<SongVoicePart>();
    public DbSet<Score> Scores => Set<Score>();
    public DbSet<Recording> Recordings => Set<Recording>();
    public DbSet<SongList> SongLists => Set<SongList>();
    public DbSet<SongListSong> SongListSongs => Set<SongListSong>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Instruction> Instructions => Set<Instruction>();
    public DbSet<SpaceJoinCode> SpaceJoinCodes => Set<SpaceJoinCode>();
    public DbSet<MembershipRequest> MembershipRequests => Set<MembershipRequest>();

    public static Guid NewIdGuid() => Guid.CreateVersion7();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ChoraleDbContext).Assembly);
    }

    /// <summary>
    /// Toutes les dates du domaine sont en UTC, et le restent au retour de base.
    /// </summary>
    /// <remarks>
    /// Constate en exercant l'API : un evenement cree avec `2026-09-01T20:00:00Z`
    /// ressortait `2026-09-01T20:00:00` (sans marqueur de fuseau) une fois relu — SQL
    /// Server stocke en `datetime2` sans Kind, et EF renvoie `Unspecified`. Le meme champ
    /// sortait donc avec et sans `Z` selon le chemin, et un client qui parse les deux
    /// obtient deux instants differents.
    ///
    /// A l'ecriture, une valeur `Local` est ramenee en UTC ; une valeur `Unspecified` est
    /// consideree comme deja UTC (contrat de l'API). A la lecture, le Kind UTC est repose.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }
}

/// <summary>Ecrit en UTC, relit en Kind=Utc. Voir ChoraleDbContext.ConfigureConventions.</summary>
public sealed class UtcDateTimeConverter
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    { }
}

/// <summary>Variante nullable de <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class UtcNullableDateTimeConverter
    : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
{
    public UtcNullableDateTimeConverter() : base(
        v => v.HasValue ? v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v.Value : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    { }
}
