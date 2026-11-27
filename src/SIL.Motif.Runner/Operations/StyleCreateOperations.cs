using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation that adds one of FieldWorks' standard styles to a project that lacks it.</summary>
public static class StyleCreateOperationKinds
{
    /// <summary>
    /// Adds a standard style: {name, type}. The target is the language project; the entity is the new <c>StStyle</c>.
    /// The style's properties come from <see cref="StandardStyles"/>, never from the payload, so a style created here is
    /// the one FieldWorks' own stylesheet import would create.
    /// </summary>
    public const string Create = "styles/style/create";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, StyleCreateHandler.Instance);
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.StyleDefinitions, false);
    }
#pragma warning restore CA2255
}

/// <summary>One standard style: what FieldWorks' stylesheet says about it, kept as data.</summary>
/// <param name="Name">The style name, as FlexStyles.xml's <c>id</c> with the underscores read as spaces.</param>
/// <param name="Type">Paragraph or character.</param>
/// <param name="Guid">The guid every project gives it: FlexStyles.xml's factory guid, or Motif's own for a table style.</param>
/// <param name="Context">The style's context attribute.</param>
/// <param name="UserLevel">The style's userlevel attribute.</param>
/// <param name="Usage">The English usage text, or null.</param>
/// <param name="BasedOn">The style it is based on, or null.</param>
/// <param name="Next">The style that follows it, or null; the style's own name when it follows itself.</param>
/// <param name="Ints">The <c>(property, variation, value)</c> int text properties the stylesheet sets.</param>
public sealed record StandardStyle(string Name, StyleType Type, Guid Guid, ContextValues Context, int UserLevel, string? Usage,
    string? BasedOn, string? Next, IReadOnlyList<(int Property, int Variation, int Value)> Ints);

/// <summary>
/// The FieldWorks standard styles the Grammar Guide Markdown profile uses (ADR 0006): Normal, Heading 1 to 3, Bulleted List,
/// Numbered List, Block Quote and Hyperlink, and Motif's Table Header and Table Row.
/// </summary>
/// <remarks>
/// Every value of the first eight is read from FieldWorks' own stylesheet, <c>DistFiles/Language Explorer/FlexStyles.xml</c>,
/// the way <c>StylesXmlAccessor.CreateAndUpdateStyles</c> (Src/Common/Framework) reads it: factory guid, type, context,
/// userlevel, usage, then the <c>font</c> and <c>paragraph</c> attributes as text properties, in milli-points, and BasedOn and
/// Next in a second pass. Structure and function are the accessor's defaults for a tag with neither attribute (Undefined, Prose),
/// and the style is built-in and unmodified. Table Header and Table Row are not in that stylesheet (ADR 0005, ADR 0006) and carry fixed Motif guids: they are
/// paragraph styles based on Normal that follow themselves, with no properties of their own, so nothing is invented.
/// </remarks>
public static class StandardStyles
{
    private const int MilliPoint = (int)FwTextPropVar.ktpvMilliPoint;
    private const int Enum = (int)FwTextPropVar.ktpvEnum;
    private const int Default = (int)FwTextPropVar.ktpvDefault;
    private const int Invert = (int)FwTextToggleVal.kttvInvert;
    private const int Off = (int)FwTextToggleVal.kttvOff;
    private const int Size = (int)FwTextPropType.ktptFontSize;
    private const int Bold = (int)FwTextPropType.ktptBold;
    private const int Italic = (int)FwTextPropType.ktptItalic;
    private const int Before = (int)FwTextPropType.ktptSpaceBefore;
    private const int After = (int)FwTextPropType.ktptSpaceAfter;
    private const int Leading = (int)FwTextPropType.ktptLeadingIndent;
    private const int Trailing = (int)FwTextPropType.ktptTrailingIndent;
    private const int First = (int)FwTextPropType.ktptFirstIndent;
    private const int BulNum = (int)FwTextPropType.ktptBulNumScheme;
    private const int BulNumStart = (int)FwTextPropType.ktptBulNumStartAt;

    // FlexStyles.xml writes italic only when the tag has the attribute, so Heading 1 sets none.
    private static IReadOnlyList<(int, int, int)> Heading(bool bold, bool? italic, int size)
    {
        var props = new List<(int, int, int)> { (Bold, Enum, bold ? Invert : Off) };
        if (italic is { } on) props.Add((Italic, Enum, on ? Invert : Off));
        props.AddRange([(Size, MilliPoint, size), (Before, MilliPoint, 12000), (After, MilliPoint, 3000)]);
        return props;
    }

