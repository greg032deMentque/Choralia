using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.ChoirMembers;

public sealed class ChangeMemberStatusViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [EnumDataType(typeof(MemberStatusEnum))]
    public MemberStatusEnum Status { get; set; }
}
