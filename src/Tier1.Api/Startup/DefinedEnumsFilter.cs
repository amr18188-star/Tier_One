using System.Reflection;
using Tier1.Domain.Common;

namespace Tier1.Api.Startup;

/// <summary>
/// BE-7: an enum taken from the path or the query string must be one the code defines. Minimal API binds "7" to any enum,
/// so status=7 would reach a service as a value no switch handles. (JSON bodies are checked by the enum converter, which
/// accepts names only.) Flags enums are combinations by design and are not checked here.
/// </summary>
public sealed class DefinedEnumsFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is Enum value && value.GetType().GetCustomAttribute<FlagsAttribute>() is null && !Enum.IsDefined(value.GetType(), value))
                throw new DomainException("invalid_value", "قيمة غير مقبولة في الطلب.", "A value in the request is not accepted.", 400);
        }
        return next(context);
    }
}
