using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SIL.Motif.Cli;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class CommandResponseTextRenderingCoverageTests
{
    private static readonly Type[] Renderers = [typeof(CommandTextRenderer), typeof(ProposalCommandRenderer)];

    [Fact]
    public void UnknownAbstractResponseTypesFailInsteadOfBeingTreatedAsConcrete()
    {
        Assert.Throws<InvalidOperationException>(() => ConcreteResponseTypes(typeof(UnregisteredResponse)));
    }

    [Fact]
    public void EveryCliCommandResponseHasATextRenderer()
    {
        var verbs = CliVerbCatalog.All;
        var commands = CommandCatalog.All;
        Assert.NotEmpty(verbs);
        Assert.NotEmpty(commands);

        var responseTypes = verbs
            .Select(verb => commands.Single(command => command.Name == verb.CommandName).ResponseType)
            .SelectMany(ConcreteResponseTypes)
            .Distinct()
            .ToArray();
        Assert.NotEmpty(responseTypes);
        Assert.All(responseTypes, responseType => Assert.False(responseType.IsAbstract || responseType.IsInterface));

        var missing = responseTypes
            .Where(responseType => !Renderers.Any(renderer => HasTextRenderer(renderer, responseType)))
            .Select(responseType => responseType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            "CLI command response types with no text rendering: " + string.Join(", ", missing));
    }

    private static bool HasTextRenderer(Type rendererType, Type responseType)
    {
        object response;
        try
        {
            response = RuntimeHelpers.GetUninitializedObject(responseType);
        }
        catch (MemberAccessException)
        {
            return false;
        }
        var outcomeType = typeof(CommandOutcome<>).MakeGenericType(responseType);
        var outcome = outcomeType.GetMethod(nameof(CommandOutcome<object>.Success))!.Invoke(null, [response]);
        var render = rendererType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(CommandTextRenderer.Render) && method.IsGenericMethodDefinition)
            .MakeGenericMethod(responseType);

        try
        {
            var arguments = render.GetParameters().Select(parameter => parameter.Position switch
            {
                0 => outcome,
                1 => false,
                _ => parameter.DefaultValue,
            }).ToArray();
            render.Invoke(null, arguments);
            return true;
        }
        catch (TargetInvocationException exception) when (
            exception.InnerException is NotSupportedException unsupported &&
            unsupported.Message.StartsWith("No text rendering registered for", StringComparison.Ordinal))
        {
            return false;
        }
        catch (TargetInvocationException)
        {
            return true;
        }
    }

    private static Type[] ConcreteResponseTypes(Type responseType)
    {
        if (!responseType.IsAbstract && !responseType.IsInterface) return [responseType];

        var concreteTypes = responseType.GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.DerivedType)
            .Where(candidate => candidate.IsClass && !candidate.IsAbstract && responseType.IsAssignableFrom(candidate))
            .ToArray();
        if (concreteTypes.Length == 0 && responseType.GetCustomAttribute<JsonConverterAttribute>() is not null)
            concreteTypes = responseType.Assembly.GetTypes()
                .Where(candidate => candidate.IsClass && !candidate.IsAbstract && responseType.IsAssignableFrom(candidate))
                .ToArray();
        if (concreteTypes.Length == 0)
            throw new InvalidOperationException(
                $"Abstract CLI response '{responseType.FullName}' has no explicit concrete response types.");
        return concreteTypes;
    }

    private abstract record UnregisteredResponse;
}
