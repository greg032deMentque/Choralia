using System.Net;
using System.Security.Cryptography;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChoraleBackEnd.Services.OnboardingServices;

/// <summary>
/// Code de rattachement d'espace (lot 6). Deux entrees publiques exploitent
/// <see cref="ResolveActiveSpaceByCodeAsync"/> — <c>PreviewCode</c> (anonyme) et
/// <c>RequestMembership</c> (authentifie) — et doivent produire EXACTEMENT le meme code HTTP et
/// le meme message pour toute raison d'echec (decision anti-enumeration) : code inconnu,
/// expire, revoque, space plein, client suspendu, choir non Publie, space supprime.
/// Fondre "espace plein" dans ce meme message generique est ce qui satisfait a la fois cette
/// regle et la regle voisine "ne jamais reveler l'etat de quota a un non-membre" — les deux
/// se rejoignent : le quota n'est justement PAS revele puisque la reponse ne distingue rien.
/// </summary>
public interface IJoinCodeService
{
    Task<JoinCodeViewModel> GetActiveAsync(Guid spaceId, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="durationDays"/> facultative, plafonnee a 90 jours (decision produit) —
    /// au-dela, la demande est refusee plutot que silencieusement bornee. Non fournie, le
    /// maximum s'applique par defaut.
    /// </summary>
    Task<JoinCodeViewModel> GenerateOrRotateAsync(
        Guid spaceId, int? durationDays = null, CancellationToken ct = default);
    Task DeactivateAsync(Guid spaceId, CancellationToken ct = default);
    Task<PreviewCodeViewModel> PreviewAsync(string? code, CancellationToken ct = default);
    Task<Space> ResolveActiveSpaceByCodeAsync(string? code, CancellationToken ct = default);

    /// <summary>
    /// Cree un code actif pour un espace TOUT JUSTE cree, sans l'ajouter encore a la base —
    /// l'appelant (creation auto-service) l'ajoute a la meme unite de travail que le reste de
    /// la transaction (chorale, pupitres, membre) et appelle <c>SaveChangesAsync</c> une seule
    /// fois. Aucune verification d'autorisation ici : un espace qui vient d'etre cree n'a pas
    /// encore de Responsable enregistre en base au moment de l'appel.
    /// </summary>
    SpaceJoinCode CreateActiveCodeForNewSpaceWithoutSave(Guid spaceId);
}

public sealed class JoinCodeService : BaseService, IJoinCodeService
{
    // Exclut 0/O, 1/I/L : alphabet sans caractere ambigu (decision produit).
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LongueurCode = 8;
    private const int MaxDurationDays = 90;
    private const int MaxAttempts = 5;
    private const string InvalidCodeMessage = "Code inconnu ou expiré.";
    private const string TooManyAttemptsMessage = "Trop de tentatives. Réessayez plus tard.";

    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    private readonly ISpaceRoleResolverService _spaceRoleResolverService;
    private readonly IServiceLimitService _serviceLimitService;
    private readonly IMemoryCache _memoryCache;

    public JoinCodeService(
        IServiceProvider serviceProvider,
        ISpaceRoleResolverService spaceRoleResolverService,
        IServiceLimitService serviceLimitService,
        IMemoryCache memoryCache)
        : base(serviceProvider)
    {
        _spaceRoleResolverService = spaceRoleResolverService;
        _serviceLimitService = serviceLimitService;
        _memoryCache = memoryCache;
    }

    public async Task<JoinCodeViewModel> GetActiveAsync(Guid spaceId, CancellationToken ct = default)
    {
        await EnsureManagerSpaceAsync(spaceId, ct);

        var active = await _context.SpaceJoinCodes
            .AsNoTracking()
            .Where(c => c.SpaceId == spaceId && c.IsActive)
            .FirstOrDefaultAsync(ct);

        return active is null
            ? new JoinCodeViewModel { IsActive = false }
            : new JoinCodeViewModel { Code = active.Code, ExpiresAt = active.ExpiresAt, IsActive = true };
    }

