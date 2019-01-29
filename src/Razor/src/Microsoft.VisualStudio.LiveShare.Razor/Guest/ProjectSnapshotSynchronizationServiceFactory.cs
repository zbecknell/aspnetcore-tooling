// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.VisualStudio.LiveShare.Razor.Guest
{
    [ExportCollaborationService(typeof(ProjectSnapshotSynchronizationService), Scope = SessionScope.Guest)]
    internal class ProjectSnapshotSynchronizationServiceFactory : ICollaborationServiceFactory
    {
        private readonly ProxyAccessor _proxyAccessor;
        private readonly JoinableTaskContext _joinableTaskContext;
        private readonly LiveShareClientProvider _liveShareClientProvider;
        private readonly Workspace _workspace;

        // TODO: CHANGED THE TYPE OF IMPORT, DOES THIS WORK?
        [ImportingConstructor]
        public ProjectSnapshotSynchronizationServiceFactory(
            ProxyAccessor proxyAccessor,
            JoinableTaskContext joinableTaskContext,
            LiveShareClientProvider liveShareClientProvider,
            [Import(typeof(VisualStudioWorkspace))] Workspace workspace)
        {
            if (proxyAccessor == null)
            {
                throw new ArgumentNullException(nameof(proxyAccessor));
            }

            if (joinableTaskContext == null)
            {
                throw new ArgumentNullException(nameof(joinableTaskContext));
            }

            if (liveShareClientProvider == null)
            {
                throw new ArgumentNullException(nameof(liveShareClientProvider));
            }

            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            _proxyAccessor = proxyAccessor;
            _joinableTaskContext = joinableTaskContext;
            _liveShareClientProvider = liveShareClientProvider;
            _workspace = workspace;
        }

        public async Task<ICollaborationService> CreateServiceAsync(CollaborationSession sessionContext, CancellationToken cancellationToken)
        {
            var languageServices = _workspace.Services.GetLanguageServices(RazorLanguage.Name);
            var projectManager = (ProjectSnapshotManagerBase)languageServices.GetRequiredService<ProjectSnapshotManager>();
            
            var synchronizationService = new ProjectSnapshotSynchronizationService(
                _joinableTaskContext.Factory,
                _liveShareClientProvider,
                _proxyAccessor.GetProjectSnapshotManagerProxy(),
                projectManager);

            await synchronizationService.InitializeAsync(cancellationToken);

            return synchronizationService;
        }
    }
}
