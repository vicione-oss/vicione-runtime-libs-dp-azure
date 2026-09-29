using AwesomeAssertions;
using ViciOne.Tree.Builder.Rules.Yaml;
using ViciOne.Tree.Builder.Validation;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class Ruleset_
{
    [Fact]
    public void Is_valid()
    {
        var ruleset = RulesDeserializer.Deserialize("AzureEventGridTopic.yaml");

        var result = ValidationManager.Validate(ruleset);

        result.IsValid.Should().BeTrue();
        result.Messages.Should().BeEmpty();
    }
}
