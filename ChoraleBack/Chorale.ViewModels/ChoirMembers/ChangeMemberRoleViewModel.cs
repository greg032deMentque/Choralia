using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.ChoirMembers;

public sealed class ChangeMemberRoleViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [EnumDataType(typeof(UserRoleEnum))]
    public UserRoleEnum Role { get; set; }
}
