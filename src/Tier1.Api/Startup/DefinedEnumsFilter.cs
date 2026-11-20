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
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is Enum value && value.GetType().GetCustomAttribute<FlagsAttribute>() is null && !Enum.IsDefined(value.GetType(), value))
                throw new DomainException("invalid_value", "قيمة غير مقبولة في الطلب.", "A value in the request is not accepted.", 400) { Field = ParameterName(context, i) };
        }
        return next(context);
    }

    /// <summary>The name of the handler's i-th parameter (the query or route name it binds), for the error's field (BE-6).</summary>
    private static string? ParameterName(EndpointFilterInvocationContext context, int index) =>
        context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>()?.GetParameters() is { } parameters && index < parameters.Length
            ? parameters[index].Name
            : null;
}
