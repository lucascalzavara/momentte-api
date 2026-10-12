using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Momentte.Api.Domain.DTOs.Auth;

public sealed class RegisterRequest : IValidatableObject
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;
    [Required, MinLength(8), MaxLength(72)]
    public string Password { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Password is not null && Encoding.UTF8.GetByteCount(Password) > 72)
            yield return new ValidationResult("A senha deve ter no máximo 72 bytes em UTF-8.", [nameof(Password)]);
    }
}
