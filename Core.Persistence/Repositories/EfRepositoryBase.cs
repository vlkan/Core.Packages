using Core.Persistence.Dynamic;
using Core.Persistence.Paging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Core.Persistence.Repositories;

public class EfRepositoryBase<TEntity, TId, TContext>
    : IAsyncRepository<TEntity, TId>, IRepository<TEntity, TId> 
    where TEntity : Entity<TId>
    where TContext : DbContext
{
    protected readonly TContext context;

    public EfRepositoryBase(TContext context)
    {
        this.context = context;
    }

    public async Task<TEntity> AddAsync(TEntity entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        await context.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<ICollection<TEntity>> AddRangeAsync(ICollection<TEntity> entities)
    {
        foreach (var entity in entities) 
            entity.CreatedAt = DateTime.UtcNow;

        await context.AddAsync(entities);
        await context.SaveChangesAsync();
        return entities;

    }

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, bool withDeleted = false, bool enableTracking = true, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> queryable = Query();
        if (enableTracking is false)
            queryable = queryable.AsNoTracking();
        if (withDeleted is true)
            queryable = queryable.IgnoreQueryFilters();
        if(predicate is not null)
            queryable = queryable.Where(predicate);
        return await queryable.AnyAsync(cancellationToken);
    }

    public async Task<TEntity> DeleteAsync(TEntity entity, bool permanent = false)
    {
        await SetEntityAsDeletedAsync(entity, permanent);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<ICollection<TEntity>> DeleteRangeAsync(ICollection<TEntity> entities, bool permanent = false)
    {
        await SetEntityAsDeletedAsync(entities, permanent);
        await context.SaveChangesAsync();
        return entities;
    }

    public async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, bool withDeleted = false, bool enableTracking = true, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> queryable = Query();
        if (!enableTracking)
            queryable = queryable.AsNoTracking();
        if (include != null)
            queryable = include(queryable);
        if (withDeleted)
            queryable = queryable.IgnoreQueryFilters();
        return await queryable.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<Paginate<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, int index = 0, int size = 10, bool withDeleted = false, bool enableTracking = true, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> queryable = Query();
        if (!enableTracking)
            queryable = queryable.AsNoTracking();
        if (include != null)
            queryable = include(queryable);
        if (withDeleted)
            queryable = queryable.IgnoreQueryFilters();
        if (predicate != null)
            queryable = queryable.Where(predicate);
        if (orderBy != null)
            return await orderBy(queryable).ToPaginateAsync(index, size, cancellationToken);
        return await queryable.ToPaginateAsync(index, size, cancellationToken);
    }

    public async Task<Paginate<TEntity>> GetListByDynamicAsync(DynamicQuery dynamic, Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, int index = 0, int size = 10, bool withDeleted = false, bool enableTracking = true, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> queryable = Query().ToDynamic(dynamic);
        if (!enableTracking)
            queryable = queryable.AsNoTracking();
        if (include != null)
            queryable = include(queryable);
        if (withDeleted)
            queryable = queryable.IgnoreQueryFilters();
        if (predicate != null)
            queryable = queryable.Where(predicate);
        return await queryable.ToPaginateAsync(index, size, cancellationToken);
    }

    public IQueryable<TEntity> Query() => context.Set<TEntity>();

    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        context.Update(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<ICollection<TEntity>> UpdateRangeAsync(ICollection<TEntity> entities)
    {
        foreach (TEntity entity in entities)
            entity.UpdatedAt = DateTime.UtcNow;
        context.UpdateRange(entities);
        await context.SaveChangesAsync();
        return entities;
    }

    protected async Task SetEntityAsDeletedAsync(TEntity entity, bool permanent)
    {
        if(permanent is false)
        {
            CheckHasEntityHaveOneToOneReleation(entity);
            await setEntityAsSoftDeletedAsync(entity);
        }
        else
        {
            context.Remove(entity);
        }
    }

    protected void CheckHasEntityHaveOneToOneReleation(TEntity entity)
    {
        bool hasEntityHaveOneToOneRelation =
            context.Entry(entity)
            .Metadata.GetForeignKeys()
            .All(
                x => x.DependentToPrincipal?.IsCollection == true
                || x.PrincipalToDependent?.IsCollection == true
                || x.DependentToPrincipal?.ForeignKey.DeclaringEntityType.ClrType == entity.GetType()) == false;
        if (hasEntityHaveOneToOneRelation)
            throw new InvalidOperationException("Entity has one-to-one relationship");
    }

    private async Task setEntityAsSoftDeletedAsync(IEntityTimestamps entity)
    {
        if (entity.DeletedAt.HasValue)
            return;
        entity.DeletedAt = DateTime.UtcNow;

        var navigations = context
            .Entry(entity)
            .Metadata.GetNavigations()
            .Where(x => x is { IsOnDependent: false, ForeignKey.DeleteBehavior: DeleteBehavior.ClientCascade or DeleteBehavior.Cascade })
            .ToList();
        foreach (INavigation? navigation in navigations)
        {
            if (navigation.TargetEntityType.IsOwned())
                continue;
            if (navigation.PropertyInfo == null)
                continue;

            object? navValue = navigation.PropertyInfo.GetValue(entity);
            if (navigation.IsCollection)
            {
                if (navValue == null)
                {
                    IQueryable query = context.Entry(entity).Collection(navigation.PropertyInfo.Name).Query();
                    navValue = await GetRelationLoaderQuery(query, navigationPropertyType: navigation.PropertyInfo.GetType()).ToListAsync();
                    if (navValue == null)
                        continue;
                }

                foreach (IEntityTimestamps navValueItem in (IEnumerable)navValue)
                    await setEntityAsSoftDeletedAsync(navValueItem);
            }
            else
            {
                if (navValue == null)
                {
                    IQueryable query = context.Entry(entity).Reference(navigation.PropertyInfo.Name).Query();
                    navValue = await GetRelationLoaderQuery(query, navigationPropertyType: navigation.PropertyInfo.GetType())
                        .FirstOrDefaultAsync();
                    if (navValue == null)
                        continue;
                }

                await setEntityAsSoftDeletedAsync((IEntityTimestamps)navValue);
            }
        }

        context.Update(entity);
    }

    protected IQueryable<object> GetRelationLoaderQuery(IQueryable query, Type navigationPropertyType)
    {
        Type queryProviderType = query.Provider.GetType();
        MethodInfo createQueryMethod =
            queryProviderType
                .GetMethods()
                .First(m => m is { Name: nameof(query.Provider.CreateQuery), IsGenericMethod: true })
                ?.MakeGenericMethod(navigationPropertyType)
            ?? throw new InvalidOperationException("CreateQuery<TElement> method is not found in IQueryProvider.");
        var queryProviderQuery =
            (IQueryable<object>)createQueryMethod.Invoke(query.Provider, parameters: new object[] { query.Expression })!;
        return queryProviderQuery.Where(x => !((IEntityTimestamps)x).DeletedAt.HasValue);
    }

    protected async Task SetEntityAsDeletedAsync(IEnumerable<TEntity> entities, bool permanent)
    {
        foreach (TEntity entity in entities)
            await SetEntityAsDeletedAsync(entity, permanent);
    }

    TEntity? IRepository<TEntity, TId>.GetAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include, bool withDeleted, bool enableTracking, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    Paginate<TEntity> IRepository<TEntity, TId>.GetListAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include, int index, int size, bool withDeleted, bool enableTracking, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    Paginate<TEntity> IRepository<TEntity, TId>.GetListByDynamicAsync(DynamicQuery dynamic, Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include, int index, int size, bool withDeleted, bool enableTracking, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    bool IRepository<TEntity, TId>.AnyAsync(Expression<Func<TEntity, bool>> predicate, bool withDeleted, bool enableTracking, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    TEntity IRepository<TEntity, TId>.AddAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    ICollection<TEntity> IRepository<TEntity, TId>.AddRangeAsync(ICollection<TEntity> entity)
    {
        throw new NotImplementedException();
    }

    TEntity IRepository<TEntity, TId>.UpdateAsync(TEntity entity)
    {
        throw new NotImplementedException();
    }

    ICollection<TEntity> IRepository<TEntity, TId>.UpdateRangeAsync(ICollection<TEntity> entity)
    {
        throw new NotImplementedException();
    }

    TEntity IRepository<TEntity, TId>.DeleteAsync(TEntity entity, bool permanent)
    {
        throw new NotImplementedException();
    }

    ICollection<TEntity> IRepository<TEntity, TId>.DeleteRangeAsync(ICollection<TEntity> entity, bool permanent)
    {
        throw new NotImplementedException();
    }
}
