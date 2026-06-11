using Google.Protobuf;
using Scry.Contracts.V1;
using Xunit;

namespace Scry.UnitTests;

[Trait("Category", "Unit")]
public sealed class HeapContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DumpHeapResponse_RoundTrips_TypStatAndObjects()
    {
        var typeStat = new HeapTypeStat
        {
            Type = "System.String",
            MethodTable = 0x2000UL,
            Count = 3,
            TotalSize = 99UL
        };

        var obj = new HeapObject
        {
            Address = 0xabcUL,
            Type = "System.String",
            Size = 24UL
        };

        var resp = new DumpHeapResponse
        {
            Stats = { typeStat },
            Objects = { obj }
        };

        var bytes = resp.ToByteArray();
        var parsed = DumpHeapResponse.Parser.ParseFrom(bytes);

        Assert.Single(parsed.Stats);
        var stat = parsed.Stats[0];
        Assert.Equal("System.String", stat.Type);
        Assert.Equal(0x2000UL, stat.MethodTable);
        Assert.Equal(3, stat.Count);
        Assert.Equal(99UL, stat.TotalSize);

        Assert.Single(parsed.Objects);
        var parsedObj = parsed.Objects[0];
        Assert.Equal(0xabcUL, parsedObj.Address);
        Assert.Equal("System.String", parsedObj.Type);
        Assert.Equal(24UL, parsedObj.Size);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DumpExceptionsResponse_RoundTrips_WithNestedInnerLinks()
    {
        var innerLink = new ExceptionLink
        {
            Type = "System.Exception",
            Message = "inner"
        };

        var exceptionInfo = new ExceptionInfo
        {
            Address = 0xdeadUL,
            Type = "System.InvalidOperationException",
            Message = "boom",
            Hresult = -2146233079,
            Inner = { innerLink }
        };

        var resp = new DumpExceptionsResponse
        {
            Exceptions = { exceptionInfo },
            TotalMatches = 1,
            Truncated = false
        };

        var bytes = resp.ToByteArray();
        var parsed = DumpExceptionsResponse.Parser.ParseFrom(bytes);

        Assert.Single(parsed.Exceptions);
        var exc = parsed.Exceptions[0];
        Assert.Equal(0xdeadUL, exc.Address);
        Assert.Equal("System.InvalidOperationException", exc.Type);
        Assert.Equal("boom", exc.Message);
        Assert.Equal(-2146233079, exc.Hresult);

        Assert.Single(exc.Inner);
        var inner = exc.Inner[0];
        Assert.Equal("System.Exception", inner.Type);
        Assert.Equal("inner", inner.Message);

        Assert.Equal(1, parsed.TotalMatches);
        Assert.False(parsed.Truncated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrintExceptionResponse_RoundTrips_WithStackTrace()
    {
        var frame = new StackFrame
        {
            Kind = "ManagedMethod",
            InstructionPointer = 0x7ff0UL,
            StackPointer = 0x1000UL,
            Method = "Foo.Bar",
            Type = "Foo",
            Module = "app.dll"
        };

        var exceptionInfo = new ExceptionInfo
        {
            Address = 0xf00dUL,
            Type = "System.InvalidOperationException",
            Message = "test",
            Hresult = -2146233079
        };

        var resp = new PrintExceptionResponse
        {
            Found = true,
            Exception = exceptionInfo,
            StackTrace = { frame }
        };

        var bytes = resp.ToByteArray();
        var parsed = PrintExceptionResponse.Parser.ParseFrom(bytes);

        Assert.True(parsed.Found);
        Assert.NotNull(parsed.Exception);
        Assert.Equal(0xf00dUL, parsed.Exception.Address);
        Assert.Equal("System.InvalidOperationException", parsed.Exception.Type);
        Assert.Equal("test", parsed.Exception.Message);
        Assert.Equal(-2146233079, parsed.Exception.Hresult);

        Assert.Single(parsed.StackTrace);
        var f = parsed.StackTrace[0];
        Assert.Equal("ManagedMethod", f.Kind);
        Assert.Equal(0x7ff0UL, f.InstructionPointer);
        Assert.Equal(0x1000UL, f.StackPointer);
        Assert.Equal("Foo.Bar", f.Method);
        Assert.Equal("Foo", f.Type);
        Assert.Equal("app.dll", f.Module);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PrintExceptionResponse_NotFound_RoundTrips()
    {
        var resp = new PrintExceptionResponse { Found = false };

        var bytes = resp.ToByteArray();
        var parsed = PrintExceptionResponse.Parser.ParseFrom(bytes);

        Assert.False(parsed.Found);
    }
}
