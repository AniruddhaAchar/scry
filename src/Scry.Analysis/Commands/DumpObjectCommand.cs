using Microsoft.Diagnostics.Runtime;
using Scry.Analysis.Model;

namespace Scry.Analysis.Commands;

/// <summary>Dumps one object's instance fields by address. Returns null if the address is not a valid object.</summary>
public sealed class DumpObjectCommand(ulong address) : IAnalysisCommand<ObjectDump?>
{
    public ObjectDump? Execute(DumpSession session, CancellationToken ct)
    {
        var obj = session.Runtime.Heap.GetObject(address);
        if (!obj.IsValid || obj.Type is null)
        {
            return null;
        }

        var fields = new List<ObjectField>();
        foreach (var field in obj.Type.Fields)
        {
            ct.ThrowIfCancellationRequested();
            fields.Add(new ObjectField(
                field.Name ?? "<unknown>",
                field.Type?.Name ?? "<unknown>",
                field.Offset,
                FormatField(field, obj.Address)));
        }

        return new ObjectDump(obj.Address, obj.Type.Name ?? "<unknown>", obj.Type.MethodTable, obj.Size, fields);
    }

    private static string? FormatField(ClrInstanceField field, ulong objAddr)
    {
        switch (field.ElementType)
        {
            case ClrElementType.Boolean: return field.Read<bool>(objAddr, interior: false).ToString();
            case ClrElementType.Char: return $"'{field.Read<char>(objAddr, false)}'";
            case ClrElementType.Int8: return field.Read<sbyte>(objAddr, false).ToString();
            case ClrElementType.UInt8: return field.Read<byte>(objAddr, false).ToString();
            case ClrElementType.Int16: return field.Read<short>(objAddr, false).ToString();
            case ClrElementType.UInt16: return field.Read<ushort>(objAddr, false).ToString();
            case ClrElementType.Int32: return field.Read<int>(objAddr, false).ToString();
            case ClrElementType.UInt32: return field.Read<uint>(objAddr, false).ToString();
            case ClrElementType.Int64: return field.Read<long>(objAddr, false).ToString();
            case ClrElementType.UInt64: return field.Read<ulong>(objAddr, false).ToString();
            case ClrElementType.Float: return field.Read<float>(objAddr, false).ToString();
            case ClrElementType.Double: return field.Read<double>(objAddr, false).ToString();
            case ClrElementType.NativeInt:
            case ClrElementType.NativeUInt:
            case ClrElementType.Pointer:
            case ClrElementType.FunctionPointer: return $"0x{field.Read<nuint>(objAddr, false):x}";
            case ClrElementType.String:
                {
                    var s = field.ReadString(objAddr, false);
                    return s is null ? "null" : ValueText.Quote(s);
                }
            case ClrElementType.Class:
            case ClrElementType.Object:
            case ClrElementType.Array:
            case ClrElementType.SZArray:
                {
                    var o = field.ReadObject(objAddr, false);
                    return o.Address == 0 ? "null" : $"0x{o.Address:x}";
                }
            default:
                return null; // structs (inline) and anything else: Type column carries the name
        }
    }
}
