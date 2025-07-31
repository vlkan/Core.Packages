using FluentValidation;
using MediatR;

namespace Core.Application.Pipelines.Validation;

public class RequestValidatonBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ValidationContext<object> context = new(request);


    }
}