    /// <summary>The standard styles, in dependency order: a style's BasedOn and Next come before it, or are itself.</summary>
    public static IReadOnlyList<StandardStyle> All { get; } =
    [
        // FlexStyles.xml: size 10 pt, lineSpacing 12 pt atleast, spaceBefore 0 pt, spaceAfter 6 pt, next Normal.
        new("Normal", StyleType.kstParagraph, new Guid("7D2C80CD-AF87-4D56-8337-CFD389B775BB"), ContextValues.General, 0,
            "Normal is a parent style that all other paragraph styles are usually based on.", null, "Normal",
            [(Size, MilliPoint, 10000), ((int)FwTextPropType.ktptLineHeight, MilliPoint, 12000), (Before, MilliPoint, 0), (After, MilliPoint, 6000)]),
        new("Heading 1", StyleType.kstParagraph, new Guid("77997501-47D3-4574-AE1D-7300107DFB73"), ContextValues.General, 1,
            "Major headings (1, 2, 3)", "Normal", "Normal", Heading(true, null, 14000)),
        new("Heading 2", StyleType.kstParagraph, new Guid("3191611C-41C6-4ECF-BCD5-A5084CC3E1F2"), ContextValues.General, 1,
            "Second-level headings (1.1, 1.2, 1.3)", "Normal", "Normal", Heading(true, true, 12000)),
        new("Heading 3", StyleType.kstParagraph, new Guid("366E9470-26F7-4BDF-86E4-F829784C3113"), ContextValues.General, 1,
            "Third-level headings (1.1.1, 1.1.2, 1.1.3)", "Normal", "Normal", Heading(false, true, 12000)),
        // bulNumScheme Arabic, bulNumStartAt 1, indentLeft 21 pt, firstLine -9 pt.
        new("Numbered List", StyleType.kstParagraph, new Guid("740C5846-9D09-4109-806C-5357FEDE8712"), ContextValues.General, 1,
            "A numbered list of paragraphs", "Normal", "Numbered List",
            [(BulNum, Enum, (int)VwBulNum.kvbnArabic), (BulNumStart, Default, 1), (Leading, MilliPoint, 21000), (First, MilliPoint, -9000)]),
        // bulNumScheme="Bullet:1": FieldWorks adds the scheme number to kvbnBulletBase.
        new("Bulleted List", StyleType.kstParagraph, new Guid("3C8C511B-4683-4289-88D0-024F66540F6F"), ContextValues.General, 1,
            "A bulleted list of paragraphs", "Normal", "Bulleted List",
            [(BulNum, Enum, (int)VwBulNum.kvbnBulletBase + 1), (BulNumStart, Default, 1), (Leading, MilliPoint, 21000), (First, MilliPoint, -9000)]),
        // indentLeft 21 pt, indentRight 21 pt, firstLine "0pt", which the accessor reads as 0.
        new("Block Quote", StyleType.kstParagraph, new Guid("879D3C6F-8E62-4887-A4AF-27FEB88C2AF1"), ContextValues.General, 1,
            "Extended (paragraph size or larger) quotation", "Normal", "Block Quote",
            [(Leading, MilliPoint, 21000), (Trailing, MilliPoint, 21000), (First, MilliPoint, 0)]),
        // Context internal; blue, single underline, blue underline colour, as (blue*256+green)*256+red.
        new("Hyperlink", StyleType.kstCharacter, new Guid("7FF691E7-32F4-445D-ACEA-1DED6A6AC8EA"), ContextValues.Internal, 1,
            "Links to other material via URLs", null, null,
            [((int)FwTextPropType.ktptForeColor, Default, 0xFF0000), ((int)FwTextPropType.ktptUnderColor, Default, 0xFF0000),
                ((int)FwTextPropType.ktptUnderline, Enum, (int)FwUnderlineType.kuntSingle)]),
        new("Table Header", StyleType.kstParagraph, new Guid("318fcbf8-cdaf-4337-9d4d-64e21e11db07"), ContextValues.General, 1, null, "Normal", "Table Header", []),
        new("Table Row", StyleType.kstParagraph, new Guid("336c3572-4997-4131-9a39-100c19a0e203"), ContextValues.General, 1, null, "Normal", "Table Row", []),
    ];

    /// <summary>The standard style of that name and type, or null when the name is not a standard style.</summary>
    public static StandardStyle? Find(string name, StyleType type) =>
        All.FirstOrDefault(style => style.Type == type && string.Equals(style.Name, name, StringComparison.Ordinal));

    /// <summary>The style, then the styles its BasedOn and Next name, each before the style that needs it.</summary>
    public static IEnumerable<StandardStyle> WithDependencies(StandardStyle style)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Walk(style).ToList();

