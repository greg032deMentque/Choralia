using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Auth;

public sealed class SpaceRoleAssignmentViewModel
{
    public Guid SpaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SpaceTypeEnum SpaceType { get; set; }
    public List<string> Roles { get; set; } = [];
    public Guid ClientId { get; set; }
    public Guid? ChoirId { get; set; }
    public VoicePartEnum? PrimaryVoicePart { get; set; }
}
