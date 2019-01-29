// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;

namespace Microsoft.VisualStudio.LiveShare.Razor
{
    public sealed class ProjectSnapshotHandleProxy
    {
        public ProjectSnapshotHandleProxy(
            Uri filePath,
            ProjectWorkspaceState projectWorkspaceState,
            RazorConfiguration configuration)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            FilePath = filePath;
            ProjectWorkspaceState = projectWorkspaceState;
            Configuration = configuration;
        }

        public RazorConfiguration Configuration { get; }

        public ProjectWorkspaceState ProjectWorkspaceState { get; }

        public Uri FilePath { get; }
    }
}
