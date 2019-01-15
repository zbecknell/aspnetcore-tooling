// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Razor.Language;

namespace Microsoft.CodeAnalysis.Razor.ProjectSystem
{
    internal sealed class ProjectWorkspaceState
    {
        public static readonly ProjectWorkspaceState Default = new ProjectWorkspaceState(Array.Empty<TagHelperDescriptor>());

        public ProjectWorkspaceState(IReadOnlyList<TagHelperDescriptor> tagHelpers)
        {
            if (tagHelpers == null)
            {
                throw new ArgumentNullException(nameof(tagHelpers));
            }

            TagHelpers = tagHelpers;
        }

        public IReadOnlyList<TagHelperDescriptor> TagHelpers { get; }
    }
}