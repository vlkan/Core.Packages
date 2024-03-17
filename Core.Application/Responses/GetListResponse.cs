using Core.Persistence.Paging;

namespace Core.Application.Responses;

public class GetListResponse<T> : BasePageablaModel
{
    private IList<T> _items;

    public IList<T> Items 
    { 
        get => _items ?? new List<T>(); 
        set => _items = value;
    }
}
