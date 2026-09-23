using System.Linq;
using AwesomeAssertions;
using ViciOne.TreeBuilder.Rules;
using ViciOne.TreeBuilder.Rules.Yaml;
using ViciOne.TreeBuilder.Validation;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class Ruleset_
{
    [Fact]
    public void Is_valid()
    {
        var ruleset = RulesDeserializer.Deserialize("Azure.yaml");

        var result = ValidationManager.Validate(ruleset);

        result.IsValid.Should().BeTrue();
        result.Messages.Should().BeEmpty();
    }
}

public class Ruleset_NameValidations
{
    private static readonly Ruleset s_ruleset = RulesDeserializer.Deserialize("Azure.yaml");

    private static readonly string[] s_jsonNodeTypeIds =
    [
        "JsonStructure",
        "DataPointBool",
        "DataPointInteger",
        "DataPointFloat",
        "DataPointDateTime",
        "DataPointString",
        "DataPointBinary",
    ];

    public static MatrixTheoryData<string, string> NamesForbiddenAsTwinKeys { get; } =
        new(s_jsonNodeTypeIds, ["motor.temp", "$version", "Speed Set", "\u0085"]);

    public static MatrixTheoryData<string, string> ValidTwinKeys { get; } =
        new(s_jsonNodeTypeIds, ["motor_temp", "Motor-Temp1", new string('x', 128)]);

    public static TheoryData<string> JsonNodeTypes { get; } = [.. s_jsonNodeTypeIds];

    [Theory]
    [MemberData(nameof(NamesForbiddenAsTwinKeys))]
    public void Rejects_characters_azure_forbids_in_twin_keys(string nodeTypeId, string name)
        => IsValidName(nodeTypeId, name).Should().BeFalse();

    [Theory]
    [MemberData(nameof(ValidTwinKeys))]
    public void Accepts_valid_twin_keys(string nodeTypeId, string name)
        => IsValidName(nodeTypeId, name).Should().BeTrue();

    [Theory]
    [MemberData(nameof(JsonNodeTypes))]
    public void Rejects_names_longer_than_128_characters(string nodeTypeId)
        => IsValidName(nodeTypeId, new string('x', 129)).Should().BeFalse();

    private static bool IsValidName(string nodeTypeId, string name)
        => s_ruleset.NodeTypes.Single(n => n.Id == nodeTypeId).NameValidations.All(r => r.Validate(name).IsValid);
}
