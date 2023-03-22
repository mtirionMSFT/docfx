﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Dynamic;
using System.Text;

using Newtonsoft.Json;

using Microsoft.DocAsCode.Build.Common;
using Microsoft.DocAsCode.Build.SchemaDriven.Processors;
using Microsoft.DocAsCode.Common;
using Microsoft.DocAsCode.MarkdigEngine;
using Microsoft.DocAsCode.Plugins;

namespace Microsoft.DocAsCode.Build.SchemaDriven;

public class SchemaDrivenDocumentProcessor : DisposableDocumentProcessor
{
    #region Fields

    private readonly ResourcePoolManager<JsonSerializer> _serializerPool;
    private readonly string _schemaName;
    private readonly DocumentSchema _schema;
    private readonly bool _allowOverwrite;
    private readonly MarkdigMarkdownService _markdigMarkdownService;
    private readonly FolderRedirectionManager _folderRedirectionManager;
    private readonly string _siteHostName;
    #endregion

    public SchemaValidator SchemaValidator { get; }
    #region Constructors

    public SchemaDrivenDocumentProcessor(
        DocumentSchema schema,
        ICompositionContainer container,
        MarkdigMarkdownService markdigMarkdownService,
        FolderRedirectionManager folderRedirectionManager,
        string siteHostName = null)
    {
        if (string.IsNullOrWhiteSpace(schema.Title))
        {
            throw new ArgumentException("Title for schema must not be empty");
        }

        _schemaName = schema.Title;
        _schema = schema;
        SchemaValidator = schema.Validator;
        _allowOverwrite = schema.AllowOverwrite;
        _serializerPool = new ResourcePoolManager<JsonSerializer>(GetSerializer, 0x10);
        _markdigMarkdownService = markdigMarkdownService ?? throw new ArgumentNullException(nameof(MarkdigMarkdownService));
        _folderRedirectionManager = folderRedirectionManager;
        if (container != null)
        {
            var commonSteps = container.GetExports<IDocumentBuildStep>(nameof(SchemaDrivenDocumentProcessor));
            var schemaSpecificSteps = container.GetExports<IDocumentBuildStep>($"{nameof(SchemaDrivenDocumentProcessor)}.{_schemaName}");
            BuildSteps = commonSteps.Union(schemaSpecificSteps).ToList();
        }
        _siteHostName = siteHostName;

    }

    #endregion

    #region IDocumentProcessor Members

    public override IEnumerable<IDocumentBuildStep> BuildSteps { get; set; }

    public override string Name => _schema.Title;

    public override ProcessingPriority GetProcessingPriority(FileAndType file)
    {
        switch (file.Type)
        {
            case DocumentType.Article:
                if (".yml".Equals(Path.GetExtension(file.File), StringComparison.OrdinalIgnoreCase) ||
                    ".yaml".Equals(Path.GetExtension(file.File), StringComparison.OrdinalIgnoreCase))
                {
                    var mime = YamlMime.ReadMime(file.File);
                    if (string.Equals(mime, YamlMime.YamlMimePrefix + _schemaName))
                    {
                        return ProcessingPriority.Normal;
                    }

                    return ProcessingPriority.NotSupported;
                }

                break;
            case DocumentType.Overwrite:
                if (_allowOverwrite && ".md".Equals(Path.GetExtension(file.File), StringComparison.OrdinalIgnoreCase))
                {
                    return ProcessingPriority.Normal;
                }
                break;
            default:
                break;
        }
        return ProcessingPriority.NotSupported;
    }

