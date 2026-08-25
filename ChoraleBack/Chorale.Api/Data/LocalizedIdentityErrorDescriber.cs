using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using System.Reflection;

namespace ChoraleBackEnd.Api.Data
{
    public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
    {
        private readonly IStringLocalizer _localizer;

        public LocalizedIdentityErrorDescriber(IStringLocalizerFactory factory)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;
            _localizer = factory.Create("IdentityErrorMessages", assemblyName);
        }

        public override IdentityError PasswordTooShort(int length)
            => new IdentityError
            {
                Code = nameof(PasswordTooShort),
                Description = _localizer[nameof(PasswordTooShort), length]
            };

        public override IdentityError PasswordRequiresDigit()
            => new IdentityError
            {
                Code = nameof(PasswordRequiresDigit),
                Description = _localizer[nameof(PasswordRequiresDigit)]
            };

        public override IdentityError PasswordRequiresUpper()
            => new IdentityError
            {
                Code = nameof(PasswordRequiresUpper),
                Description = _localizer[nameof(PasswordRequiresUpper)]
            };
    }


}
