using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViciOne.Suite.DataPort;

internal static class JsonSetup
{
    internal static JsonSerializerOptions PreserveTypeOptions { get; } = CreatePreserveTypeOptions();

    internal static JsonSerializerOptions CreatePreserveTypeOptions(AssemblyLoadContext? assemblyLoadContext = default)
        => new()
        {
            Converters =
            {
                new JsonStringEnumConverter(),
                new TypeJsonConverter(assemblyLoadContext),
                new TypeNameHandlingConfig(assemblyLoadContext),
            },
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
}
