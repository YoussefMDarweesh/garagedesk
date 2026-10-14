namespace GarageDesk.Web.Services;

/// <summary>What the _Pager partial needs. Route holds the other query values (search text, status...) to keep.</summary>
public sealed record PagerModel(
    int Page, int PageCount, int Total, int PageSize, string PageName, IReadOnlyDictionary<string, string?> Route)
{
    public int FirstItem => Total == 0 ? 0 : (Page - 1) * PageSize + 1;

    public int LastItem => Math.Min(Total, Page * PageSize);

    public static int CountPages(int total, int pageSize) =>
        Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
}
