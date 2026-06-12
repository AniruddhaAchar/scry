using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>Dumps a page of an array's elements by address. Returns null if the address is not a valid array.</summary>
public sealed class DumpArrayCommand(ulong address, int offset, int limit) : IAnalysisCommand<ArrayDump?>
{
    private const int DefaultLimit = 1000;

    public ArrayDump? Execute(DumpSession session, CancellationToken ct)
    {
        var obj = session.Runtime.Heap.GetObject(address);
        if (!obj.IsValid || obj.Type is null || !obj.Type.IsArray)
        {
            return null;
        }

        var arr = obj.AsArray();
        var length = arr.Length;
        var componentType = obj.Type.ComponentType;
        // The element kind is the COMPONENT type's element type (e.g. Int32 for int[]),
        // not obj.Type.ElementType (which is the array's own kind — always SZArray/Array).
        var componentKind = componentType?.ElementType ?? default;

        var lim = limit <= 0 ? DefaultLimit : limit;
        var off = offset < 0 ? 0 : offset;
        var end = (int)Math.Min((long)off + lim, length);

        var elements = new List<ArrayElement>();
        for (var i = off; i < end; i++)
        {
            ct.ThrowIfCancellationRequested();
            elements.Add(new ArrayElement(i, FormatElement(arr, i, componentKind)));
        }

        var truncated = off + elements.Count < length;
        return new ArrayDump(
            obj.Address,
            obj.Type.Name ?? "<unknown>",
            componentType?.Name ?? "<unknown>",
            length,
            truncated,
            elements);
    }

    private static string? FormatElement(ClrArray arr, int i, ClrElementType kind)
    {
        switch (kind)
        {
            case ClrElementType.Boolean: return arr.GetValue<bool>(i).ToString();
            case ClrElementType.Char: return $"'{arr.GetValue<char>(i)}'";
            case ClrElementType.Int8: return arr.GetValue<sbyte>(i).ToString();
            case ClrElementType.UInt8: return arr.GetValue<byte>(i).ToString();
            case ClrElementType.Int16: return arr.GetValue<short>(i).ToString();
            case ClrElementType.UInt16: return arr.GetValue<ushort>(i).ToString();
            case ClrElementType.Int32: return arr.GetValue<int>(i).ToString();
            case ClrElementType.UInt32: return arr.GetValue<uint>(i).ToString();
            case ClrElementType.Int64: return arr.GetValue<long>(i).ToString();
            case ClrElementType.UInt64: return arr.GetValue<ulong>(i).ToString();
            case ClrElementType.Float: return arr.GetValue<float>(i).ToString();
            case ClrElementType.Double: return arr.GetValue<double>(i).ToString();
            case ClrElementType.NativeInt:
            case ClrElementType.NativeUInt:
            case ClrElementType.Pointer:
            case ClrElementType.FunctionPointer: return $"0x{arr.GetValue<nuint>(i):x}";
            case ClrElementType.String:
            case ClrElementType.Class:
            case ClrElementType.Object:
            case ClrElementType.Array:
            case ClrElementType.SZArray:
                {
                    var o = arr.GetObjectValue(i);
                    return o.Address == 0 ? "null" : $"0x{o.Address:x}";
                }
            default:
                return null;
        }
    }
}
