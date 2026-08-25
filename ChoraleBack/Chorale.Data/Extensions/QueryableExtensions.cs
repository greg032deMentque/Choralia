namespace ChoraleBackEnd.Data.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<T> WhereNotDeleted<T>(this IQueryable<T> query)
        where T : class, IHasIsDeleted
        => query.Where(e => !e.IsDeleted);
}

public interface IHasIsDeleted
{
    bool IsDeleted { get; set; }
}
