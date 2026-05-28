using AwesomeAssertions;
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
