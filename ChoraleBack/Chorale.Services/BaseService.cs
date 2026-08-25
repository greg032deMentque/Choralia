using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChoraleBackEnd.Services;

public abstract class BaseService
{
    public readonly ILogService _logger;
    public readonly IMapper _mapper;
    public readonly IServiceProvider _serviceProvider;
    public readonly IConfiguration _configuration;
    public readonly ChoraleDbContext _context;
    public readonly IHttpContextAccessor? _httpContextAccessor;
    public readonly UserManager<User> _userManager;
    public readonly RoleManager<IdentityRole> _roleManager;
    public readonly SignInManager<User> _signInManager;
    public string? _currentUserId { get; private set; }
    public IReadOnlyList<UserRoleEnum> _currentUserRoles { get; private set; } = [];

    private User? _currentUserCache;
    private bool _currentUserLoaded;

    protected BaseService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogService>();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
        _configuration = serviceProvider.GetRequiredService<IConfiguration>();
        _context = serviceProvider.GetRequiredService<ChoraleDbContext>();
        _httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>();
        _userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        _roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        _signInManager = serviceProvider.GetRequiredService<SignInManager<User>>();
        SetCurrentUserId();
        SetCurrentUserRoles();
    }

    private void SetCurrentUserId()
        => _currentUserId = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private void SetCurrentUserRoles()
        => _currentUserRoles = _httpContextAccessor?.HttpContext?.User?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => Enum.Parse<UserRoleEnum>(c.Value))
            .ToList() ?? [];

    /// <summary>
    /// Charge l'utilisateur courant a la demande, une seule fois par requete HTTP. Le
    /// chargement est paresseux : seules quelques regles metier ont besoin de l'entite
    /// complete, tout le reste du code se contente de <see cref="_currentUserId"/>, resolu
    /// depuis les claims sans acces base.
    /// </summary>
    protected async Task<User?> GetCurrentUserEntityAsync(CancellationToken ct = default)
    {
        if (_currentUserLoaded) return _currentUserCache;

        _currentUserLoaded = true;
        if (_currentUserId is null) return null;

        _currentUserCache = await _context.Users.FindAsync([_currentUserId], ct);
        return _currentUserCache;
    }
}
