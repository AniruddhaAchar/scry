using Google.Protobuf;
using Scry.Contracts.V1;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class ObjectContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DumpObjectResponse_RoundTrips()
    {
        var field = new ObjectField
        {
            Name = "_stringLength",
            Type = "System.Int32",
            Offset = 0,
            Value = "5"
        };

        var resp = new DumpObjectResponse
        {
            Found = true,
            Address = 0xabcUL,
            Type = "System.String",
            MethodTable = 0x7ff0UL,
            Size = 24UL,
            Fields = { field }
        };

        var bytes = resp.ToByteArray();
        var parsed = DumpObjectResponse.Parser.ParseFrom(bytes);

        Assert.True(parsed.Found);
        Assert.Equal(0xabcUL, parsed.Address);
        Assert.Equal("System.String", parsed.Type);
        Assert.Equal(0x7ff0UL, parsed.MethodTable);
        Assert.Equal(24UL, parsed.Size);

        Assert.Single(parsed.Fields);
        var parsedField = parsed.Fields[0];
        Assert.Equal("_stringLength", parsedField.Name);
        Assert.Equal("System.Int32", parsedField.Type);
        Assert.Equal(0, parsedField.Offset);
        Assert.Equal("5", parsedField.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DumpObjectResponse_NotFound_RoundTrips()
    {
        var resp = new DumpObjectResponse { Found = false };

        var bytes = resp.ToByteArray();
        var parsed = DumpObjectResponse.Parser.ParseFrom(bytes);

        Assert.False(parsed.Found);
        Assert.Empty(parsed.Fields);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DumpArrayResponse_RoundTrips()
    {
        var elem1 = new ArrayElement { Index = 0, Value = "63" };
        var elem2 = new ArrayElement { Index = 1, Value = "1" };

        var resp = new DumpArrayResponse
        {
            Found = true,
            Address = 0xdefUL,
            Type = "System.Int32[]",
            ElementType = "System.Int32",
            Length = 18,
            Truncated = true,
            Elements = { elem1, elem2 }
        };

        var bytes = resp.ToByteArray();
        var parsed = DumpArrayResponse.Parser.ParseFrom(bytes);

        Assert.True(parsed.Found);
        Assert.Equal(0xdefUL, parsed.Address);
        Assert.Equal("System.Int32[]", parsed.Type);
        Assert.Equal("System.Int32", parsed.ElementType);
        Assert.Equal(18, parsed.Length);
        Assert.True(parsed.Truncated);

        Assert.Equal(2, parsed.Elements.Count);
        Assert.Equal(0, parsed.Elements[0].Index);
        Assert.Equal("63", parsed.Elements[0].Value);
        Assert.Equal(1, parsed.Elements[1].Index);
        Assert.Equal("1", parsed.Elements[1].Value);
    }
}
