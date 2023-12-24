using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;
using Core.Persistence.Paging;
using Core.Persistence.Dynamic;

namespace Core.Persistence.Repositories;

public interface IRepository<TEntity, TEntityId> : IQueryable<TEntity> where TEntity : Entity<TEntityId>
{
    TEntity? GetAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool withDeleted = false,
        bool enableTracking = true,
        CancellationToken cancellationToken = default);

    Paginate<TEntity> GetListAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        int index = 0,
        int size = 10,
        bool withDeleted = false,
        bool enableTracking = true,
        CancellationToken cancellationToken = default);
    
    Paginate<TEntity> GetListByDynamicAsync(
        DynamicQuery dynamic,
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        int index = 0,
        int size = 10,
        bool withDeleted = false,
        bool enableTracking = true,
        CancellationToken cancellationToken = default);

    TEntity? AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        bool withDeleted = false,
        bool enableTracking = true,
        CancellationToken cancellationToken = default);

    TEntity AddAsync(TEntity entity);

    ICollection<TEntity> AddRangeAsync(ICollection<TEntity> entity);

    TEntity UpdateAsync(TEntity entity);

    ICollection<TEntity> UpdateRangeAsync(ICollection<TEntity> entity);

    TEntity DeleteAsync(TEntity entity, bool permanent = false);

    ICollection<TEntity> DeleteRangeAsync(ICollection<TEntity> entity, bool permanent = false);
}
