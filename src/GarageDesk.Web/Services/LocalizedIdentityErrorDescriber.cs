using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Services;

/// <summary>Identity's error texts ("Passwords must have at least one digit...") run through the same translation table.</summary>
public class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer) : IdentityErrorDescriber
{
    private IdentityError Localized(string code, string englishText, params object[] args) =>
        new() { Code = code, Description = localizer[englishText, args] };

    public override IdentityError DefaultError() =>
        Localized(nameof(DefaultError), "An unknown failure has occurred.");

    public override IdentityError PasswordMismatch() =>
        Localized(nameof(PasswordMismatch), "Incorrect password.");

    public override IdentityError InvalidUserName(string? userName) =>
        Localized(nameof(InvalidUserName), "Username '{0}' is invalid, can only contain letters or digits.", userName ?? "");

    public override IdentityError InvalidEmail(string? email) =>
        Localized(nameof(InvalidEmail), "Email '{0}' is invalid.", email ?? "");

    public override IdentityError DuplicateUserName(string userName) =>
        Localized(nameof(DuplicateUserName), "Username '{0}' is already taken.", userName);

    public override IdentityError DuplicateEmail(string email) =>
        Localized(nameof(DuplicateEmail), "Email '{0}' is already taken.", email);

    public override IdentityError PasswordTooShort(int length) =>
        Localized(nameof(PasswordTooShort), "Passwords must be at least {0} characters.", length);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Localized(nameof(PasswordRequiresUniqueChars), "Passwords must use at least {0} different characters.", uniqueChars);

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Localized(nameof(PasswordRequiresNonAlphanumeric), "Passwords must have at least one non alphanumeric character.");

    public override IdentityError PasswordRequiresDigit() =>
        Localized(nameof(PasswordRequiresDigit), "Passwords must have at least one digit ('0'-'9').");

    public override IdentityError PasswordRequiresLower() =>
        Localized(nameof(PasswordRequiresLower), "Passwords must have at least one lowercase ('a'-'z').");

    public override IdentityError PasswordRequiresUpper() =>
        Localized(nameof(PasswordRequiresUpper), "Passwords must have at least one uppercase ('A'-'Z').");
}
