namespace MiniPrinter.Ipp;

/// <summary>Attribute group delimiter tags (RFC 8010 §3.5.1).</summary>
public enum IppGroupTag : byte
{
    Operation = 0x01,
    Job = 0x02,
    End = 0x03,
    Printer = 0x04,
    Unsupported = 0x05,
}

/// <summary>Value tags (RFC 8010 §3.5.2) used by this server.</summary>
public enum IppTag : byte
{
    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,
    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BeginCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A,
}

public static class IppOperation
{
    public const short PrintJob = 0x0002;
    public const short ValidateJob = 0x0004;
    public const short CreateJob = 0x0005;
    public const short SendDocument = 0x0006;
    public const short CancelJob = 0x0008;
    public const short GetJobAttributes = 0x0009;
    public const short GetJobs = 0x000A;
    public const short GetPrinterAttributes = 0x000B;
    public const short CloseJob = 0x003B;
    public const short IdentifyPrinter = 0x003C;
}

public static class IppStatus
{
    public const short SuccessfulOk = 0x0000;
    public const short SuccessfulOkIgnoredOrSubstituted = 0x0001;
    public const short ClientErrorBadRequest = 0x0400;
    public const short ClientErrorNotPossible = 0x0404;
    public const short ClientErrorNotFound = 0x0406;
    public const short ClientErrorDocumentFormatNotSupported = 0x040A;
    public const short ClientErrorAttributesOrValuesNotSupported = 0x040B;
    public const short ServerErrorInternalError = 0x0500;
    public const short ServerErrorOperationNotSupported = 0x0501;
    public const short ServerErrorVersionNotSupported = 0x0503;
}

public readonly record struct IppResolution(int X, int Y, byte Units = IppResolution.DotsPerInch)
{
    public const byte DotsPerInch = 3;
}

public readonly record struct IppRange(int Lower, int Upper);

/// <summary>A collection value: an ordered list of member attributes.</summary>
public sealed class IppCollection : List<IppAttribute>
{
    public IppCollection() { }

    public IppCollection(IEnumerable<IppAttribute> members) : base(members) { }
}

/// <summary>
/// One typed value. <see cref="Value"/> is an <c>int</c>, <c>bool</c>, <c>string</c>,
/// <c>byte[]</c>, <see cref="IppResolution"/>, <see cref="IppRange"/>, <see cref="IppCollection"/>
/// or null (out-of-band tags).
/// </summary>
public readonly record struct IppValue(IppTag Tag, object? Value)
{
    public static IppValue Integer(int v) => new(IppTag.Integer, v);
    public static IppValue Enum(int v) => new(IppTag.Enum, v);
    public static IppValue Boolean(bool v) => new(IppTag.Boolean, v);
    public static IppValue Keyword(string v) => new(IppTag.Keyword, v);
    public static IppValue Text(string v) => new(IppTag.TextWithoutLanguage, v);
    public static IppValue Name(string v) => new(IppTag.NameWithoutLanguage, v);
    public static IppValue Uri(string v) => new(IppTag.Uri, v);
    public static IppValue Charset(string v) => new(IppTag.Charset, v);
    public static IppValue Language(string v) => new(IppTag.NaturalLanguage, v);
    public static IppValue Mime(string v) => new(IppTag.MimeMediaType, v);
    public static IppValue Resolution(int dpi) => new(IppTag.Resolution, new IppResolution(dpi, dpi));
    public static IppValue Range(int lower, int upper) => new(IppTag.RangeOfInteger, new IppRange(lower, upper));
    public static IppValue Collection(IppCollection c) => new(IppTag.BeginCollection, c);
    public static IppValue NoValue => new(IppTag.NoValue, null);

    public int AsInt() => Value is int i ? i : throw new InvalidCastException($"{Tag} is not an integer.");
    public string AsString() => Value as string ?? throw new InvalidCastException($"{Tag} is not a string.");

    public override string ToString() => Value switch
    {
        null => Tag.ToString(),
        IppCollection c => "{" + string.Join(" ", c) + "}",
        _ => Value.ToString() ?? "",
    };
}

public sealed record IppAttribute(string Name, IReadOnlyList<IppValue> Values)
{
    public IppAttribute(string name, params IppValue[] values) : this(name, (IReadOnlyList<IppValue>)values) { }

    public IppValue Value => Values[0];

    public override string ToString() => $"{Name}={string.Join(",", Values)}";
}

public sealed class IppGroup
{
    public IppGroup(IppGroupTag tag, IEnumerable<IppAttribute>? attributes = null)
    {
        Tag = tag;
        Attributes = attributes?.ToList() ?? [];
    }

    public IppGroupTag Tag { get; }
    public List<IppAttribute> Attributes { get; }

    public IppAttribute? this[string name] => Attributes.FirstOrDefault(a => a.Name == name);

    public IppGroup Add(string name, params IppValue[] values)
    {
        Attributes.Add(new IppAttribute(name, values));
        return this;
    }
}

/// <summary>An IPP request or response (code = operation id or status code).</summary>
public sealed class IppMessage
{
    public byte VersionMajor { get; set; } = 2;
    public byte VersionMinor { get; set; }
    public short Code { get; set; }
    public int RequestId { get; set; }
    public List<IppGroup> Groups { get; } = [];

    public IppGroup? Group(IppGroupTag tag) => Groups.FirstOrDefault(g => g.Tag == tag);

    public IppGroup GetOrAddGroup(IppGroupTag tag)
    {
        var group = Group(tag);
        if (group is null)
        {
            group = new IppGroup(tag);
            Groups.Add(group);
        }
        return group;
    }

    /// <summary>Finds an operation attribute.</summary>
    public IppAttribute? Operation(string name) => Group(IppGroupTag.Operation)?[name];

    /// <summary>Creates a response with the mandatory charset and language attributes.</summary>
    public static IppMessage Response(IppMessage request, short status, string? message = null)
    {
        var response = new IppMessage
        {
            VersionMajor = Math.Min(request.VersionMajor, (byte)2),
            VersionMinor = request.VersionMajor >= 2 ? (byte)0 : request.VersionMinor,
            Code = status,
            RequestId = request.RequestId,
        };
        var operation = response.GetOrAddGroup(IppGroupTag.Operation)
            .Add("attributes-charset", IppValue.Charset("utf-8"))
            .Add("attributes-natural-language", IppValue.Language("en"));
        if (message is not null)
            operation.Add("status-message", IppValue.Text(message));
        return response;
    }
}
