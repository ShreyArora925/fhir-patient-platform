using Fhir.Application.Hl7;

namespace Fhir.UnitTests.Hl7;

public class Hl7HeaderTests
{
    [Fact]
    public void TryParse_ValidMessage_ReturnsHeader()
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "hl7", "ORU_R01.hl7"));

        Assert.True(Hl7Header.TryParse(raw, out var header, out var error));

        Assert.Null(error);
        Assert.Equal(new Hl7Header("TGH", "MSG00002", "ORU^R01"), header);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("PID|1||MRN12345", "must start with an MSH segment")]
    [InlineData("MSH|^~\\&|ADT1|TGH|FHIR|FHIR|20261004||ADT^A01|", "MSH-10")]
    public void TryParse_InvalidMessage_ReturnsReason(string raw, string expectedReason)
    {
        Assert.False(Hl7Header.TryParse(raw, out var header, out var error));

        Assert.Null(header);
        Assert.Contains(expectedReason, error);
    }
}
