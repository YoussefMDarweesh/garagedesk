using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Services;

/// <summary>
/// ASP.NET only translates validation messages that have an explicit ErrorMessage, and the built-in
/// English defaults ("The {0} field is required.") have no Arabic version. This gives the common
/// attributes an explicit, English-text ErrorMessage so the normal localizer can translate it.
/// It also covers the Identity library's own login/register models, which we can't edit.
/// </summary>
public class LocalizedValidationAttributeAdapterProvider : IValidationAttributeAdapterProvider
{
    private readonly ValidationAttributeAdapterProvider _inner = new();

    public IAttributeAdapter? GetAttributeAdapter(ValidationAttribute attribute, IStringLocalizer? stringLocalizer)
    {
        if (string.IsNullOrEmpty(attribute.ErrorMessage)
            && string.IsNullOrEmpty(attribute.ErrorMessageResourceName)
            && attribute.ErrorMessageResourceType is null)
        {
            attribute.ErrorMessage = attribute switch
            {
                RequiredAttribute => "The {0} field is required.",
                EmailAddressAttribute => "The {0} field is not a valid e-mail address.",
                StringLengthAttribute => "The field {0} must be a string with a maximum length of {1}.",
                _ => null,
            };
        }

        return _inner.GetAttributeAdapter(attribute, stringLocalizer);
    }
}

/// <summary>
/// Field names in messages ("The Email field is required.") come from the property name unless a
/// [Display] attribute says otherwise. This translates those names too.
/// </summary>
public class LocalizedDisplayNameProvider(IStringLocalizer localizer) : IDisplayMetadataProvider
{
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        if (context.DisplayMetadata.DisplayName is null
            && context.Key.MetadataKind == ModelMetadataKind.Property
            && context.Key.Name is { } propertyName)
        {
            context.DisplayMetadata.DisplayName = () => localizer[propertyName].Value;
        }
    }
}
