﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.VisualStudio.LanguageServer.ContainedLanguage;
using Microsoft.VisualStudio.LanguageServerClient.Razor.HtmlCSharp;
using CodeAnalysisWorkspace = Microsoft.CodeAnalysis.Workspace;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    [Shared]
    [Export(typeof(LSPDocumentManagerChangeTrigger))]
    internal class CSharpVirtualDocumentPublisher : LSPDocumentManagerChangeTrigger
    {
        private readonly RazorDynamicFileInfoProvider _dynamicFileInfoProvider;
        private readonly LSPDocumentMappingProvider _lspDocumentMappingProvider;

        [ImportingConstructor]
        public CSharpVirtualDocumentPublisher(RazorDynamicFileInfoProvider dynamicFileInfoProvider, LSPDocumentMappingProvider lspDocumentMappingProvider)
        {
            if (dynamicFileInfoProvider is null)
            {
                throw new ArgumentNullException(nameof(dynamicFileInfoProvider));
            }

            _dynamicFileInfoProvider = dynamicFileInfoProvider;
            _lspDocumentMappingProvider = lspDocumentMappingProvider;
        }

        public override void Initialize(LSPDocumentManager documentManager)
        {
            if (documentManager is null)
            {
                throw new ArgumentNullException(nameof(documentManager));
            }

            documentManager.Changed += DocumentManager_Changed;
        }

        // Internal for testing
        internal void DocumentManager_Changed(object sender, LSPDocumentChangeEventArgs args)
        {
            if (args.Kind != LSPDocumentChangeKind.VirtualDocumentChanged)
            {
                return;
            }

            if (args.VirtualNew is CSharpVirtualDocumentSnapshot)
            {
                var csharpContainer = new CSharpVirtualDocumentContainer(_lspDocumentMappingProvider, args.New, args.VirtualNew.Snapshot);
                _dynamicFileInfoProvider.UpdateLSPFileInfo(args.New.Uri, csharpContainer);
            }
        }

        private class CSharpVirtualDocumentContainer : DynamicDocumentContainer
        {
            private readonly ITextSnapshot _textSnapshot;
            private readonly LSPDocumentMappingProvider _lspDocumentMappingProvider;
            private readonly LSPDocumentSnapshot _documentSnapshot;
            private IRazorSpanMappingService _mappingService;
            private IRazorDocumentExcerptService _excerptService;

            public override string FilePath => _documentSnapshot.Uri.LocalPath;

            public CSharpVirtualDocumentContainer(LSPDocumentMappingProvider lspDocumentMappingProvider, LSPDocumentSnapshot documentSnapshot, ITextSnapshot textSnapshot)
            {
                if (lspDocumentMappingProvider is null)
                {
                    throw new ArgumentNullException(nameof(lspDocumentMappingProvider));
                }

                if (textSnapshot is null)
                {
                    throw new ArgumentNullException(nameof(textSnapshot));
                }

                if (documentSnapshot is null)
                {
                    throw new ArgumentNullException(nameof(documentSnapshot));
                }

                _lspDocumentMappingProvider = lspDocumentMappingProvider;

                _textSnapshot = textSnapshot;
                _documentSnapshot = documentSnapshot;
            }

            public override IRazorDocumentExcerptService GetExcerptService()
            {
                if (_excerptService == null)
                {
                    var mappingService = GetMappingService();
                    _excerptService = new CSharpDocumentExcerptService(mappingService, _documentSnapshot);
                }

                return _excerptService;
            }

            public override IRazorSpanMappingService GetMappingService()
            {
                if (_mappingService == null)
                {
                    _mappingService = new CSharpSpanMappingService(_lspDocumentMappingProvider, _documentSnapshot, _textSnapshot);
                }

                return _mappingService;
            }

            public override IRazorDocumentPropertiesService GetDocumentPropertiesService()
            {
                return CSharpDocumentPropertiesService.Instance;
            }

            public override TextLoader GetTextLoader(string filePath)
            {
                var sourceText = _textSnapshot.AsText();
                var textLoader = new SourceTextLoader(sourceText, filePath);
                return textLoader;
            }

            private sealed class SourceTextLoader : TextLoader
            {
                private readonly SourceText _sourceText;
                private readonly string _filePath;

                public SourceTextLoader(SourceText sourceText, string filePath)
                {
                    if (sourceText is null)
                    {
                        throw new ArgumentNullException(nameof(sourceText));
                    }

                    if (filePath is null)
                    {
                        throw new ArgumentNullException(nameof(filePath));
                    }

                    _sourceText = sourceText;
                    _filePath = filePath;
                }

                public override Task<TextAndVersion> LoadTextAndVersionAsync(CodeAnalysisWorkspace workspace, DocumentId documentId, CancellationToken cancellationToken)
                {
                    return Task.FromResult(TextAndVersion.Create(_sourceText, VersionStamp.Default, _filePath));
                }
            }
        }
    }
}