    public async Task<JoinCodeViewModel> GenerateOrRotateAsync(
        Guid spaceId, int? durationDays = null, CancellationToken ct = default)
    {
        await EnsureManagerSpaceAsync(spaceId, ct);
        await EnsureSpaceExistsAsync(spaceId, ct);

        if (durationDays is { } requeste && (requeste < 1 || requeste > MaxDurationDays))
            throw new CustomException(HttpStatusCode.BadRequest,
                $"La durée de validité ne peut pas dépasser {MaxDurationDays} jours.");

        // Rotation en un geste : l'ancien code cesse immediatement de fonctionner — pas de
        // periode de recouvrement ou les deux codes seraient valides.
        var previous = await _context.SpaceJoinCodes
            .Where(c => c.SpaceId == spaceId && c.IsActive)
            .FirstOrDefaultAsync(ct);
        if (previous is not null)
            previous.IsActive = false;

        var newItem = await CreateActiveCodeAsync(spaceId, durationDays ?? MaxDurationDays, ct);
        _context.SpaceJoinCodes.Add(newItem);
        await _context.SaveChangesAsync(ct);

        return new JoinCodeViewModel { Code = newItem.Code, ExpiresAt = newItem.ExpiresAt, IsActive = true };
    }

    public async Task DeactivateAsync(Guid spaceId, CancellationToken ct = default)
    {
        await EnsureManagerSpaceAsync(spaceId, ct);

        var active = await _context.SpaceJoinCodes
            .Where(c => c.SpaceId == spaceId && c.IsActive)
            .FirstOrDefaultAsync(ct);
        if (active is null) return;

        active.IsActive = false;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<PreviewCodeViewModel> PreviewAsync(string? code, CancellationToken ct = default)
    {
        var space = await ResolveActiveSpaceByCodeAsync(code, ct);

        var name = await _context.Choirs
            .AsNoTracking()
            .Where(c => c.Id == space.Id)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);

        return new PreviewCodeViewModel { Name = name ?? string.Empty, SpaceType = space.SpaceType };
    }

    public async Task<Space> ResolveActiveSpaceByCodeAsync(string? code, CancellationToken ct = default)
    {
        EnsureNotRateLimited();

        var normalise = NormalizeCode(code);
        if (normalise is null)
        {
            RecordFailure();
            throw new CustomException(HttpStatusCode.BadRequest, InvalidCodeMessage);
        }

        var maintenant = DateTime.UtcNow;
        var row = await _context.SpaceJoinCodes
            .AsNoTracking()
            .Include(c => c.Space).ThenInclude(e => e.Client)
            .Where(c => c.Code == normalise && c.IsActive && c.ExpiresAt > maintenant)
            .FirstOrDefaultAsync(ct);

        if (row is null || row.Space.IsDeleted)
        {
            RecordFailure();
            throw new CustomException(HttpStatusCode.BadRequest, InvalidCodeMessage);
        }

        // Restreint aux espaces de type Chorale : le code de rattachement et la demande
        // d'adhesion portent, dans ce lot, sur le parcours "rejoindre une chorale" — un
        // evenement autonome ne passe pas par ce canal (decision assumee, a documenter).
        if (row.Space.SpaceType != SpaceTypeEnum.Choir
            || row.Space.Client.Status != ClientStatusEnum.Active)
        {
            RecordFailure();
            throw new CustomException(HttpStatusCode.BadRequest, InvalidCodeMessage);
        }

        var choir = await _context.Choirs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == row.SpaceId, ct);

        if (choir is null || choir.Status != ChoirStatusEnum.Published)
        {
            RecordFailure();
            throw new CustomException(HttpStatusCode.BadRequest, InvalidCodeMessage);
        }

        // "Espace plein" fond dans le meme message generique que tout autre echec : c'est ce
        // qui garantit qu'aucun etat de quota n'est revele a un non-membre (decision produit),
        // tout en respectant la regle d'anti-enumeration (meme code, meme message partout).
        var usage = await _serviceLimitService.GetUsageAsync(row.Space.ClientId, ct);
        if (usage.Members >= usage.MemberLimit)
        {
            RecordFailure();
            throw new CustomException(HttpStatusCode.BadRequest, InvalidCodeMessage);
        }