        IEnumerable<StandardStyle> Walk(StandardStyle current)
        {
            if (!seen.Add(current.Name)) yield break;
            foreach (var name in new[] { current.BasedOn, current.Next })
                if (name is not null && Find(name, StyleType.kstParagraph) is { } dependency)
                    foreach (var inner in Walk(dependency)) yield return inner;
            yield return current;
        }
    }
}

/// <summary>Adds one standard style, as FieldWorks' stylesheet import would.</summary>
internal sealed class StyleCreateHandler : IOperationHandler
{
    internal static readonly StyleCreateHandler Instance = new();
    private StyleCreateHandler() { }

    private const string Kind = StyleCreateOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var definition = Definition(operation);
        if (entityId.ToGuid() != definition.Guid)
            throw new InvalidOperationException($"'{Kind}': the style '{definition.Name}' must carry its standard identity {definition.Guid}.");
        var (id, _) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        CreateIdentity.RequireFresh(cache, entityId, Kind, "StStyle");
        if (Find(cache, definition.Name, definition.Type) is not null)
            throw new InvalidOperationException($"'{Kind}': the project already has the {TypeName(definition.Type)} style '{definition.Name}'; stage the edit again.");
        var before = Read(cache, entityId, definition);

        var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create(cache, entityId.ToGuid());
        cache.LangProject.StylesOC.Add(style);
        style.IsBuiltIn = true;
        style.IsModified = false;
        style.Name = definition.Name;
        style.Type = definition.Type;
        style.Context = definition.Context;
        style.Structure = StructureValues.Undefined;
        style.Function = FunctionValues.Prose;
        style.UserLevel = definition.UserLevel;
        if (definition.Usage is not null && cache.WritingSystemFactory.GetWsFromStr("en") is var en and > 0)
            style.Usage.set_String(en, TsStringUtils.MakeString(definition.Usage, en));
        var props = TsStringUtils.MakePropsBldr();
        foreach (var (property, variation, value) in definition.Ints) props.SetIntPropValues(property, variation, value);
        style.Rules = props.GetTextProps();
        if (definition.BasedOn is not null) style.BasedOnRA = Find(cache, definition.BasedOn, definition.Type) ?? throw Missing(definition.BasedOn);
        if (definition.Next is not null)
            style.NextRA = definition.Next == definition.Name ? style : Find(cache, definition.Next, definition.Type) ?? throw Missing(definition.Next);
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.StyleDefinitions, before, Read(cache, entityId, definition));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var (id, _) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        var current = Read(cache, entityId, Definition(operation));
        return new ExpectedEffect(id, SnapshotFields.StyleDefinitions, current, current);
    }

    private static InvalidOperationException Missing(string name) =>
        new($"'{Kind}': the style '{name}' that this one is based on or followed by is not in the project; stage it first.");

    private static string TypeName(StyleType type) => type == StyleType.kstCharacter ? "character" : "paragraph";

    private static StandardStyle Definition(OperationEnvelope operation)
    {
        var after = operation.After ?? throw new InvalidOperationException($"'{Kind}' requires after.");
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["name", "type"], Kind);
        var name = ClosedPayloadParsing.GetRequiredString(after, "name", Kind);
        var type = ClosedPayloadParsing.GetRequiredString(after, "type", Kind) switch
        {
            "paragraph" => StyleType.kstParagraph,
            "character" => StyleType.kstCharacter,
            var other => throw new InvalidOperationException($"'{Kind}': type must be 'paragraph' or 'character', not '{other}'."),
        };
        return StandardStyles.Find(name, type) ?? throw new InvalidOperationException(
            $"'{Kind}': '{name}' is not a FieldWorks standard {TypeName(type)} style Motif can create.");
    }

    private static IStStyle? Find(LcmCache cache, string name, StyleType type) =>
        cache.LangProject.StylesOC.FirstOrDefault(style => style.Name == name && style.Type == type);

    // The footprint: whether the style exists, the identity names something else, or another style holds the name.
    private static Dictionary<string, string> Read(LcmCache cache, CanonicalId entityId, StandardStyle definition)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (cache.LangProject.StylesOC.Any(style => style.Guid == entityId.ToGuid())) result["ref"] = entityId.Value;
        else if (repository.IsValidObjectId(entityId.ToGuid())) result["collision"] = entityId.Value;
        if (Find(cache, definition.Name, definition.Type) is { } holder && holder.Guid != entityId.ToGuid())
            result["nameHeldBy"] = CanonicalId.FromGuid(holder.Guid).Value;
        return result;
    }
}
