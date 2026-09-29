namespace BlazorWebApp.Models;

public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = new List<T>();
    public int TotalRecords { get; set; }
}