        return row.Space;
    }

    public SpaceJoinCode CreateActiveCodeForNewSpaceWithoutSave(Guid spaceId)
        => new()
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceId = spaceId,
            Code = Format(GenerateRawCode()),
            ExpiresAt = DateTime.UtcNow.AddDays(MaxDurationDays),
            IsActive = true,
            IsDeleted = false
        };

    private async Task<SpaceJoinCode> CreateActiveCodeAsync(Guid spaceId, int durationDays, CancellationToken ct)
    {
        // Collision virtuellement impossible (31^8 combinaisons parmi les codes ACTIFS), mais
        // une verification explicite reste moins couteuse que le risque d'un index unique
        // viole en production.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = Format(GenerateRawCode());
            var exists = await _context.SpaceJoinCodes
                .AnyAsync(c => c.Code == code && c.IsActive, ct);
            if (!exists)
            {
                return new SpaceJoinCode
                {
                    Id = ChoraleDbContext.NewIdGuid(),
                    SpaceId = spaceId,
                    Code = code,
                    ExpiresAt = DateTime.UtcNow.AddDays(durationDays),
                    IsActive = true,
                    IsDeleted = false
                };
            }
        }

        throw new CustomException(HttpStatusCode.Conflict, "Impossible de générer un code de assignment unique. Réessayez.");
    }

    private static string GenerateRawCode()
    {
        var chars = new char[LongueurCode];
        for (var i = 0; i < LongueurCode; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    private static string Format(string codeBrut) => $"{codeBrut[..4]}-{codeBrut[4..]}";

    private static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var nettoye = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return nettoye.Length == LongueurCode ? Format(nettoye) : null;
    }

    private async Task EnsureSpaceExistsAsync(Guid spaceId, CancellationToken ct)
    {
        var exists = await _context.Spaces.AsNoTracking().AnyAsync(e => e.Id == spaceId && !e.IsDeleted, ct);
        if (!exists)
            throw new KeyNotFoundException($"Space {spaceId} not found.");
    }

    /// <summary>
    /// Reservee au Responsable de l'espace — matrice `02`, meme regle que pour l'invitation et
    /// l'affectation. L'Organizer d'un evenement n'y a pas acces : ce controle est plus
    /// strict que la policy HTTP generique <c>SpaceManager</c>.
    /// </summary>
    private async Task EnsureManagerSpaceAsync(Guid spaceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
            throw new CustomException(HttpStatusCode.Forbidden, "Action réservée au chef de chœur.");

        var rolesBySpace = await _spaceRoleResolverService.ResolveRolesAsync(_currentUserId, [spaceId], ct);
        if (rolesBySpace.TryGetValue(spaceId, out var roles) && roles.Contains(UserRoleEnum.Manager))
            return;

        throw new CustomException(HttpStatusCode.Forbidden, "Action réservée au chef de chœur.");
    }

    private void EnsureNotRateLimited()
    {
        EnsureCounterNotReached(IpKey());
        var userKey = UserKey();
        if (userKey is not null)
            EnsureCounterNotReached(userKey);
    }

    private void RecordFailure()
    {
        IncrementCounter(IpKey());
        var userKey = UserKey();
        if (userKey is not null)
            IncrementCounter(userKey);
    }

    private void EnsureCounterNotReached(string key)
    {
        var counter = _memoryCache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AttemptWindow;
            return new AttemptCounter();
        })!;

        if (counter.Count >= MaxAttempts)
            throw new CustomException(HttpStatusCode.TooManyRequests, TooManyAttemptsMessage);
    }

    private void IncrementCounter(string key)
    {
        var counter = _memoryCache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AttemptWindow;
            return new AttemptCounter();
        })!;
        counter.Count++;
    }

    private string IpKey()
    {
        var ip = _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "inconnu";
        return $"coderattachement:ip:{ip}";
    }

    private string? UserKey()
        => string.IsNullOrWhiteSpace(_currentUserId) ? null : $"coderattachement:user:{_currentUserId}";

    private sealed class AttemptCounter
    {
        public int Count;
    }
}
