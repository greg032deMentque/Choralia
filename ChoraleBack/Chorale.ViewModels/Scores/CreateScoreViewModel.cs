using System.ComponentModel.DataAnnotations;
using ChoraleBackEnd.Common.Enums;
using Microsoft.AspNetCore.Http;

namespace ChoraleBackEnd.ViewModels.Scores;

public sealed class CreateScoreViewModel : IValidatableObject
{
    [Required]
    public IFormFile File { get; set; } = null!;

    [Required]
    public Guid SongId { get; set; }

    [Required]
    [EnumDataType(typeof(ScoreTypeEnum))]
    public ScoreTypeEnum Type { get; set; }

    public VoicePartEnum? TargetVoicePart { get; set; }

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    public bool DownloadAllowed { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == ScoreTypeEnum.ByVoicePart && TargetVoicePart is null)
            yield return new ValidationResult(
                "VoixCible est requis lorsque Type = ParVoix.", [nameof(TargetVoicePart)]);
    }
}
