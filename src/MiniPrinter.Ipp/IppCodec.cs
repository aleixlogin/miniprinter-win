using System.Buffers.Binary;
using System.Text;

namespace MiniPrinter.Ipp;

/// <summary>Binary IPP encoding (RFC 8010): reads requests and writes responses.</summary>
public static class IppCodec
{
    /// <summary>
    /// Reads the IPP header and attribute groups. The stream is left positioned at the start of
    /// the document data that may follow the end-of-attributes tag.
    /// </summary>
    public static async Task<IppMessage> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var reader = new Reader(stream);
        var message = new IppMessage
        {
            VersionMajor = await reader.ByteAsync(cancellationToken),
            VersionMinor = await reader.ByteAsync(cancellationToken),
            Code = (short)await reader.UInt16Async(cancellationToken),
            RequestId = await reader.Int32Async(cancellationToken),
        };

        IppGroup? group = null;
        IppAttribute? attribute = null;
        var collections = new Stack<(IppCollection Collection, string? PendingMember, List<IppValue>? MemberValues)>();

        while (true)
        {
            var tag = await reader.ByteAsync(cancellationToken);
            if (tag == (byte)IppGroupTag.End)
                break;
            if (tag < 0x10)
            {
                if (collections.Count > 0)
                    throw new InvalidDataException("Unterminated collection.");
                group = new IppGroup((IppGroupTag)tag);
                message.Groups.Add(group);
                attribute = null;
                continue;
            }
            if (group is null)
                throw new InvalidDataException("Attribute outside of a group.");

            var name = await reader.StringAsync(await reader.UInt16Async(cancellationToken), cancellationToken);
            var length = await reader.UInt16Async(cancellationToken);
            var raw = await reader.BytesAsync(length, cancellationToken);
            var valueTag = (IppTag)tag;

            if (collections.Count > 0)
            {
                // Inside a collection: memberAttrName introduces a member; values follow with empty names.
                var (collection, pending, values) = collections.Pop();
                switch (valueTag)
                {
                    case IppTag.MemberAttrName:
                        Flush(collection, pending, values);
                        collections.Push((collection, Encoding.UTF8.GetString(raw), []));
                        break;
                    case IppTag.EndCollection:
                        Flush(collection, pending, values);
                        if (collections.Count > 0)
                        {
                            var parent = collections.Peek();
                            parent.MemberValues!.Add(IppValue.Collection(collection));
                        }
                        else
                        {
                            AddValue(ref attribute, group, name: null, IppValue.Collection(collection));
                        }
                        break;
                    case IppTag.BeginCollection:
                        collections.Push((collection, pending, values));
                        collections.Push((new IppCollection(), null, null));
                        break;
                    default:
                        values?.Add(Decode(valueTag, raw));
                        collections.Push((collection, pending, values));
                        break;
                }
                continue;
            }

            if (valueTag == IppTag.BeginCollection)
            {
                if (name.Length > 0)
                {
                    attribute = new IppAttribute(name, new List<IppValue>());
                    group.Attributes.Add(attribute);
                }
                collections.Push((new IppCollection(), null, null));
                continue;
            }

            AddValue(ref attribute, group, name.Length > 0 ? name : null, Decode(valueTag, raw));
        }
        return message;
    }

    public static byte[] Encode(IppMessage message)
    {
        using var output = new MemoryStream();
        Write(output, message);
        return output.ToArray();
    }

    public static void Write(Stream output, IppMessage message)
    {
        var w = new BinaryWriterBE(output);
        w.Byte(message.VersionMajor);
        w.Byte(message.VersionMinor);
        w.UInt16((ushort)message.Code);
        w.Int32(message.RequestId);
        foreach (var group in message.Groups)
        {
            w.Byte((byte)group.Tag);
            foreach (var attribute in group.Attributes)
                WriteAttribute(w, attribute.Name, attribute.Values);
        }
        w.Byte((byte)IppGroupTag.End);
    }

    private static void WriteAttribute(BinaryWriterBE w, string name, IReadOnlyList<IppValue> values)
    {
        for (var i = 0; i < values.Count; i++)
            WriteValue(w, i == 0 ? name : "", values[i]);
    }

    private static void WriteValue(BinaryWriterBE w, string name, IppValue value)
    {
        if (value.Value is IppCollection collection)
        {
            w.Byte((byte)IppTag.BeginCollection);
            w.String16(name);
            w.UInt16(0);
            foreach (var member in collection)
            {
                w.Byte((byte)IppTag.MemberAttrName);
                w.UInt16(0);
                w.String16(member.Name);
                foreach (var memberValue in member.Values)
                    WriteValue(w, "", memberValue);
            }
            w.Byte((byte)IppTag.EndCollection);
            w.UInt16(0);
            w.UInt16(0);
            return;
        }

        w.Byte((byte)value.Tag);
        w.String16(name);
        switch (value.Value)
        {
            case null:
                w.UInt16(0);
                break;
            case int i:
                w.UInt16(4);
                w.Int32(i);
                break;
            case bool b:
                w.UInt16(1);
                w.Byte(b ? (byte)1 : (byte)0);
                break;
            case IppResolution r:
                w.UInt16(9);
                w.Int32(r.X);
                w.Int32(r.Y);
                w.Byte(r.Units);
                break;
            case IppRange range:
                w.UInt16(8);
                w.Int32(range.Lower);
                w.Int32(range.Upper);
                break;
            case byte[] bytes:
                w.UInt16((ushort)bytes.Length);
                w.Bytes(bytes);
                break;
            case string s:
                w.String16(s);
                break;
            default:
                throw new NotSupportedException($"Cannot encode {value.Value.GetType().Name}.");
        }
    }

    private static IppValue Decode(IppTag tag, byte[] raw) => tag switch
    {
        IppTag.Integer or IppTag.Enum when raw.Length == 4 => new(tag, BinaryPrimitives.ReadInt32BigEndian(raw)),
        IppTag.Boolean when raw.Length == 1 => new(tag, raw[0] != 0),
        IppTag.Resolution when raw.Length == 9 => new(tag, new IppResolution(
            BinaryPrimitives.ReadInt32BigEndian(raw), BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(4)), raw[8])),
        IppTag.RangeOfInteger when raw.Length == 8 => new(tag, new IppRange(
            BinaryPrimitives.ReadInt32BigEndian(raw), BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(4)))),
        IppTag.Unsupported or IppTag.Unknown or IppTag.NoValue => new(tag, null),
        IppTag.TextWithoutLanguage or IppTag.NameWithoutLanguage or IppTag.Keyword or IppTag.Uri or IppTag.UriScheme
            or IppTag.Charset or IppTag.NaturalLanguage or IppTag.MimeMediaType => new(tag, DecodeText(raw)),
        IppTag.TextWithLanguage or IppTag.NameWithLanguage => new(tag, DecodeWithLanguage(raw)),
        _ => new(tag, raw),
    };

    /// <summary>textWithLanguage: 2-byte language length, language, 2-byte text length, text. Keeps the text.</summary>
    private static string DecodeWithLanguage(byte[] raw)
    {
        if (raw.Length < 4)
            return "";
        var langLength = BinaryPrimitives.ReadUInt16BigEndian(raw);
        var textOffset = 2 + langLength;
        if (raw.Length < textOffset + 2)
            return "";
        var textLength = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(textOffset));
        return DecodeText(raw.AsSpan(textOffset + 2, Math.Min(textLength, raw.Length - textOffset - 2)));
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Text is UTF-8 per attributes-charset, but the Windows IPP Class Driver sends job names in
    /// the ANSI code page (e.g. "Página de prueba"); fall back to Windows-1252/Latin-1.
    /// </summary>
    internal static string DecodeText(ReadOnlySpan<byte> raw)
    {
        try
        {
            return StrictUtf8.GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(raw);
        }
    }

    private static void AddValue(ref IppAttribute? attribute, IppGroup group, string? name, IppValue value)
    {
        if (name is not null || attribute is null)
        {
            attribute = new IppAttribute(name ?? "", new List<IppValue>());
            group.Attributes.Add(attribute);
        }
        ((List<IppValue>)attribute.Values).Add(value);
    }

    private static void Flush(IppCollection collection, string? member, List<IppValue>? values)
    {
        if (member is not null && values is not null)
            collection.Add(new IppAttribute(member, values));
    }

    private sealed class Reader(Stream stream)
    {
        private readonly byte[] _scratch = new byte[4];

        public async ValueTask<byte> ByteAsync(CancellationToken ct)
        {
            await stream.ReadExactlyAsync(_scratch.AsMemory(0, 1), ct);
            return _scratch[0];
        }

        public async ValueTask<ushort> UInt16Async(CancellationToken ct)
        {
            await stream.ReadExactlyAsync(_scratch.AsMemory(0, 2), ct);
            return BinaryPrimitives.ReadUInt16BigEndian(_scratch);
        }

        public async ValueTask<int> Int32Async(CancellationToken ct)
        {
            await stream.ReadExactlyAsync(_scratch.AsMemory(0, 4), ct);
            return BinaryPrimitives.ReadInt32BigEndian(_scratch);
        }

        public async ValueTask<byte[]> BytesAsync(int length, CancellationToken ct)
        {
            var bytes = new byte[length];
            await stream.ReadExactlyAsync(bytes, ct);
            return bytes;
        }

        public async ValueTask<string> StringAsync(int length, CancellationToken ct) =>
            Encoding.UTF8.GetString(await BytesAsync(length, ct));
    }

    private sealed class BinaryWriterBE(Stream stream)
    {
        public void Byte(byte b) => stream.WriteByte(b);

        public void UInt16(ushort v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
            stream.Write(b);
        }

        public void Int32(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            stream.Write(b);
        }

        public void Bytes(byte[] bytes) => stream.Write(bytes);

        public void String16(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > ushort.MaxValue)
                throw new InvalidDataException("IPP string too long.");
            UInt16((ushort)bytes.Length);
            stream.Write(bytes);
        }
    }
}
