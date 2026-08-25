using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;
using Microsoft.AspNetCore.Http;

namespace ChoraleBackEnd.ViewModels.Recordings;

public sealed class CreateRecordingViewModel : IValidatableObject
{
    [Required]
    public IFormFile File { get; set; } = null!;

    [Required]
    public Guid SongId { get; set; }

    [Required]
    [EnumDataType(typeof(RecordingTypeEnum))]
    public RecordingTypeEnum Type { get; set; }

    public VoicePartEnum? TargetVoicePart { get; set; }

    [Required]
    [MaxLength(200)]
    public string ContentOwner { get; set; } = string.Empty;

    public bool DownloadAllowed { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int DurationSeconds { get; set; }

    [Required]
    [EnumDataType(typeof(RecordingSourceEnum))]
    public RecordingSourceEnum Source { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == RecordingTypeEnum.ByVoicePart && TargetVoicePart is null)
            yield return new ValidationResult(
                "VoixCible est requis lorsque Type = ParVoix.", [nameof(TargetVoicePart)]);

        if (Source == RecordingSourceEnum.Shared)
            yield return new ValidationResult(
                "La source Partage n'est pas disponible via cet endpoint.", [nameof(Source)]);
    }
}
