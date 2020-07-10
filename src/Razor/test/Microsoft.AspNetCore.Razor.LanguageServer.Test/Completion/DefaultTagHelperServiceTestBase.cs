﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Components;
using Microsoft.AspNetCore.Razor.Test.Common;
using Microsoft.VisualStudio.Editor.Razor;
using DefaultRazorTagHelperCompletionService = Microsoft.VisualStudio.Editor.Razor.DefaultTagHelperCompletionService;
using RazorTagHelperCompletionService = Microsoft.VisualStudio.Editor.Razor.TagHelperCompletionService;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Completion
{
    public abstract class DefaultTagHelperServiceTestBase : LanguageServerTestBase
    {
        public DefaultTagHelperServiceTestBase()
        {
            var builder1 = TagHelperDescriptorBuilder.Create("Test1TagHelper", "TestAssembly");
            builder1.TagMatchingRule(rule => rule.TagName = "test1");
            builder1.SetTypeName("Test1TagHelper");
            builder1.BindAttribute(attribute =>
            {
                attribute.Name = "bool-val";
                attribute.SetPropertyName("BoolVal");
                attribute.TypeName = typeof(bool).FullName;
            });
            builder1.BindAttribute(attribute =>
            {
                attribute.Name = "int-val";
                attribute.SetPropertyName("IntVal");
                attribute.TypeName = typeof(int).FullName;
            });

            var builder2 = TagHelperDescriptorBuilder.Create("Test2TagHelper", "TestAssembly");
            builder2.TagMatchingRule(rule => rule.TagName = "test2");
            builder2.SetTypeName("Test2TagHelper");
            builder2.BindAttribute(attribute =>
            {
                attribute.Name = "bool-val";
                attribute.SetPropertyName("BoolVal");
                attribute.TypeName = typeof(bool).FullName;
            });
            builder2.BindAttribute(attribute =>
            {
                attribute.Name = "int-val";
                attribute.SetPropertyName("IntVal");
                attribute.TypeName = typeof(int).FullName;
            });

            var directiveAttribute1 = TagHelperDescriptorBuilder.Create(ComponentMetadata.Component.TagHelperKind, "TestDirectiveAttribute", "TestAssembly");
            directiveAttribute1.TagMatchingRule(rule =>
            {
                rule.TagName = "*";
                rule.RequireAttributeDescriptor(b =>
                {
                    b.Name = "@test";
                    b.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.PrefixMatch;
                });
            });
            directiveAttribute1.TagMatchingRule(rule =>
            {
                rule.TagName = "*";
                rule.RequireAttributeDescriptor(b =>
                {
                    b.Name = "@test";
                    b.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                });
            });
            directiveAttribute1.BindAttribute(attribute =>
            {
                attribute.Metadata[ComponentMetadata.Common.DirectiveAttribute] = bool.TrueString;
                attribute.Name = "@test";
                attribute.SetPropertyName("Test");
                attribute.TypeName = typeof(string).FullName;

                attribute.BindAttributeParameter(parameter =>
                {
                    parameter.Name = "something";
                    parameter.TypeName = typeof(string).FullName;

                    parameter.SetPropertyName("Something");
                });
            });
            directiveAttribute1.Metadata[ComponentMetadata.Component.NameMatchKey] = ComponentMetadata.Component.FullyQualifiedNameMatch;
            directiveAttribute1.SetTypeName("TestDirectiveAttribute");

            var directiveAttribute2 = TagHelperDescriptorBuilder.Create(ComponentMetadata.Component.TagHelperKind, "MinimizedDirectiveAttribute", "TestAssembly");
            directiveAttribute2.TagMatchingRule(rule =>
            {
                rule.TagName = "*";
                rule.RequireAttributeDescriptor(b =>
                {
                    b.Name = "@minimized";
                    b.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.PrefixMatch;
                });
            });
            directiveAttribute2.TagMatchingRule(rule =>
            {
                rule.TagName = "*";
                rule.RequireAttributeDescriptor(b =>
                {
                    b.Name = "@minimized";
                    b.NameComparisonMode = RequiredAttributeDescriptor.NameComparisonMode.FullMatch;
                });
            });
            directiveAttribute2.BindAttribute(attribute =>
            {
                attribute.Metadata[ComponentMetadata.Common.DirectiveAttribute] = bool.TrueString;
                attribute.Name = "@minimized";
                attribute.SetPropertyName("Minimized");
                attribute.TypeName = typeof(bool).FullName;

                attribute.BindAttributeParameter(parameter =>
                {
                    parameter.Name = "something";
                    parameter.TypeName = typeof(string).FullName;

                    parameter.SetPropertyName("Something");
                });
            });
            directiveAttribute2.Metadata[ComponentMetadata.Component.NameMatchKey] = ComponentMetadata.Component.FullyQualifiedNameMatch;
            directiveAttribute2.SetTypeName("TestDirectiveAttribute");

            DefaultTagHelpers = new[] { builder1.Build(), builder2.Build(), directiveAttribute1.Build(), directiveAttribute2.Build() };

            HtmlFactsService = new DefaultHtmlFactsService();
            TagHelperFactsService = new DefaultTagHelperFactsService();
            RazorTagHelperCompletionService = new DefaultRazorTagHelperCompletionService(TagHelperFactsService);
        }

        protected TagHelperDescriptor[] DefaultTagHelpers { get; }

        protected RazorTagHelperCompletionService RazorTagHelperCompletionService { get; }

        internal HtmlFactsService HtmlFactsService { get; }

        protected TagHelperFactsService TagHelperFactsService { get; }

        internal static RazorCodeDocument CreateCodeDocument(string text, params TagHelperDescriptor[] tagHelpers)
        {
            return CreateCodeDocument(text, "test.cshtml", tagHelpers);
        }

        internal static RazorCodeDocument CreateRazorDocument(string text, params TagHelperDescriptor[] tagHelpers)
        {
            return CreateCodeDocument(text, "test.razor", tagHelpers);
        }

        internal static RazorCodeDocument CreateCodeDocument(string text, string filePath, params TagHelperDescriptor[] tagHelpers)
        {
            tagHelpers = tagHelpers ?? Array.Empty<TagHelperDescriptor>();
            var sourceDocument = TestRazorSourceDocument.Create(text, filePath: filePath, relativePath: filePath);
            var projectEngine = RazorProjectEngine.Create(builder => { });
            var fileKind = filePath.EndsWith(".razor", StringComparison.Ordinal) ? FileKinds.Component : FileKinds.Legacy;
            var codeDocument = projectEngine.ProcessDesignTime(sourceDocument, fileKind, Array.Empty<RazorSourceDocument>(), tagHelpers);
            return codeDocument;
        }
    }
}
