using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels.Clients;

public sealed class CreateClientViewModel
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [EmailAddress, MaxLength(256)]
    public string? ContactEmail { get; set; }
}