    public override FileModel Load(FileAndType file, ImmutableDictionary<string, object> metadata)
    {
        switch (file.Type)
        {
            case DocumentType.Article:
                // TODO: Support dynamic in YAML deserializer
                try
                {
                    // MUST be a dictionary
                    var obj = YamlUtility.Deserialize<Dictionary<string, object>>(file.File);

                    // load overwrite fragments
                    string markdownFragmentsContent = null;
                    var markdownFragmentsFile = file.File + ".md";
                    if (_folderRedirectionManager != null)
                    {
                        markdownFragmentsFile = _folderRedirectionManager.GetRedirectedPath((RelativePath)markdownFragmentsFile).ToString();
                    }
                    if (EnvironmentContext.FileAbstractLayer.Exists(markdownFragmentsFile))
                    {
                        markdownFragmentsContent = EnvironmentContext.FileAbstractLayer.ReadAllText(markdownFragmentsFile);
                    }
                    else
                    {
                        // Validate against the schema first, only when markdown fragments don't exist
                        SchemaValidator.Validate(obj);
                    }

                    var content = ConvertToObjectHelper.ConvertToDynamic(obj);
                    if (!(_schema.MetadataReference.GetValue(content) is IDictionary<string, object> pageMetadata))
                    {
                        pageMetadata = new ExpandoObject();
                        _schema.MetadataReference.SetValue(ref content, pageMetadata);
                    }
                    foreach (var (key, value) in metadata.OrderBy(item => item.Key))
                    {
                        pageMetadata[key] = value;
                    }

                    var localPathFromRoot = PathUtility.MakeRelativePath(EnvironmentContext.BaseDirectory, EnvironmentContext.FileAbstractLayer.GetPhysicalPath(file.File));

                    var fm = new FileModel(file, content)
                    {
                        LocalPathFromRoot = localPathFromRoot,
                    };
                    fm.MarkdownFragmentsModel = new FileModel(
                        new FileAndType(
                            file.BaseDir,
                            markdownFragmentsFile,
                            DocumentType.MarkdownFragments,
                            file.SourceDir,
                            file.DestinationDir),
                        markdownFragmentsContent);
                    fm.Properties.Schema = _schema;
                    fm.Properties.Metadata = pageMetadata;
                    fm.MarkdownFragmentsModel.Properties.MarkdigMarkdownService = _markdigMarkdownService;
                    fm.MarkdownFragmentsModel.Properties.Metadata = pageMetadata;
                    if (markdownFragmentsContent != null)
                    {
                        fm.MarkdownFragmentsModel.LocalPathFromRoot = PathUtility.MakeRelativePath(
                            EnvironmentContext.BaseDirectory,
                            EnvironmentContext.FileAbstractLayer.GetPhysicalPath(markdownFragmentsFile));
                    }
                    return fm;
                }
                catch (YamlDotNet.Core.YamlException e)
                {
                    var message = $"{file.File} is not in supported format: {e.Message}";
                    Logger.LogError(message, code: ErrorCodes.Build.InvalidYamlFile);
                    throw new DocumentException(message, e);
                }
            case DocumentType.Overwrite:
                return OverwriteDocumentReader.Read(file);
            default:
                throw new NotSupportedException();
        }
    }

    public override SaveResult Save(FileModel model)
    {
        if (model.Type != DocumentType.Article)
        {
            throw new NotSupportedException();
        }

        var result = new SaveResult
        {
            DocumentType = model.DocumentType ?? _schemaName,
            FileWithoutExtension = Path.ChangeExtension(model.File, null),
            LinkToFiles = model.LinkToFiles.ToImmutableArray(),
            LinkToUids = model.LinkToUids,
            FileLinkSources = model.FileLinkSources,
            UidLinkSources = model.UidLinkSources,
            XRefSpecs = ImmutableArray.CreateRange(model.Properties.XRefSpecs),
            ExternalXRefSpecs = ImmutableArray.CreateRange(model.Properties.ExternalXRefSpecs)
        };

        if (((IDictionary<string, object>)model.Properties).ContainsKey("XrefSpec"))
        {
            result.XRefSpecs = ImmutableArray.Create(model.Properties.XrefSpec);
        }

        return result;
    }

    public override void UpdateHref(FileModel model, IDocumentBuildContext context)
    {
        var content = model.Content;
        var pc = new ProcessContext(null, model, context);
        DocumentSchema schema = model.Properties.Schema;
        model.Content = new SchemaProcessor(
            new HrefInterpreter(false, true, _siteHostName),
            new FileInterpreter(false, true),
            new XrefInterpreter(false, true)
            ).Process(content, schema, pc);
    }

    #endregion

    #region Protected Methods

    protected virtual void SerializeModel(object model, Stream stream)
    {
        using var sw = new StreamWriter(stream, Encoding.UTF8, 0x100, true);
        using var lease = _serializerPool.Rent();
        lease.Resource.Serialize(sw, model);
    }

    protected virtual object DeserializeModel(Stream stream)
    {
        using var sr = new StreamReader(stream, Encoding.UTF8, false, 0x100, true);
        using var jr = new JsonTextReader(sr);
        using var lease = _serializerPool.Rent();
        return lease.Resource.Deserialize(jr);
    }

    protected virtual void SerializeProperties(IDictionary<string, object> properties, Stream stream)
    {
        using var sw = new StreamWriter(stream, Encoding.UTF8, 0x100, true);
        using var lease = _serializerPool.Rent();
        lease.Resource.Serialize(sw, properties);
    }

    protected virtual IDictionary<string, object> DeserializeProperties(Stream stream)
    {
        using var sr = new StreamReader(stream, Encoding.UTF8, false, 0x100, true);
        using var jr = new JsonTextReader(sr);
        using var lease = _serializerPool.Rent();
        return lease.Resource.Deserialize<Dictionary<string, object>>(jr);
    }

    protected virtual JsonSerializer GetSerializer()
    {
        return new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
            Converters =
            {
                new Newtonsoft.Json.Converters.StringEnumConverter(),
            },
            TypeNameHandling = TypeNameHandling.All, // lgtm [cs/unsafe-type-name-handling]
        };
    }

    #endregion
}
