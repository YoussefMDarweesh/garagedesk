using GarageDesk.Web.Services;

namespace GarageDesk.Tests;

public class PhoneAndPlateTests
{
    // ---- Egyptian phone numbers ----

    [Theory]
    // Mobile, in every way a shop might type it
    [InlineData("01012345678", "+201012345678")]
    [InlineData("0101 234 5678", "+201012345678")]
    [InlineData("010-1234-5678", "+201012345678")]
    [InlineData("(010) 1234 5678", "+201012345678")]
    [InlineData("+201012345678", "+201012345678")]
    [InlineData("+20 101 234 5678", "+201012345678")]
    [InlineData("201012345678", "+201012345678")]
    [InlineData("00201012345678", "+201012345678")]
    [InlineData("+2001012345678", "+201012345678")]     // trunk 0 typed after the country code
    [InlineData("1012345678", "+201012345678")]         // leading 0 left out
    [InlineData("  01012345678  ", "+201012345678")]
    // Arabic-Indic and Persian digits (typing on an Arabic keyboard)
    [InlineData("٠١٠١٢٣٤٥٦٧٨", "+201012345678")]
    [InlineData("۰۱۰۱۲۳۴۵۶۷۸", "+201012345678")]
    // The other mobile operators
    [InlineData("01112345678", "+201112345678")]
    [InlineData("01212345678", "+201212345678")]
    [InlineData("01512345678", "+201512345678")]
    // Landlines: Cairo, Alexandria, another governorate
    [InlineData("0223456789", "+20223456789")]
    [InlineData("034567890", "+2034567890")]
    [InlineData("0403123456", "+20403123456")]
    public void Valid_numbers_are_stored_in_plus_20_form(string typed, string expected)
    {
        Assert.True(EgyptianPhone.TryNormalize(typed, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0101234567")]        // one digit short
    [InlineData("010123456789")]      // one digit too many
    [InlineData("01312345678")]       // 013 is not a mobile prefix
    [InlineData("01912345678")]       // neither is 019
    [InlineData("+441234567890")]     // another country
    [InlineData("0101-234-5678x")]    // stray letter
    [InlineData("0101+2345678")]      // plus in the middle
    [InlineData("12345")]
    public void Invalid_numbers_are_refused(string? typed)
    {
        Assert.False(EgyptianPhone.TryNormalize(typed, out var normalized));
        Assert.Equal("", normalized);
    }

    [Fact]
    public void Normalizing_twice_changes_nothing()
    {
        EgyptianPhone.TryNormalize("0101 234 5678", out var once);
        Assert.True(EgyptianPhone.TryNormalize(once, out var twice));
        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData("0101 234", "101234")]
    [InlineData("+20 1012", "1012")]
    [InlineData("00201012345", "1012345")]
    [InlineData("1012345678", "1012345678")]
    [InlineData("٠١٠١٢٣٤", "101234")]
    public void Phone_search_digits_ignore_the_prefix_the_shop_happens_to_type(string typed, string expected)
    {
        Assert.Equal(expected, EgyptianPhone.SearchDigits(typed));
    }

    [Theory]
    [InlineData("Ahmed")]   // no digits
    [InlineData("12")]      // too short to search phones with
    [InlineData("010")]     // only the prefix
    [InlineData("")]
    [InlineData(null)]
    public void Short_or_non_numeric_searches_do_not_search_phones(string? typed)
    {
        Assert.Null(EgyptianPhone.SearchDigits(typed));
    }

    // ---- Plate search keys ----

    [Theory]
    [InlineData("أ ب ج 1234", "ابج1234")]
    [InlineData("ا ب ج ١٢٣٤", "ابج1234")]          // Arabic digits and a plain alef give the same key
    [InlineData("abc-123", "ABC123")]
    [InlineData("  ABC 123  ", "ABC123")]
    [InlineData("1234 س ص ع", "1234سصع")]
    [InlineData("أحمد", "احمد")]
    [InlineData("---", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Plates_are_reduced_to_a_search_key(string? plate, string expected)
    {
        Assert.Equal(expected, PlateNormalizer.Key(plate));
    }
}
